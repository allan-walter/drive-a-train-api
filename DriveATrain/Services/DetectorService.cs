using System.Diagnostics;
using DriveATrain.Hubs;
using DriveATrain.OpenCv;
using DriveATrain.Services.Layout;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using OpenCvSharp;

namespace DriveATrain.Services;

public class DetectorService(
    LimiterService limiterService,
    CaptureService captureService,
    DccService dccService,
    UnitService unitService,
    LayoutService layoutService,
    LayoutDrawingService layoutDrawingService,
    IHubContext<UnitHub> unitHub,
    Config config) : IHostedService, IDisposable
{
    private BackgroundSubtractorMOG2 _mog2;

    private static Size Blur;

    private CancellationTokenSource token = new CancellationTokenSource();


    private LiveData? _pendingLiveData;
    private List<Uncouple>? _pendingConnections;
    private int _publishScheduled;

    public async Task Process(Mat fullResFrame)
    {
        using var processingFrame = new Mat();
        Cv2.Resize(fullResFrame, processingFrame,
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
            markers = await GetMarkerSeeds(processingFrame, debugFrame);

            // TODO, for now its easier to debug just the loco
            // markers = markers.Where(m => m.Color.SingleColor == LookupColor.Colors[0].SingleColor).ToList();

            using var combinedMaskColor =
                OpenCvHelpers.CombineMasksColor(markers.Select(m => (m.Mask, m.Color)).ToList());

            combinedMaskBinary = OpenCvHelpers.CombineMasks(markers.Select(m => m.Mask).ToList());


            // TODO gross, but dir marker dection needs a full size mask
            Cv2.Resize(combinedMaskBinary, combinedMaskBinaryFullRes,
                new Size(CaptureService.CAMERA_WIDTH, CaptureService.CAMERA_HEIGHT));


            // using var blocksOverlay = MeasureStage("overlay.blocks-overlay",
            //     () => Helpers.InverseMaskOverlay(config.Vision.blocks));
            using var goZoneOverlay = OpenCvHelpers.InverseMaskOverlay(config.Vision.goZone);

            Blend.BlendOverlay(combinedMaskColor, debugFrame, 1);


            if (_goZoneOverlayPrepared != null)
                Blend.BlendPrepared(_goZoneOverlayPrepared, debugFrame);

            // TODO expensive, probably because its full res, but needs to be since the markers show up quite small
            var dirMarkers = IdentifyDirectionMarkers(fullResFrame, debugFrame, combinedMaskBinaryFullRes);
            // var dirMarkers = new List<Point>();
            var units = CalculateLayoutPosition(processingFrame, debugFrame, markers, dirMarkers);
            var center = units.FirstOrDefault(u => u.Marker.Unit?.Type == UnitType.Locomotive)?.Center;

            // TODO for debugging
            // center = unitService.DebugPointer.Position;

            layoutDrawingService.DrawLayout(center, debugFrame);
            layoutDrawingService.DrawUnits(debugFrame, units);

            var train = units.FirstOrDefault(u => u.Marker.Unit?.Type == UnitType.Locomotive);

            SpeedResult limits = new SpeedResult() { Forward = SpeedLimit.STOP, Reverse = SpeedLimit.STOP };
            if (train != null)
            {
                limits = limiterService.ProcessLimits(processingFrame, train.Front, train.Back, debugFrame);
            }

            dccService.SetLimits(limits.Forward, limits.Reverse);
            var throttleLimits = dccService.GetThrottleLimits();

            var railUnits = units.Select(u => new RailUnitGet(u)).ToList();

            // var railUnits = new List<RailUnitGet>();
            // railUnits = RailUnitMocks.GetMocks(config.Units.First(u => u.Type == UnitType.Locomotive),
            //     config.Units.First(u => u.Type == UnitType.Wagon));

            var connections = GetConnections(railUnits);

            unitService.SetLiveData(
                new LiveData
                {
                    Units = railUnits,
                    Forward = dccService.ForwardLimit,
                    ForwardLimitValue = throttleLimits.Forward,
                    Reverse = dccService.ReverseLimit,
                    PowerOn = dccService.PowerIsOn,
                    Connections = connections,
                    ReverseLimitValue = throttleLimits.Reverse,
                });

            ExtraDebugInfo(debugFrame);

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

    void ExtraDebugInfo(Mat debugFrame)
    {
        Cv2.Circle(debugFrame, unitService.DebugPointer.Position.ToPoint(), 3, Colors.Magenta, -1);
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

    // Doesn't throw any exceptions, may return empty list
    private async Task<List<MarkerDef>> GetMarkerSeeds(Mat frame, Mat debugFrame)
    {
        // // Debug the go zone
        // double goZoneAlpha = 0.2;
        // using var goZoneColor = new Mat();
        // Cv2.CvtColor(goZone, goZoneColor, ColorConversionCodes.GRAY2BGRA);
        // Cv2.AddWeighted(goZoneColor, goZoneAlpha, debugFrame, 1 - goZoneAlpha, 1, debugFrame);

        Cv2.GaussianBlur(frame, frame, Blur, 0);


        using var res = GetDiffMask(frame);

        using var cutout = new Mat();
        using var blurredFrame = new Mat();
        // A bit of blur so there is more of an average color to find
        int blurSize = (int)ResolutionScaler.ScaleKernel(21);
        Cv2.GaussianBlur(frame, blurredFrame, new Size(blurSize, blurSize), 0);
        blurredFrame.CopyTo(cutout, res);

        var colorMasks = SplitMaskByNearestColorRegion(blurredFrame, res, UnitColor.Colors);

        var markerDefs = new List<MarkerDef>();
        var keptMasks = new HashSet<Mat>();

        try
        {
            for (int index = 0; index < colorMasks.Count; index++)
            {
                var mask = colorMasks[index].mat;

                var color = UnitColor.Colors[index];

                Cv2.FindContours(mask, out Point[][] contours, out HierarchyIndex[] hierarchy,
                    RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                // Start with a blank mask, same size/type as original
                Mat filteredMask = Mat.Zeros(mask.Size(), mask.Type());

                // Its likely (and probably inevitable) that there will be other stuff on the layout that is also detected because its the same color
                // So for each color (we're alraaddy in a loop doing that), find the contour closest the a point on the defined layout. There shouldnt be a collision on the track
                // TODO maybe a hard cutoff would be best so if the unit is truely gone we dont default to random objects
                // Do both, just in case
                // TODO re-comment
                // Check that the detected blob is on the track path, and also that the area kinda resembles a unit, in case we do detect small stuff on the tracks
                // Its liely there is someting of a valid area in the sceneery but the distance check should filter it out
                var contourInfo = contours.Select(c =>
                    {
                        var area = Cv2.ContourArea(c);
                        var rotatedRect = Cv2.MinAreaRect(c.Select(p => new Point(p.X, p.Y)).ToArray());

                        // Get the 4 corner points
                        Point2f[] pts = rotatedRect.Points();

                        // Draw as a closed polygon
                        // Yellow for maybe, if we decide to keep it it's just drawn over later in green
                        Cv2.Polylines(debugFrame, new Point[][] { pts.Select(p => p.ToPoint()).ToArray() },
                            isClosed: true,
                            color: Colors.Yellow, thickness: 2, lineType: LineTypes.AntiAlias);

                        // Draw this contour onto the filtered mask (filled white)
                        Cv2.FillPoly(filteredMask, new[] { c }, Colors.White);

                        var center = new Vector2Int(rotatedRect.Center.ToPoint().X, rotatedRect.Center.ToPoint().Y);
                        var pointOnLayout = layoutService.ProjectOnPath(center);

                        var dist = pointOnLayout.Point.DistanceTo(center);

                        return new { dist = dist, area = area, rect = rotatedRect, shape = c };
                    })
                    .OrderBy(a => a.dist)
                    .ToList();

                var validContours = contourInfo
                    .Where(d => d.dist < 15 && d.area > 250)
                    .ToList();

                // TODO DEbugging
                // if (colorMasks[index].color == LookupColor.UnitBlack && validContours.Count != 1)
                // {
                //     await dccService.SetThrottleAsync(new Throttle(0, false, false));
                //     // Debugger.Break();
                // }

                var unitContour = validContours.FirstOrDefault();


                // Replace the original mask with the filtered one
                mask = filteredMask;

                if (unitContour != null)
                {
                    // Draw green now we've found the main shape to keep
                    Cv2.Polylines(debugFrame,
                        new Point[][] { unitContour.rect.Points().Select(p => p.ToPoint()).ToArray() },
                        isClosed: true,
                        color: Colors.Green, thickness: 2, lineType: LineTypes.AntiAlias);

                    keptMasks.Add(mask);
                    markerDefs.Add(new MarkerDef(
                        -1,
                        color,
                        index == 0
                            ? config.Units.ElementAtOrDefault(0)
                            : config.Units.ElementAtOrDefault(1),
                        mask,
                        unitContour.shape
                    ));
                }
            }
        }
        finally
        {
            foreach (var mask in colorMasks)
            {
                if (!keptMasks.Contains(mask.mat))
                    mask.mat.Dispose();
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


        var res = new Mat();
        fgMask.CopyTo(res, config.Vision.goZone);


        // DebugWindow.Show("raw", res.Clone());

        // using var color = new Mat();
        // Cv2.CvtColor(res, color, ColorConversionCodes.BGR2BGRA);
        // Blend.BlendOverlay(color, debugFrame, 0.75);

        Cv2.Threshold(res, res, 254.0, 255.0, ThresholdTypes.Binary);

        using var frameCut = new Mat();
        liveFrame.CopyTo(frameCut, res);

        DebugWindow.Show("noiseRemoval", "step 0", res);
        // TODO, even with a perfect background as the train moves the cameras image changes slightly so there will always be small noise to remove
        // I've tried disabling auto exposer, focus etc with no luck
        // Erosion then dilation, renmove noise
        int openSize = 3; //(int)ResolutionScaler.ScaleKernel(3);
        using var kernelOpen = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(openSize, openSize));
        Cv2.MorphologyEx(res, res, MorphTypes.Open, kernelOpen);

        DebugWindow.Show("noiseRemoval", "step 1", res);


        // Dilation then eriosion, fill gaps and join blobs
        int closeSize = 15; //ResolutionScaler.ScaleKernel(30);
        using var kernelClose = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(closeSize, closeSize));
        Cv2.MorphologyEx(res, res, MorphTypes.Close, kernelClose);

        DebugWindow.Show("noiseRemoval", "step 2", res);

        // Now that the important blobs are joined we can safely remoive bigger noise thats still seperate
        int open2Size = 5; //(int)ResolutionScaler.ScaleKernel(15);
        using var kernelOpen2 = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(open2Size, open2Size));
        Cv2.MorphologyEx(res, res, MorphTypes.Open, kernelOpen2);

        DebugWindow.Show("noiseRemoval", "step 3", res);

        // Finally, join what remains back togfether, the last stop removes a lot, and sometimes seperates things
        int close2Size = 5; //(int)ResolutionScaler.ScaleKernel(15);
        using var kernalClose2 = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(close2Size, close2Size));
        Cv2.MorphologyEx(res, res, MorphTypes.Close, kernalClose2);

        DebugWindow.Show("noiseRemoval", "step 4", res);

        liveFrame.CopyTo(frameCut, res);

        return res;
    }

    // NOTE, this frame is the full size since the white dots are quite small
    public List<Point> IdentifyDirectionMarkers(Mat frame, Mat debugFrame, Mat mask)
    {
        // DebugWindow.Show("test", frame.Clone());
        using var hsv = new Mat();
        Cv2.CvtColor(frame, hsv, ColorConversionCodes.BGR2HSV);

        using var debug = new Mat();
        Cv2.InRange(hsv, UnitColor.DirMarkerColor.Lower, UnitColor.DirMarkerColor.Upper, debug);

        using var frameCut = new Mat();
        frame.CopyTo(frameCut, mask);

        using var cutout = new Mat();
        debug.CopyTo(cutout, mask);

        DebugWindow.Show("dirMarkers", "thresholded", cutout);
        Point[][] contours = [];
        HierarchyIndex[] hierarchy = [];
        Cv2.FindContours(cutout, out contours, out hierarchy, RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        var markers = new List<Point>();

        var points = contours.Select(c => OpenCvHelpers.ScalePoint(Cv2.MinAreaRect(c).Center.ToPoint())).ToList();
        foreach (var point in points)
        {
            Cv2.Circle(debugFrame, point, 3, Colors.Gold, -1);
        }

        markers.AddRange(points);

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
            var rotatedRect = Cv2.MinAreaRect(contour2f);
            var boxPoints = rotatedRect.Points(); // Point2f[4]

            var box = boxPoints.Select(p => new Vector2Int((int)p.X, (int)p.Y)).ToArray();

            // Could be the front or the back depending on the unit
            // TODO I dont think thjis would ever actually be null
            // Get the cliosertt onem it might be just outside if the mask detection is loose, closets should be fine
            Point2f center = rotatedRect.Center;

            // Point? point = dirMarkers
            //     .OrderBy(p => Math.Pow(p.X - center.X, 2) + Math.Pow(p.Y - center.Y, 2))
            //     .Select(p => (Point?)p)
            //     .FirstOrDefault();
            Point? point = dirMarkers
                // groness so the default is null not zero
                .Select(p => (Point?)p)
                .FirstOrDefault(p =>
                    RectContainsPoint(rotatedRect, p.Value));


// O

            if (point == null)
            {
                // TODO delete, limit handles this properl;y (hopefuly)
                // so I can debug break without the train driving away
                // dccService.SetThrottleAsync(new Throttle(0, false, false));
            }

            if (point != null)
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

                    if (marker.Unit != null && marker.Unit.DirMarkerOnBack.GetValueOrDefault())
                    {
                        (front, back) = (back, front);
                    }

                    double dist = a.DistanceTo(point.Value) + b.DistanceTo(point.Value);

                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = (dist, front, back);
                    }
                }

                // Cv2.Circle(frame, new Point(best.front.Position.X, best.front.Position.Y), 20, Colors.GREEN);

                var frontPos = layoutService.ProjectOnPath(best.front.Position, debugFrame).Point;
                var backPos = layoutService.ProjectOnPath(best.back.Position, debugFrame).Point;
                res.Add(new UnitMarkerResponse(frontPos, backPos, marker));
            }
        }


        return res;
    }

    // Static so I can use it easily in other project for debugging colors
    // Doesn't throw any exceptions, may return empty list
    public static List<(Mat mat, UnitColor color)> SplitMaskByNearestColorRegion(Mat frame, Mat mask,
        List<UnitColor> targetColors)
    {
        using var hsv = new Mat();
        Cv2.CvtColor(frame, hsv, ColorConversionCodes.BGR2HSV);

        int n = targetColors.Count;
        var colorMasks = new Mat[n];
        var distMaps = new Mat[n];

        // Step 1: for each target color, build a mask of pixels in "frame" that are
        // within +/- tolerance of that color, then restrict it to the input mask.
        for (int i = 0; i < n; i++)
        {
            var color = targetColors[i];

            // Pixels in frame that fall within this color's range
            using var rangeMask = new Mat();
            Cv2.InRange(hsv, color.Color.Lower, color.Color.Upper, rangeMask);

            // Keep only the ones that are also inside the original mask
            colorMasks[i] = new Mat();
            Cv2.BitwiseAnd(rangeMask, mask, colorMasks[i]);

            DebugWindow.Show("colorSplit", $"mask_{i} step 0", colorMasks[i]);
            // var a = new Mat();
            // frame.CopyTo(a, colorMasks[i]);
            // DebugWindow.Show("colorSplit", $"mask_{i} color", a.Clone());
        }

        // Step 2: for each color mask, compute a distance transform so that every
        // pixel knows how far it is from the nearest pixel belonging to that color region.
        using var inv = new Mat(); // reused scratch buffer, not reallocated per color
        for (int i = 0; i < n; i++)
        {
            // DistanceTransform measures distance to the nearest ZERO pixel,
            // so invert the mask first (color region becomes 0, everything else 255)
            Cv2.BitwiseNot(colorMasks[i], inv);

            distMaps[i] = new Mat();
            Cv2.DistanceTransform(inv, distMaps[i], DistanceTypes.L2, DistanceTransformMasks.Mask3);
        }

        // Step 3: assign every pixel in the original mask to whichever color region is closest

        // Output mask per color, all starting empty
        var results = Enumerable.Range(0, n)
            .Select(i => (Mat.Zeros(mask.Size(), MatType.CV_8UC1).ToMat(), targetColors[i]))
            .ToList();

        // Track the smallest distance seen so far per pixel, and which color index achieved it
        using var bestDist = new Mat(mask.Size(), MatType.CV_32FC1, new Scalar(float.MaxValue));
        using var bestIdx = new Mat(mask.Size(), MatType.CV_32SC1, new Scalar(-1));

        for (int i = 0; i < n; i++)
        {
            // Find pixels where this color's distance beats the current best
            using var better = new Mat();
            Cv2.Compare(distMaps[i], bestDist, better, CmpTypes.LT);

            // Update bestDist/bestIdx only at those pixels
            distMaps[i].CopyTo(bestDist, better);
            using var idxMat = new Mat(mask.Size(), MatType.CV_32SC1, new Scalar(i));
            idxMat.CopyTo(bestIdx, better);
        }

        // Step 4: build each color's final output mask from the winning indices,
        // clipped back to the original mask shape
        for (int i = 0; i < n; i++)
        {
            using var isIndex = new Mat();
            Cv2.Compare(bestIdx, new Scalar(i), isIndex, CmpTypes.EQ);
            Cv2.BitwiseAnd(isIndex, mask, results[i].Item1);

            // DebugWindow.Show("colorSplit", $"mask_{i} result", results[i].Clone());
            var a = new Mat();
            frame.CopyTo(a, results[i].Item1);
            // DebugWindow.Show("colorSplit", $"mask_{i} result color", a.Clone());
        }

        // Cleanup intermediate mats
        foreach (var cm in colorMasks)
            cm?.Dispose();
        foreach (var dm in distMaps)
            dm?.Dispose();

        return results;
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
        const int maxDist = 25;

        // Flatten every unit's two couplers into one list of (unit, index, position).
        var couplers = railUnits
            .SelectMany(u => new[]
            {
                new { Unit = u, Index = u.Def.FrontCouplerIndex, Position = u.Front },
                new { Unit = u, Index = u.Def.BackCouplerIndex, Position = u.Back }
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
    private Blend.PreparedOverlay? _goZoneOverlayPrepared;


    public Task StartAsync(CancellationToken cancellationToken)
    {
        int size = ResolutionScaler.ScaleKernel(9);
        Blur = new Size(size, size);

        using var goZoneOverlaySrc = OpenCvHelpers.InverseMaskOverlay(config.Vision.goZone);
        _goZoneOverlayPrepared = Blend.Prepare(goZoneOverlaySrc, 0.4);

        _mog2 = BackgroundSubtractorMOG2.Create(history: 500, varThreshold: 150.0, detectShadows: true);

        var outputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DriveATrain",
            "Training Images");
        TrainFromDirectory(outputDir);

        processLoop = Task.Run(async () =>
        {
            using var frame = new Mat();

            while (!cancellationToken.IsCancellationRequested && !token.IsCancellationRequested)
            {
                if (captureService.TryGetLatestFrame(frame))
                {
                    // await Task.Delay(100);
                    await Process(frame);
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