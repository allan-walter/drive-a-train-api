using System.Security.Cryptography.Xml;
using DriveATrain.Services;
using Microsoft.Extensions.Options;
using OpenCvSharp;

namespace DriveATrain.OpenCv;

public class Try4 
{

    private readonly BackgroundSubtractorMOG2 _mog2;

    private static readonly Size Blur = new Size(9, 9);
    private List<UnitDefinition> units;

    Mat goZone = Cv2.ImRead("Images/go zone.png");

    public Try4(IOptions<List<UnitDefinition>> units)
    {
        this.units = units.Value;

        _mog2 = BackgroundSubtractorMOG2.Create(history: 500, varThreshold: 150.0, detectShadows: true);
        _mog2.VarThresholdGen = 1.0;
        _mog2.ShadowThreshold = 0.4;
        _mog2.NMixtures = 8;

        //TrainFromDirectory(@"C:\Users\Allan\source\RunATrain\Detector Images\empty");
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
        Mat first = null;

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

    private Mat GetDiffMask(Mat liveFrame)
    {
        var fgMask = new Mat();

        const double liveLearningRate = 0.5;
        _mog2.Apply(liveFrame, fgMask, liveLearningRate);

        var cut = new Mat();
        fgMask.CopyTo(cut, goZone);

        return cut;
    }

    public List<MarkerDef> GetMarkerSeeds(Mat frame)
    {
        Cv2.GaussianBlur(frame, frame, Blur, 0);

        var res = GetDiffMask(frame);

        DebugWindow.Show("raw diff", res.Clone());

        Cv2.Threshold(res, res, 254.0, 255.0, ThresholdTypes.Binary);

        var kernelOpen = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(21, 21));
        Cv2.MorphologyEx(res, res, MorphTypes.Open, kernelOpen);

        DebugWindow.Show("step 0", res.Clone());

        var kernelClose = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(53, 53));
        Cv2.MorphologyEx(res, res, MorphTypes.Close, kernelClose);

        DebugWindow.Show("step 1", res.Clone());

        kernelOpen = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(15, 15));
        Cv2.MorphologyEx(res, res, MorphTypes.Open, kernelOpen);

        DebugWindow.Show("step 3", res.Clone());

        var convexMask = ConvexHullMask(res);

        var cutout = new Mat();
        var blurredFrame = new Mat();
        Cv2.GaussianBlur(frame, blurredFrame, new Size(71, 71), 0);
        blurredFrame.CopyTo(cutout, convexMask);

        var convexColorMasks =
            SplitMaskByNearestColor(frame, convexMask, LookupColor.Colors.Select(c => c.SingleColor).ToList());
        var rawColorMasks = SplitMaskByNearestColor(frame, res, LookupColor.Colors.Select(c => c.SingleColor).ToList());

        var markerDefs = new List<MarkerDef>();

        for (int index = 0; index < convexColorMasks.Count; index++)
        {
            var mask = convexColorMasks[index];
            DebugWindow.Show($"mask {index}", mask);

            var center = GetCenterOfShape(mask);
            var color = LookupColor.Colors[index];

            if (center != null)
            {
                markerDefs.Add(new MarkerDef(
                    -1,
                    color,
                    index == 0
                        ? units.ElementAtOrDefault(0)
                        : units.ElementAtOrDefault(1),
                    center.Value.ToPoint(),
                    mask,
                    rawColorMasks[index]
                ));
            }
        }

        return markerDefs;
    }

    public List<Point> IdentifyDirectionMarkers(Mat frame, List<MarkerDef> blobs, Mat mask)
    {
        var hsv = new Mat();
        Cv2.CvtColor(frame, hsv, ColorConversionCodes.BGR2HSV);

        var debug = new Mat();
        Cv2.InRange(
            hsv,
            new Scalar(0, 0, 180),
            new Scalar(180, 40, 255),
            debug
        );
        DebugWindow.Show("WHAATTTTTT", debug.Clone());

        var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(15, 15));
        Cv2.MorphologyEx(debug, debug, MorphTypes.Open, kernel);

        var cutout = new Mat();
        debug.CopyTo(cutout, mask);

        Cv2.FindContours(cutout, out Point[][] contours, out HierarchyIndex[] hierarchy,
            RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        var markers = new List<Point>();
        var bits = new List<Point[]>();

        foreach (var contour in contours)
        {
            var area = Cv2.ContourArea(contour);
            Cv2.DrawContours(frame, new[] { contour }, -1, Colors.ORANGE, -1);

            var contour2f = contour.Select(p => new Point2f(p.X, p.Y)).ToArray();
            var rect = Cv2.MinAreaRect(contour2f);
            var center = rect.Center;

            Cv2.PutText(frame, ((int)area).ToString(), new Point((int)center.X, (int)center.Y),
                HersheyFonts.HersheySimplex, 3.0, Colors.RED);

            if (area > 300 && area < 400.0)
            {
                bits.Add(contour);
            }
            else
            {
                Cv2.Circle(frame, new Point((int)center.X, (int)center.Y), 3, Colors.RED, -1);
            }
        }

        foreach (var b in bits)
        {
            Cv2.DrawContours(frame, new[] { b }, -1, Colors.ORANGE, -1);
            var contour2f = b.Select(p => new Point2f(p.X, p.Y)).ToArray();
            var rect = Cv2.MinAreaRect(contour2f);
            var center = rect.Center;
            Cv2.Circle(frame, new Point((int)center.X, (int)center.Y), 3, Colors.GREEN, -1);
            markers.Add(center.ToPoint());
        }

        DebugWindow.Show("AAXXXX", frame.Clone());
        return markers;
    }

    public List<UnitMarkerResponse> GetRects(Mat frame, List<MarkerDef> markers, List<Point> dirMarkers)
    {
        var res = new List<UnitMarkerResponse>();

        foreach (var marker in markers)
        {
            Cv2.FindContours(marker.Mask, out Point[][] contours, out HierarchyIndex[] hierarchy,
                RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            if (contours.Length != 1)
                continue; // mirrors `return@forEachIndexed`

            var contour2f = contours[0].Select(p => new Point(p.X, p.Y)).ToArray();
            var rotatedRect = Cv2.MinAreaRect(contour2f);
            var boxPoints = rotatedRect.Points(); // Point2f[4]

            var box = boxPoints.Select(p => new Vector2Int((int)p.X, (int)p.Y)).ToArray();

            for (int i = 0; i < 4; i++)
            {
                Cv2.Line(frame,
                    new Point((int)boxPoints[i].X, (int)boxPoints[i].Y),
                    new Point((int)boxPoints[(i + 1) % 4].X, (int)boxPoints[(i + 1) % 4].Y),
                    marker.Color.SingleColor, 2);
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

                Cv2.Circle(frame, new Point(best.front.Position.X, best.front.Position.Y), 20, Colors.GREEN);

                res.Add(new UnitMarkerResponse(box.ToList(), best.front, best.back, marker));
            }
        }

        DebugWindow.Show("debug frame", frame.Clone());

        return res;
    }

    // ---- Helpers referenced but not shown in the original snippet ----
    // Port these to match your actual Kotlin implementations.

    private Mat ConvexHullMask(Mat mask)
    {
        Cv2.FindContours(mask, out Point[][] contours, out HierarchyIndex[] hierarchy,
            RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        var result = Mat.Zeros(mask.Size(), mask.Type()).ToMat();

        var allPoints = contours.SelectMany(c => c).ToArray();
        if (allPoints.Length == 0) return result;

        var hull = Cv2.ConvexHull(allPoints);
        Cv2.FillConvexPoly(result, hull, new Scalar(255));

        return result;
    }

    private List<Mat> SplitMaskByNearestColor(Mat frame, Mat mask, List<Scalar> targetColors)
    {
        var results = targetColors.Select(_ => Mat.Zeros(mask.Size(), MatType.CV_8UC1).ToMat()).ToList();

        for (int y = 0; y < mask.Rows; y++)
        {
            for (int x = 0; x < mask.Cols; x++)
            {
                if (mask.At<byte>(y, x) == 0) continue;

                var pixel = frame.At<Vec3b>(y, x);
                double bestDist = double.MaxValue;
                int bestIndex = -1;

                for (int i = 0; i < targetColors.Count; i++)
                {
                    var c = targetColors[i];
                    double db = pixel.Item0 - c.Val0;
                    double dg = pixel.Item1 - c.Val1;
                    double dr = pixel.Item2 - c.Val2;
                    double dist = db * db + dg * dg + dr * dr;

                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestIndex = i;
                    }
                }

                if (bestIndex >= 0)
                    results[bestIndex].Set(y, x, (byte)255);
            }
        }

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
        // OpenCvSharp RotatedRect doesn't expose a direct containsPoint helper,
        // so approximate using the bounding polygon test.
        var pts = rect.Points();
        using var poly = new Mat(4, 1, MatType.CV_32FC2);
        for (int i = 0; i < 4; i++)
            poly.Set(i, 0, pts[i]);

        return Cv2.PointPolygonTest(InputArray.Create(pts), p, false) >= 0;
    }
}