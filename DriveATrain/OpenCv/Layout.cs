using System.Drawing;
using DriveATrain.Services;
using OpenCvSharp;
using Size = OpenCvSharp.Size;

namespace DriveATrain.OpenCv;

public static class LayoutHelpers
{
    public static Mat DrawLayout(Config config, Mat frame)
    {
        foreach (var edge in config.Layout.Edges)
        {
            var a = config.Layout.Nodes.First(n => n.Id == edge.A);
            var b = config.Layout.Nodes.First(n => n.Id == edge.B);

            Cv2.Line(frame, a.Point.ToPoint(), b.Point.ToPoint(), Colors.White, 1);
        }

        return frame;
    }

    public static void DrawUnits(Mat frame, List<UnitMarkerResponse> units)
    {
        foreach (var unit in units)
        {
            Cv2.Line(frame, unit.Front.ToPoint(), unit.Back.ToPoint(), Colors.Blue, 3);

            Cv2.Circle(frame, unit.Front.ToPoint(), 3, Colors.Green, -1);
        }
    }
}