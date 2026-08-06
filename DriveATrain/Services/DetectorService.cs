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
    UnitService unitService,
    IHubContext<UnitHub> unitHub,
    PathProjector pathProjector,
    Config config) : IHostedService, IDisposable
{
    private BackgroundSubtractorMOG2 _mog2;

    private static Size Blur;

    private CancellationTokenSource token = new CancellationTokenSource();


    private LiveData? _pendingLiveData;
    private List<Uncouple>? _pendingConnections;
    private int _publishScheduled;

    public void Process(Mat frame)
    {
        var processStopwatch = Stopwatch.StartNew();
        using var processingFrame = new Mat();
        Cv2.Resize(frame, processingFrame,
            new Size(CaptureService.DETECTION_WIDTH, CaptureService.DETECTION_HEIGHT));

        // Transparent with debug info on top. This is overlayed over the actual frame at the end
        using var debugFrame = new Mat(new Size(CaptureService.DETECTION_WIDTH, CaptureService.DETECTION_HEIGHT),
            MatType.CV_8UC4,
            new Scalar(0, 0, 0, 0));
        List<MarkerDef>? markers = null;

        Mat combinedMaskBinary = null;
        using Mat combinedMaskBinaryFullRes = new Mat();
        try
        {
            markers = GetMarkerSeeds(processingFrame, debugFrame);

            using var combinedMaskColor = Helpers.CombineMasksColor(markers.Select(m => (m.Mask, m.Color)).ToList());

            combinedMaskBinary = Helpers.CombineMasks(markers.Select(m => m.Mask).ToList());

            // TODO gross, but dir marker dection needs a full size mask
            Cv2.Resize(combinedMaskBinary, combinedMaskBinaryFullRes,
                new Size(CaptureService.CAMERA_WIDTH, CaptureService.CAMERA_HEIGHT));

            // using var blocksOverlay = MeasureStage("overlay.blocks-overlay",
            //     () => Helpers.InverseMaskOverlay(config.Vision.blocks));
            using var goZoneOverlay = Helpers.InverseMaskOverlay(config.Vision.goZone);

            Cv2.Circle(debugFrame, new Point(500, 200), 20, new Scalar(0, 0, 255, 255), -1);
            Blend.BlendOverlay(combinedMaskColor, debugFrame, 1);

            if (layoutOverlayPrepared != null)
                Blend.BlendPrepared(layoutOverlayPrepared, debugFrame);
            if (_goZoneOverlayPrepared != null)
                Blend.BlendPrepared(_goZoneOverlayPrepared, debugFrame);

            // TODO expensive, probably because its full res, but needs to be since the markers show up quite small
            var dirMarkers = IdentifyDirectionMarkers(frame, debugFrame, combinedMaskBinaryFullRes);
            // var dirMarkers = new List<Point>();
            var units = CalculateLayoutPosition(processingFrame, debugFrame, markers, dirMarkers);

            LayoutDraw.DrawUnits(debugFrame, units);

            var train = units.FirstOrDefault(u => u.Marker.Unit?.Type == UnitType.Locomotive);

            if (train != null)
            {
                var limits = limiterService.ProcessLimits(processingFrame, train.Front, train.Back, debugFrame);
                dccService.SetLimits(limits.Forward, limits.Reverse);
            }
            else
            {
                dccService.SetLimits(SpeedLimit.NORMAL, SpeedLimit.NORMAL);
            }

            // var throttleLimits = dccService.GetThrottleLimits(config.Dcc);
            var railUnits = units.Select(u => new RailUnitGet(u)).ToList();

            // var railUnits = new List<RailUnitGet>();
            // railUnits = RailUnitMocks.GetMocks(config.Units.First(u => u.Type == UnitType.Locomotive),
            //     config.Units.First(u => u.Type == UnitType.Wagon));

            unitService.SetLiveData(
                new LiveData
                {
                    Units = railUnits,
                    Forward = dccService.ForwardLimit,
                    // ForwardValue = throttleLimits.Forward,
                    Reverse = dccService.ReverseLimit,
                    PowerOn = dccService.PowerIsOn
                    // ReverseValue = throttleLimits.Reverse,
                });
            // GetConnections(railUnits));

            lock (captureService.debugOverlayLock)
            {
                debugFrame.CopyTo(captureService.debugOverlayFrame);
            }
        }
        finally
        {
            if (markers != null)
            {
                foreach (var marker in markers)
                    marker.Mask.Dispose();
            }

            combinedMaskBinary?.Dispose();
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
                Cv2.Resize(frame, frame, new Size(CaptureService.DETECTION_WIDTH, CaptureService.DETECTION_HEIGHT));

                Cv2.GaussianBlur(frame, frame, Blur, 0);
                // Cv2.Add(frame, new Scalar(-50, -50, -50), frame);


                // if (i == 20)
                //     frame.SaveImage(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                //         "DriveATrain",
                //         "TRAINIG FRAME.png"));

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

        // using var color = new Mat();
        // Cv2.CvtColor(res, color, ColorConversionCodes.BGR2BGRA);
        // Blend.BlendOverlay(color, debugFrame, 0.75);

        Cv2.Threshold(res, res, 254.0, 255.0, ThresholdTypes.Binary);


        // Erosion then dilation, renmove noise
        int openSize = 3; //(int)ResolutionScaler.ScaleKernel(3);
        using var kernelOpen = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(openSize, openSize));
        Cv2.MorphologyEx(res, res, MorphTypes.Open, kernelOpen);

        // Dilation then eriosion, fill gaps and join blobs
        int closeSize = ResolutionScaler.ScaleKernel(30);
        using var kernelClose = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(closeSize, closeSize));
        Cv2.MorphologyEx(res, res, MorphTypes.Close, kernelClose);

        // Now that the important blobs are joined we can safely remoive bigger noise thats still seperate
        int open2Size = (int)ResolutionScaler.ScaleKernel(15);
        using var kernelOpen2 = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(open2Size, open2Size));
        Cv2.MorphologyEx(res, res, MorphTypes.Open, kernelOpen2);
        using var cutout = new Mat();
        using var blurredFrame = new Mat();
        // A bit of blur so there is more of an average color to find
        int blurSize = (int)ResolutionScaler.ScaleKernel(21);
        Cv2.GaussianBlur(frame, blurredFrame, new Size(blurSize, blurSize), 0);
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
                    if (area <= ResolutionScaler.ScaleArea(3000))
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
        // liveFrame.SaveImage(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        //     "DriveATrain",
        //     "LIVE FRAME.png"));
        using var fgMask = new Mat();

        const double liveLearningRate = 0.0;
        _mog2.Apply(liveFrame, fgMask, liveLearningRate);

        var cut = new Mat();
        fgMask.CopyTo(cut, config.Vision.goZone);


        return cut;
    }

    // NOTE, this frame is the full size since the white dots are quite small
    public List<Point> IdentifyDirectionMarkers(Mat frame, Mat debugFrame, Mat mask)
    {
        using var hsv = new Mat();
        Cv2.CvtColor(frame, hsv, ColorConversionCodes.BGR2HSV);

        using var debug = new Mat();
        Cv2.InRange(hsv, new Scalar(105, 150, 80), new Scalar(125, 255, 255), debug);

        using var frameCut = new Mat();
        frame.CopyTo(frameCut, mask);

        using var cutout = new Mat();
        debug.CopyTo(cutout, mask);

        Point[][] contours = [];
        HierarchyIndex[] hierarchy = [];
        Cv2.FindContours(cutout, out contours, out hierarchy, RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        var markers = new List<Point>();

        var points = contours.Select(c => Helpers.ScalePoint(Cv2.MinAreaRect(c).Center.ToPoint())).ToList();
        foreach (var point in points)
        {
            Cv2.Circle(debugFrame, point, 3, new Scalar(0, 255, 0, 255), -1);
        }

        markers.AddRange(points);

        // Not even true at all
        // // This threshold includes extra noise on the unit boundary, but the markers that matter are closer to the center, and there is no noise around them
        // // Take the closest one which will ignore the extra outside noise
        // foreach (var unitLocation in unitLocations)
        // {
        //     var closetPoint = points.OrderBy(p => p.DistanceTo(unitLocation.Center)).FirstOrDefault();
        //
        //     if (closetPoint != null)
        //     {
        //         Cv2.Circle(debugFrame, closetPoint, 3, new Scalar(0, 255, 0, 255), -1);
        //         markers.Add(closetPoint);
        //     }
        // }

        // foreach (var contour in contours)
        // {
        //     var area = Cv2.ContourArea(contour);
        //
        //     var contour2f = contour.Select(p => new Point2f(p.X, p.Y)).ToArray();
        //     var rect = Cv2.MinAreaRect(contour2f);
        //     var center = rect.Center;
        //
        //     // Extra removed with morph
        //     // if (area > 5)
        //     // {
        //     var scaled = Helpers.ScalePoint(center.ToPoint());
        //
        //     Cv2.Circle(debugFrame, scaled, 3, new Scalar(0, 255, 0, 255), -1);
        //
        //     markers.Add(scaled);
        //     // }
        // }


        return markers;
    }

    // Take the camera position and map that onto the layout path
    public List<UnitMarkerResponse> CalculateLayoutPosition(Mat frame, Mat debugFrame, List<MarkerDef> markers,
        List<Point> dirMarkers)
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

                // res.Add(new UnitMarkerResponse(best.front, best.back, marker));
                var frontProjection = pathProjector.Project(best.front.Position.ToLayoutPoint());
                var backProjection = pathProjector.Project(best.back.Position.ToLayoutPoint());
                res.Add(new UnitMarkerResponse(frontProjection.Point.ToVector2Int(),
                    backProjection.Point.ToVector2Int(), marker));
            }
        }


        return res;
    }

    private List<Mat> SplitMaskByNearestColorRegion(Mat frame, Mat mask, List<Scalar> targetColors, int tolerance = 20)
    {
        int n = targetColors.Count;
        var colorMasks = new Mat[n];
        var distMaps = new Mat[n];

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
        using var inv = new Mat(); // reused across iterations, not reallocated per-i
        for (int i = 0; i < n; i++)
        {
            Cv2.BitwiseNot(colorMasks[i], inv);
            distMaps[i] = new Mat();
            Cv2.DistanceTransform(inv, distMaps[i], DistanceTypes.L2, DistanceTransformMasks.Mask3);
        }

        // Step 3: assign every pixel in the original mask to its nearest color region
        var results = Enumerable.Range(0, n)
            .Select(_ => Mat.Zeros(mask.Size(), MatType.CV_8UC1).ToMat())
            .ToList();

        // Running best-distance and best-index maps, same size as mask
        using var bestDist = new Mat(mask.Size(), MatType.CV_32FC1, new Scalar(float.MaxValue));
        using var bestIdx = new Mat(mask.Size(), MatType.CV_32SC1, new Scalar(-1));

        for (int i = 0; i < n; i++)
        {
            // where this map beats the current best
            using var better = new Mat();
            Cv2.Compare(distMaps[i], bestDist, better, CmpTypes.LT);

            distMaps[i].CopyTo(bestDist, better);

            using var idxMat = new Mat(mask.Size(), MatType.CV_32SC1, new Scalar(i));
            idxMat.CopyTo(bestIdx, better);
        }

        for (int i = 0; i < n; i++)
        {
            using var isIndex = new Mat();
            Cv2.Compare(bestIdx, new Scalar(i), isIndex, CmpTypes.EQ);
            Cv2.BitwiseAnd(isIndex, mask, results[i]);
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
        // const int maxDist = 100;
        //
        // // Flatten every unit's two couplers into one list of (unit, index, position).
        // var couplers = railUnits
        //     .SelectMany(u => new[]
        //     {
        //         new { Unit = u, Index = u.Def.FrontCouplerIndex, Position = u.Front.Position },
        //         new { Unit = u, Index = u.Def.BackCouplerIndex, Position = u.Back.Position }
        //     })
        //     .ToList();
        //
        // foreach (var coupler in couplers)
        // {
        //     double bestDist = double.MaxValue;
        //     RailUnitGet? bestUnit = null;
        //     int bestIndex = -1;
        //     object? bestPos = null;
        //
        //     foreach (var other in couplers)
        //     {
        //         if (other.Unit == coupler.Unit) continue; // skip same unit's own couplers
        //
        //         double dist = other.Position.DistanceTo(coupler.Position);
        //         if (dist <= maxDist && dist < bestDist)
        //         {
        //             bestDist = dist;
        //             bestUnit = other.Unit;
        //             bestIndex = other.Index;
        //             bestPos = other.Position;
        //         }
        //     }
        //
        //     if (bestUnit != null && !connections.Any(c =>
        //             c.Address == coupler.Unit.Def.Address || c.Address == bestUnit.Def.Address))
        //     {
        //         connections.Add(new Uncouple
        //         {
        //             Address = coupler.Unit.Def.Address,
        //             Coupler = coupler.Index,
        //             Position = GetMidpoint(coupler.Position, (dynamic)bestPos!)
        //         });
        //     }
        // }

        return connections;
    }

    private Task? processLoop;
    private Blend.PreparedOverlay? layoutOverlayPrepared;
    private Blend.PreparedOverlay? _goZoneOverlayPrepared;


    public Task StartAsync(CancellationToken cancellationToken)
    {
        int size = ResolutionScaler.ScaleKernel(9);
        Blur = new Size(size, size);
// once at startup
        using var layoutOverlay = LayoutDraw.DrawLayout(config);
        layoutOverlayPrepared = Blend.Prepare(layoutOverlay, 1);

        using var goZoneOverlaySrc = Helpers.InverseMaskOverlay(config.Vision.goZone);
        _goZoneOverlayPrepared = Blend.Prepare(goZoneOverlaySrc, 0.4);

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