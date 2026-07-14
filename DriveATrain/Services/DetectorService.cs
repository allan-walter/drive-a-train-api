using System.Diagnostics;
using DriveATrain.Hubs;
using DriveATrain.OpenCv;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using OpenCvSharp;

namespace DriveATrain.Services;

public class DetectorService(
    LimiterService limiterService,
    CaptureService captureService,
    DccService dccService,
    IHubContext<UnitHub> unitHub,
    Config config) : IHostedService, IDisposable
{
    private BackgroundSubtractorMOG2 _mog2;

    private static readonly Size Blur = new Size(9, 9);

    Mat goZone = Cv2.ImRead(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "DriveATrain",
        "Static Images/go zone.png"), ImreadModes.Grayscale);

    private CancellationTokenSource token = new CancellationTokenSource();

    // public static double CAMERA_WIDTH = 640.0;
    // public static double CAMERA_HEIGHT = 360.0;
    public static double CAMERA_WIDTH = 1920.0;
    public static double CAMERA_HEIGHT = 1080.0;

    public static int STREAM_WIDTH = 1920;
    public static int STREAM_HEIGHT = 1080;
    public static int STREAM_FPS = 30;

    public Mat currentDebugFrame = new Mat();

    private LiveData? _pendingLiveData;
    private List<Uncouple>? _pendingConnections;
    private int _publishScheduled;

    public void Process(Mat frame)
    {
        // DebugWindow.Show("test frame", frame.Clone());
        using var processingFrame = frame.Clone();
        // Transparent with debug info on top. This is overlayed over the actual frame at the end
        using var debugFrame = new Mat(new Size(CaptureService.width, CaptureService.height), MatType.CV_8UC4,
            new Scalar(0, 0, 0, 0));
        var markers = GetMarkerSeeds(processingFrame, debugFrame);

        try
        {
            using var combinedMask = Helpers.CombineMasks(markers.Select(m => m.Mask).ToList());
            using var combinedOverlay = Helpers.MaskToTransparentOverlay(combinedMask);

            Cv2.Circle(debugFrame, new Point(200, 200), 20, new Scalar(0, 200, 0, 255), -1);
            // Blend.BlendOverlay(combinedOverlay, debugFrame);

            var dirMarkers = IdentifyDirectionMarkers(frame, debugFrame, combinedMask);
            var units = GetRects(frame, debugFrame, markers, dirMarkers);

            var train = units.FirstOrDefault(u => u.Marker.Unit?.Type == UnitType.Locomotive);

            if (train != null)
            {
                var limits = limiterService.ProcessLimits(frame, train.Front, train.Back);
                dccService.SetLimits(limits.Forward, limits.Reverse);
            }
            else
            {
                // _dccService.SetLimits(SpeedLimit.Stop, SpeedLimit.Stop);
                dccService.SetLimits(SpeedLimit.NORMAL, SpeedLimit.NORMAL);
            }

            var throttleLimits = dccService.GetThrottleLimits();
            var railUnits = units.Select(u => new RailUnitGet(u)).ToList();

            // var railUnits = new List<RailUnitGet>();
            // railUnits = RailUnitMocks.GetMocks(config.Units.First(u => u.Type == UnitType.Locomotive),
            //     config.Units.First(u => u.Type == UnitType.Wagon));

            SchedulePublish(
                new LiveData
                {
                    Units = railUnits,
                    Forward = dccService.ForwardLimit,
                    ForwardValue = throttleLimits.Forward,
                    Reverse = dccService.ReverseLimit,
                    ReverseValue = throttleLimits.Reverse,
                },
                GetConnections(railUnits));

            lock (captureService.debugOverlayLock)
            {
                // DebugWindow.Show("debug overlay", captureService.debugOverlayFrame.Clone());
                debugFrame.CopyTo(captureService.debugOverlayFrame);
            }
        }
        finally
        {
            foreach (var marker in markers)
                marker.Mask.Dispose();
        }
    }

    private void SchedulePublish(LiveData liveData, List<Uncouple> connections)
    {
        _pendingLiveData = liveData;
        _pendingConnections = connections;
        if (Interlocked.CompareExchange(ref _publishScheduled, 1, 0) != 0)
            return;

        _ = PublishLoopAsync();
    }

    private async Task PublishLoopAsync()
    {
        try
        {
            while (true)
            {
                var liveData = _pendingLiveData;
                var connections = _pendingConnections;
                if (liveData == null || connections == null)
                    break;

                _pendingLiveData = null;
                _pendingConnections = null;

                await unitHub.Clients.All.SendAsync("units", liveData);
                await unitHub.Clients.All.SendAsync("connections", connections);

                if (_pendingLiveData == null)
                    break;
            }
        }
        finally
        {
            Interlocked.Exchange(ref _publishScheduled, 0);
            if (_pendingLiveData != null)
                SchedulePublish(_pendingLiveData, _pendingConnections!);
        }
    }

    public Mat TrainFromDirectory(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
            throw new Exception($"Couldn't find directory path {directoryPath}");

        var files = Directory.GetFiles(directoryPath)
            .Where(f => f.ToLowerInvariant().EndsWith(".png") || f.ToLowerInvariant().EndsWith(".jpg"))
            .OrderBy(f => f)
            .ToList();

        Console.WriteLine($"Found {files.Count} images. Training background...");

        var fgMask = new Mat();
        Mat? first = null;

        for (int i = 0; i < files.Count; i++)
        {
            var frame = Cv2.ImRead(files[i]);
            if (i == 0) first = frame;

            if (!frame.Empty())
            {
                Cv2.GaussianBlur(frame, frame, Blur, 0);
                // Cv2.Add(frame, new Scalar(-50, -50, -50), frame);

                _mog2.Apply(frame, fgMask, 0.01);

                if (!ReferenceEquals(frame, first)) frame.Release();
            }
        }

        fgMask.Release();
        Console.WriteLine("Background training complete!");

        return first ?? throw new Exception("No frames were trained.");
    }

    public List<MarkerDef> GetMarkerSeeds(Mat frame, Mat debugFrame)
    {
        // // Debug the go zone
        // double goZoneAlpha = 0.2;
        // using var goZoneColor = new Mat();
        // Cv2.CvtColor(goZone, goZoneColor, ColorConversionCodes.GRAY2BGRA);
        // Cv2.AddWeighted(goZoneColor, goZoneAlpha, debugFrame, 1 - goZoneAlpha, 1, debugFrame);

        Cv2.GaussianBlur(frame, frame, Blur, 0);

        using var res = GetDiffMask(frame);


        Cv2.Threshold(res, res, 254.0, 255.0, ThresholdTypes.Binary);

        // Erosion then dilation, renmove noise
        using var kernelOpen = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3));
        Cv2.MorphologyEx(res, res, MorphTypes.Open, kernelOpen);

        // Dilation then eriosion, fill gaps and join blobs
        using var kernelClose = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(53, 53));
        Cv2.MorphologyEx(res, res, MorphTypes.Close, kernelClose);

        // Now that the important blobs are joined we can safely remoive bigger noise thats still seperate
        using var kernelOpen2 = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(15, 15));
        Cv2.MorphologyEx(res, res, MorphTypes.Open, kernelOpen2);

        using var cutout = new Mat();
        using var blurredFrame = new Mat();
        // A bit of blur so there is more of an average color to find
        Cv2.GaussianBlur(frame, blurredFrame, new Size(21, 21), 0);
        blurredFrame.CopyTo(cutout, res);

        var colorMasks =
            SplitMaskByNearestColorRegion(blurredFrame, res, LookupColor.Colors.Select(c => c.SingleColor).ToList());

        var markerDefs = new List<MarkerDef>();
        var keptMasks = new HashSet<Mat>();

        try
        {
            for (int index = 0; index < colorMasks.Count; index++)
            {
                var mask = colorMasks[index];

                var center = GetCenterOfShape(mask);
                var color = LookupColor.Colors[index];

                // No shape for this color this frame; skip before allocating filteredMask.
                // The original colorMasks[index] is disposed in the finally block below.
                if (center == null)
                    continue;

                Cv2.FindContours(mask, out Point[][] contours, out HierarchyIndex[] hierarchy,
                    RetrievalModes.External, ContourApproximationModes.ApproxSimple);

// Start with a blank mask, same size/type as original
                Mat filteredMask = Mat.Zeros(mask.Size(), mask.Type());
                Point[] contourMatch = [];

                foreach (var contour in contours)
                {
                    // TODO gross but works for now to filter out extra detected stuff. In future when background is not yellow should be easier to only detect one color
                    double area = Cv2.ContourArea(contour);
                    if (area <= 3000)
                        continue;

                    contourMatch = contour;

                    // Draw this contour onto the filtered mask (filled white)
                    Cv2.FillPoly(filteredMask, new[] { contour }, Scalar.All(255));

                    // // Overlay drawing (unchanged from before)
                    // contourOverlay.SetTo(Scalar.All(0));
                    // Cv2.FillPoly(contourOverlay, [contour], Scalar.Red);
                    // double alpha = 0.5;
                    // Cv2.AddWeighted(contourOverlay, alpha, debugFrame, 1 - alpha, 1, debugFrame);
                }

// Replace the original mask with the filtered one
                mask = filteredMask;

                // DebugWindow.Show("mask " + index, mask);

                keptMasks.Add(mask);
                markerDefs.Add(new MarkerDef(
                    -1,
                    color,
                    index == 0
                        ? config.Units.ElementAtOrDefault(0)
                        : config.Units.ElementAtOrDefault(1),
                    center.Value.ToPoint(),
                    mask,
                    contourMatch
                ));
            }
        }
        finally
        {
            foreach (var mask in colorMasks)
            {
                if (!keptMasks.Contains(mask))
                    mask.Dispose();
            }
        }

        return markerDefs;
    }

    private Mat GetDiffMask(Mat liveFrame)
    {
        using var fgMask = new Mat();

        const double liveLearningRate = 0.0;
        _mog2.Apply(liveFrame, fgMask, liveLearningRate);

        var cut = new Mat();
        fgMask.CopyTo(cut, goZone);

        return cut;
    }

    public List<Point> IdentifyDirectionMarkers(Mat frame, Mat debugFrame, Mat mask)
    {
        using var hsv = new Mat();
        Cv2.CvtColor(frame, hsv, ColorConversionCodes.BGR2HSV);

        using var debug = new Mat();
        Cv2.InRange(hsv, new Scalar(0, 0, 120), new Scalar(180, 35, 255), debug);

        using var cutout = new Mat();
        debug.CopyTo(cutout, mask);

        Cv2.FindContours(cutout, out Point[][] contours, out HierarchyIndex[] hierarchy,
            RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        var markers = new List<Point>();

        foreach (var contour in contours)
        {
            var area = Cv2.ContourArea(contour);
            // Cv2.DrawContours(debugFrame, new[] { contour }, -1, Scalar.Orange, -1);

            var contour2f = contour.Select(p => new Point2f(p.X, p.Y)).ToArray();
            var rect = Cv2.MinAreaRect(contour2f);
            var center = rect.Center;

            if (area > 13 && area < 30)
            {
                // Cv2.Circle(debugFrame, center.ToPoint(), 3, Scalar.Green, -1);
            }
            else
            {
                // Cv2.Circle(debugFrame, center.ToPoint(), 3, Scalar.Red, -1);
            }

            // Cv2.PutText(debugFrame, area.ToString("F0"), center.ToPoint(), HersheyFonts.HersheySimplex, 1, Scalar.Orange, 2);
        }


        return markers;
    }

    public List<UnitMarkerResponse> GetRects(Mat frame, Mat debugFrame, List<MarkerDef> markers, List<Point> dirMarkers)
    {
        var res = new List<UnitMarkerResponse>();

        foreach (var marker in markers)
        {
            var contour2f = marker.Contour.Select(p => new Point(p.X, p.Y)).ToArray();
            var test = Cv2.ContourArea(marker.Contour);
            var rotatedRect = Cv2.MinAreaRect(contour2f);
            var boxPoints = rotatedRect.Points(); // Point2f[4]

            var box = boxPoints.Select(p => new Vector2Int((int)p.X, (int)p.Y)).ToArray();

            for (int i = 0; i < 4; i++)
            {
                // Cv2.Line(debugFrame,
                //     new Point((int)boxPoints[i].X, (int)boxPoints[i].Y),
                //     new Point((int)boxPoints[(i + 1) % 4].X, (int)boxPoints[(i + 1) % 4].Y),
                //     Scalar.Red, 5);
            }

            Point? frontDirMarker = dirMarkers.FirstOrDefault(p =>
                RectContainsPoint(rotatedRect, p));
            if (frontDirMarker == default && !dirMarkers.Any(p => RectContainsPoint(rotatedRect, p)))
                frontDirMarker = null;

            if (frontDirMarker != null)
            {
                (double dist, Transform front, Transform back) best = default;
                double bestDist = double.MaxValue;

                for (int j = 0; j < 4; j++)
                {
                    var a = box[j];
                    var b = box[(j + 1) % 4];

                    var backA = box[(j + 2) % 4];
                    var backB = box[(j + 3) % 4];

                    var midFront = new Vector2Int((a.X + b.X) / 2, (a.Y + b.Y) / 2);
                    var midBack = new Vector2Int((backA.X + backB.X) / 2, (backA.Y + backB.Y) / 2);

                    var normal = new Vector2Int(b.X - a.X, b.Y - a.Y).Normalized().Rotate90CW();

                    var front = new Transform(midFront, normal);
                    var back = new Transform(midBack, -normal);

                    double dist = a.DistanceTo(frontDirMarker.Value) + b.DistanceTo(frontDirMarker.Value);

                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = (dist, front, back);
                    }
                }

                // Cv2.Circle(frame, new Point(best.front.Position.X, best.front.Position.Y), 20, Colors.GREEN);

                res.Add(new UnitMarkerResponse(box.ToList(), best.front, best.back, marker));
            }
        }


        return res;
    }

    private List<Mat> SplitMaskByNearestColorRegion(Mat frame, Mat mask, List<Scalar> targetColors, int tolerance = 20)
    {
        int n = targetColors.Count;
        var colorMasks = new Mat[n];
        var distMaps = new Mat[n];

        // Step 1: per-color range masks, restricted to the original mask
        for (int i = 0; i < n; i++)
        {
            var color = targetColors[i];
            var lower = new Scalar(
                Math.Max(0, color.Val0 - tolerance),
                Math.Max(0, color.Val1 - tolerance),
                Math.Max(0, color.Val2 - tolerance));
            var upper = new Scalar(
                Math.Min(255, color.Val0 + tolerance),
                Math.Min(255, color.Val1 + tolerance),
                Math.Min(255, color.Val2 + tolerance));

            using var rangeMask = new Mat();
            Cv2.InRange(frame, lower, upper, rangeMask);

            colorMasks[i] = new Mat();
            Cv2.BitwiseAnd(rangeMask, mask, colorMasks[i]);
        }

        // Step 2: distance transform per color
        for (int i = 0; i < n; i++)
        {
            using var inv = new Mat();
            Cv2.BitwiseNot(colorMasks[i], inv);

            distMaps[i] = new Mat();
            Cv2.DistanceTransform(inv, distMaps[i], DistanceTypes.L2, DistanceTransformMasks.Mask5);
        }

        // Step 3: assign every pixel in the original mask to its nearest color region
        var results = Enumerable.Range(0, n)
            .Select(_ => Mat.Zeros(mask.Size(), MatType.CV_8UC1).ToMat())
            .ToList();

        int rows = mask.Rows;
        for (int y = 0; y < rows; y++)
        {
            int cols = mask.Cols;
            for (int x = 0; x < cols; x++)
            {
                if (mask.At<byte>(y, x) == 0) continue;

                int bestIndex = -1;
                float bestDist = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    float d = distMaps[i].At<float>(y, x);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        bestIndex = i;
                    }
                }

                if (bestIndex >= 0)
                    results[bestIndex].Set(y, x, (byte)255);
            }
        }

        foreach (var cm in colorMasks)
            cm?.Dispose();
        foreach (var dm in distMaps)
            dm?.Dispose();

        return results;
    }

    private Point2f? GetCenterOfShape(Mat mask)
    {
        var moments = Cv2.Moments(mask, true);
        if (moments.M00 == 0) return null;

        return new Point2f((float)(moments.M10 / moments.M00), (float)(moments.M01 / moments.M00));
    }

    private static bool RectContainsPoint(RotatedRect rect, Point2f p)
    {
        return Cv2.PointPolygonTest(rect.Points(), p, false) >= 0;
    }

    private Vector2Int GetMidpoint(Vector2Int p1, Vector2Int p2)
    {
        return new Vector2Int(
            (p1.X + p2.X) / 2,
            (p1.Y + p2.Y) / 2
        );
    }

    // Find units that's front / back is close to each other (assume coupled) so they can be uncoupled
    public List<Uncouple> GetConnections(List<RailUnitGet> railUnits)
    {
        var connections = new List<Uncouple>();
        const int maxDist = 100;

        // Flatten every unit's two couplers into one list of (unit, index, position).
        var couplers = railUnits
            .SelectMany(u => new[]
            {
                new { Unit = u, Index = u.Def.FrontCouplerIndex, Position = u.Front.Position },
                new { Unit = u, Index = u.Def.BackCouplerIndex, Position = u.Back.Position }
            })
            .ToList();

        foreach (var coupler in couplers)
        {
            double bestDist = double.MaxValue;
            RailUnitGet? bestUnit = null;
            int bestIndex = -1;
            object? bestPos = null;

            foreach (var other in couplers)
            {
                if (other.Unit == coupler.Unit) continue; // skip same unit's own couplers

                double dist = other.Position.DistanceTo(coupler.Position);
                if (dist <= maxDist && dist < bestDist)
                {
                    bestDist = dist;
                    bestUnit = other.Unit;
                    bestIndex = other.Index;
                    bestPos = other.Position;
                }
            }

            if (bestUnit != null && !connections.Any(c =>
                    c.Address == coupler.Unit.Def.Address || c.Address == bestUnit.Def.Address))
            {
                connections.Add(new Uncouple
                {
                    Address = coupler.Unit.Def.Address,
                    Coupler = coupler.Index,
                    Position = GetMidpoint(coupler.Position, (dynamic)bestPos!)
                });
            }
        }

        return connections;
    }

    private Task? processLoop;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        DebugWindow.Start();

        _mog2 = BackgroundSubtractorMOG2.Create(history: 500, varThreshold: 150.0, detectShadows: true);

        var outputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DriveATrain",
            "Training Images");
        TrainFromDirectory(outputDir);

        processLoop = Task.Run(() =>
        {
            using var frame = new Mat();

            while (!cancellationToken.IsCancellationRequested && !token.IsCancellationRequested)
            {
                if (captureService.TryGetLatestFrame(frame))
                {
                    // This just processes at whatever  speed it can manage. TODO get fps im interested
                    Process(frame);
                }
            }
        }, cancellationToken);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await token.CancelAsync();
        if (processLoop != null)
        {
            // wait for the loop to actually exit, but don't hang forever on shutdown
            await Task.WhenAny(processLoop, Task.Delay(Timeout.Infinite, cancellationToken));
        }
    }

    public void Dispose()
    {
        token.Dispose();
        _mog2.Dispose();
    }
}

public class Uncouple
{
    public int Address { get; set; }

    public int Coupler { get; set; }

    public Vector2Int Position { get; set; }
}