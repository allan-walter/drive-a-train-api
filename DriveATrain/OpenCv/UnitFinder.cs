using DriveATrain.Data;
using DriveATrain.Services;
using OpenCvSharp;

namespace DriveATrain.OpenCv;

// Finds units by colour alone, no empty scene needed. Everything that is the right colour,
// roughly the right size and inside the track corridor comes back as a rect.
public static class UnitFinder
{
    private const string DebugCategory = "units";

    // All px constants below are stated at full camera resolution and scaled to whatever frame
    // is passed in, so the same code runs on the full res stills in ColorTest and the quarter
    // res detection frames in DetectorService.

    // Units are saturated hue matches, so the blur only needs to knock down sensor noise,
    // not average the track texture away like the old darkness-based black detection needed.
    private const int BlurSize = 5;

    private const int OpenSize = 9;
    private const int CloseSize = 13;

    // A wagon is about 160 long and 60 wide, a loco about 220. Rolling stock is a
    // long thin brick, which is most of what separates it from the tools on the bench.
    private const int MinLength = 90;
    private const int MaxLength = 400;
    private const int MinWidth = 30;
    private const int MaxWidth = 85;
    private const double MinAspect = 2.2;
    private const double MaxAspect = 6.5;
    private const double MinFill = 0.55;

    // A unit is always fully in frame, anything running off an edge is the bench or the backdrop
    private const int BorderMargin = 4;

    // Units can only be on the track, so everything outside a corridor along the layout edges
    // is bench clutter and never reaches the contour stage. Wide enough for the widest unit
    // plus blur growth plus a unit sitting slightly off the edge line.
    private const int CorridorWidth = 130;

    public sealed class Hit
    {
        public string Colour = "";
        public RotatedRect Rect;
        public double Fill;
        public Point[] Contour = [];
    }

    // Built once at startup. Layout nodes are in detection resolution, the mask is scaled up to
    // the frame it will be ANDed against.
    public static Mat BuildTrackMask(Size frameSize, Layout layout)
    {
        var mask = new Mat(frameSize, MatType.CV_8UC1, Scalar.Black);
        double nodeScale = (double)frameSize.Width / CaptureService.DETECTION_WIDTH;
        double pxScale = (double)frameSize.Width / CaptureService.CAMERA_WIDTH;
        var nodesById = layout.Nodes.ToDictionary(n => n.Id);

        foreach (var edge in layout.Edges)
        {
            var a = nodesById[edge.A].Point;
            var b = nodesById[edge.B].Point;
            Cv2.Line(mask,
                new Point(a.X * nodeScale, a.Y * nodeScale),
                new Point(b.X * nodeScale, b.Y * nodeScale),
                Scalar.White, Math.Max(1, (int)(CorridorWidth * pxScale)));
        }

        DebugWindow.Show(DebugCategory, "track mask", mask);
        return mask;
    }

    public static List<Hit> Find(Mat frame, Mat? trackMask = null)
    {
        double scale = (double)frame.Width / CaptureService.CAMERA_WIDTH;
        int Px(int fullResPx) => Math.Max(1, (int)Math.Round(fullResPx * scale));
        int Kernel(int fullResPx)
        {
            int k = (int)Math.Round(fullResPx * scale);
            return Math.Max(1, k | 1); // odd, at least 1
        }

        using var blurred = new Mat();
        int blur = Kernel(BlurSize);
        if (blur > 1)
            Cv2.GaussianBlur(frame, blurred, new Size(blur, blur), 0);
        else
            frame.CopyTo(blurred);

        using var hsv = new Mat();
        Cv2.CvtColor(blurred, hsv, ColorConversionCodes.BGR2HSV);

        using var kernelOpen = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(Kernel(OpenSize), Kernel(OpenSize)));
        using var kernelClose = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(Kernel(CloseSize), Kernel(CloseSize)));

        var hits = new List<Hit>();

        foreach (var unit in UnitColor.Colors)
        {
            using var mask = new Mat();
            InRange.InRangeHue(hsv, unit.Color, mask);
            if (trackMask != null)
                Cv2.BitwiseAnd(mask, trackMask, mask);

            Cv2.MorphologyEx(mask, mask, MorphTypes.Open, kernelOpen);
            Cv2.MorphologyEx(mask, mask, MorphTypes.Close, kernelClose);

            DebugWindow.Show(DebugCategory, $"mask {unit.Color.Name}", mask);

            Cv2.FindContours(mask, out Point[][] contours, out _,
                RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            foreach (var contour in contours)
            {
                var rect = Cv2.MinAreaRect(contour);
                double length = Math.Max(rect.Size.Width, rect.Size.Height);
                double width = Math.Min(rect.Size.Width, rect.Size.Height);

                if (length < Px(MinLength) || length > Px(MaxLength)) continue;
                if (width < Px(MinWidth) || width > Px(MaxWidth)) continue;

                double aspect = length / Math.Max(1, width);
                if (aspect < MinAspect || aspect > MaxAspect) continue;
                if (TouchesBorder(rect, frame.Size(), Px(BorderMargin))) continue;

                // A unit is a solid brick, so it fills its own bounding rect. Cables and tool
                // handles are the right size but wander around inside theirs.
                double fill = Cv2.ContourArea(contour) / (length * width);
                if (fill < MinFill) continue;

                hits.Add(new Hit { Colour = unit.Color.Name, Rect = rect, Fill = fill, Contour = contour });
            }
        }

        Draw(frame, hits);
        return hits;
    }

    private static bool TouchesBorder(RotatedRect rect, Size frame, int margin) =>
        rect.Points().Any(p => p.X < margin || p.Y < margin ||
                               p.X > frame.Width - margin || p.Y > frame.Height - margin);

    private static void Draw(Mat frame, List<Hit> hits)
    {
        if (!DebugWindow.IsEnabled(DebugCategory))
            return;

        using var debug = frame.Clone();

        foreach (var hit in hits)
        {
            var colour = hit.Colour == "Yellow" ? Colors.Yellow : Colors.Cyan;
            var pts = hit.Rect.Points().Select(p => p.ToPoint()).ToArray();

            Cv2.Polylines(debug, new[] { pts }, true, colour, 3, LineTypes.AntiAlias);
            Cv2.PutText(debug, hit.Colour, pts[1] + new Point(0, -10),
                HersheyFonts.HersheySimplex, 0.8, colour, 2, LineTypes.AntiAlias);
        }

        DebugWindow.Show(DebugCategory, "units", debug);

        var dump = Environment.GetEnvironmentVariable("UNITFINDER_DUMP"); // debug-only, inert without the env var
        if (!string.IsNullOrEmpty(dump)) Cv2.ImWrite(dump, debug);
    }
}
