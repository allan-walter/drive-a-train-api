using DriveATrain.Services;
using OpenCvSharp;

namespace DriveATrain.OpenCv;

public class LayoutDraw
{
    // Scale from layout resolution to debug resolution
    public static LayoutPoint ScalePoint(LayoutPoint point)
    {
        float scaleX = (float)CaptureService.detectionWidth / CaptureService.width;
        float scaleY = (float)CaptureService.detectionHeight / CaptureService.height;

        return new LayoutPoint((int)(point.X * scaleX), (int)(point.Y * scaleY));
    }

    public static Mat DrawLayout(Config config)
    {
        Mat frame = new Mat(new Size(CaptureService.detectionWidth, CaptureService.detectionHeight), MatType.CV_8UC4,
            new Scalar(0, 0, 0, 0));


        foreach (var path in config.Vision.Layout.Paths)
        {
            for (int i = 0; i < path.Count - 2; i++)
            {
                var p1 = ScalePoint(path[i]).ToPoint();
                var p2 = ScalePoint(path[i + 1]).ToPoint();
                Cv2.Line(frame, p1, p2, new Scalar(0, 255, 0, 255), 3);
            }
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