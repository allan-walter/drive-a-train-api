using OpenCvSharp;

namespace DriveATrain.OpenCv;

public static class LineHelpers
{
    public static void DrawGradientLine(Mat img, Point pt1, Point pt2, Scalar color1, Scalar color2, int thickness = 2)
    {
        double length = Math.Sqrt(Math.Pow(pt2.X - pt1.X, 2) + Math.Pow(pt2.Y - pt1.Y, 2));
        int segments = Math.Max((int)length, 2);

        for (int i = 0; i < segments; i++)
        {
            double t0 = (double)i / segments;
            double t1 = (double)(i + 1) / segments;

            Point p0 = new Point(
                pt1.X + (pt2.X - pt1.X) * t0,
                pt1.Y + (pt2.Y - pt1.Y) * t0
            );
            Point p1 = new Point(
                pt1.X + (pt2.X - pt1.X) * t1,
                pt1.Y + (pt2.Y - pt1.Y) * t1
            );

            double tm = (t0 + t1) / 2.0;

            // BGRA interpolation
            Scalar color = new Scalar(
                color1.Val0 + (color2.Val0 - color1.Val0) * tm, // B
                color1.Val1 + (color2.Val1 - color1.Val1) * tm, // G
                color1.Val2 + (color2.Val2 - color1.Val2) * tm, // R
                color1.Val3 + (color2.Val3 - color1.Val3) * tm // A
            );

            Cv2.Line(img, p0, p1, color, thickness, LineTypes.AntiAlias);
        }
    }
}