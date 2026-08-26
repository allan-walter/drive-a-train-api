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
    private Mat _trackMask;


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
            markers = FindUnitMarkers(processingFrame, debugFrame);

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

            // Drawn last so no other overlay can cover a detected marker; every dot here
            // is a candidate the orientation logic saw, hidden ones were misleading
            foreach (var dirMarker in dirMarkers)
                Cv2.Circle(debugFrame, dirMarker, 3, Colors.Gold, -1);

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

    // Doesn't throw any exceptions, may return empty list
    private List<MarkerDef> FindUnitMarkers(Mat frame, Mat debugFrame)
    {
        var hits = UnitFinder.Find(frame, _trackMask);

        var markerDefs = new List<MarkerDef>();

        for (int index = 0; index < UnitColor.Colors.Count; index++)
        {
            var color = UnitColor.Colors[index];
            var colourHits = hits.Where(h => h.Colour == color.Color.Name).ToList();

            // Yellow for maybe, the kept one is drawn over in green below
            foreach (var hit in colourHits)
                Cv2.Polylines(debugFrame, new[] { hit.Rect.Points().Select(p => p.ToPoint()).ToArray() },
                    isClosed: true, color: Colors.Yellow, thickness: 2, lineType: LineTypes.AntiAlias);

            // The corridor mask keeps the bench out, but if something colour and unit shaped is
            // on the track anyway, prefer the blob closest to the layout path
            var best = colourHits
                .OrderBy(h =>
                {
                    var center = new Vector2Int((int)h.Rect.Center.X, (int)h.Rect.Center.Y);
                    return layoutService.ProjectOnPath(center).Point.DistanceTo(center);
                })
                .FirstOrDefault();

            if (best == null)
                continue;

            Cv2.Polylines(debugFrame, new[] { best.Rect.Points().Select(p => p.ToPoint()).ToArray() },
                isClosed: true, color: Colors.Green, thickness: 2, lineType: LineTypes.AntiAlias);

            var mask = Mat.Zeros(frame.Size(), MatType.CV_8UC1).ToMat();
            // Hull, not raw contour: the direction LED cuts a concave notch out of the
            // colour blob, and the LED must land inside this mask to be found
            Cv2.FillPoly(mask, new[] { Cv2.ConvexHull(best.Contour) }, Colors.White);

            markerDefs.Add(new MarkerDef(
                -1,
                color,
                config.Units.ElementAtOrDefault(index),
                mask,
                best.Contour
            ));
        }

        return markerDefs;
    }

    // The LED reads blue-white and grey weights blue at only 11%, so threshold the max of
    // the three channels instead. 200 leaves headroom for compression noise between frames;
    // the area floor drops the 1-2px specular glints that sneak in at the lower cutoff
    // (the LED blob itself measures ~260)
    private const int DirMarkerBrightness = 200;
    private const int DirMarkerMinArea = 20;

    // NOTE, this frame is the full size since the white dots are quite small
    public List<Point> IdentifyDirectionMarkers(Mat frame, Mat debugFrame, Mat mask)
    {
        DebugWindow.Show("dirMarkers", "frame", frame);

        var channels = Cv2.Split(frame);
        using var maxChannel = new Mat();
        Cv2.Max(channels[0], channels[1], maxChannel);
        Cv2.Max(maxChannel, channels[2], maxChannel);
        foreach (var channel in channels)
            channel.Dispose();

        using var bright = new Mat();
        Cv2.Threshold(maxChannel, bright, DirMarkerBrightness, 255, ThresholdTypes.Binary);

        using var cutout = new Mat();
        bright.CopyTo(cutout, mask);

        DebugWindow.Show("dirMarkers", "thresholded", cutout);
        Point[][] contours = [];
        HierarchyIndex[] hierarchy = [];
        Cv2.FindContours(cutout, out contours, out hierarchy, RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        var markers = new List<Point>();

        // Largest first, so downstream first-inside-the-unit matching prefers the LED blob
        // over any glint that survived the area floor
        var points = contours
            .Where(c => Cv2.ContourArea(c) >= DirMarkerMinArea)
            .OrderByDescending(c => Cv2.ContourArea(c))
            .Select(c => OpenCvHelpers.ScalePoint(Cv2.MinAreaRect(c).Center.ToPoint()))
            .ToList();

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

            bool markerDetected = point != null;
            if (!markerDetected)
            {
                // The LED can drop out for a frame; the unit can't physically flip between
                // frames, so reuse the last seen marker position instead of guessing. No
                // history means the unit can't be oriented yet, skip it this frame.
                if (_lastDirMarker.TryGetValue(marker.Color.Color.Name, out var prev))
                    point = prev;
                else
                    continue;
            }

            if (point != null)
            {
                if (markerDetected)
                    _lastDirMarker[marker.Color.Color.Name] = point.Value;

                (double dist, Transform front, Transform back) best = default;
                double bestDist = double.MaxValue;

                double shortDim = Math.Min(rotatedRect.Size.Width, rotatedRect.Size.Height);
                double longDim = Math.Max(rotatedRect.Size.Width, rotatedRect.Size.Height);

                for (int j = 0; j < 4; j++)
                {
                    var a = box[j];
                    var b = box[(j + 1) % 4];

                    // Only the two short ends may compete: for a marker mid-length near one
                    // side, a long side edge's corner-distance sum can narrowly beat the
                    // correct end and point the unit sideways for a frame
                    double edgeLen = Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));
                    if (edgeLen > (shortDim + longDim) / 2)
                        continue;

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

                    // Distance to the end's midpoint, i.e. the marker's position along the
                    // unit's long axis — how far the marker sits toward a side is irrelevant
                    // to which end is the front, so it must not influence the score
                    double dist = midFront.DistanceTo(point.Value);

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

    // Last seen dir marker per unit colour, used to hold orientation through LED dropout frames
    private readonly Dictionary<string, Point> _lastDirMarker = new();

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
        using var goZoneOverlaySrc = OpenCvHelpers.InverseMaskOverlay(config.Vision.goZone);
        _goZoneOverlayPrepared = Blend.Prepare(goZoneOverlaySrc, 0.4);

        _trackMask = UnitFinder.BuildTrackMask(
            new Size(CaptureService.DETECTION_WIDTH, CaptureService.DETECTION_HEIGHT), config.Layout);


        processLoop = Task.Run(async () =>
        {
            using var frame = new Mat();

            while (!cancellationToken.IsCancellationRequested && !token.IsCancellationRequested)
            {
                try
                {
                    await captureService.DetectorFrameSignal.WaitAsync(token.Token);
                    // Drain signals that built up while processing the previous frame,
                    // so we always work on the latest frame instead of a backlog
                    while (captureService.DetectorFrameSignal.Wait(0))
                    {
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }

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
        _trackMask?.Dispose();
        token.Dispose();
    }
}

public class Uncouple
{
    public int Address { get; set; }

    public int Coupler { get; set; }

    public Vector2Int Position { get; set; }
}