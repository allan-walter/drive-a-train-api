using DriveATrain.Services;
using OpenCvSharp;

namespace DriveATrain.OpenCv;

public class LayoutDraw
{
    public static Mat DrawLayout(Config config)
    {
        Mat frame = new Mat(new Size(CaptureService.DETECTION_WIDTH, CaptureService.DETECTION_HEIGHT), MatType.CV_8UC4,
            new Scalar(0, 0, 0, 0));


        foreach (var edge in config.Vision.Layout.Edges)
        {
            Cv2.Line(frame, edge.A.ToPoint(), edge.B.ToPoint(), new Scalar(0, 255, 0, 255), 3);
        }

        return frame;
    }

    public static void DrawUnits(Mat frame, List<UnitMarkerResponse> units)
    {
        foreach (var unit in units)
        {
            var box = unit.Box;
            for (int i = 0; i < box.Count; i++)
            {
                var p1 = box[i];
                var p2 = box[(i + 1) % box.Count]; // wraps last point back to first

                Cv2.Line(frame, new Point(p1.X, p1.Y), new Point(p2.X, p2.Y),
                    new Scalar(0, 255, 0, 255), 3);
            }
        }
    }
}