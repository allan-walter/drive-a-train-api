using System.Drawing;
using DriveATrain.Data;
using DriveATrain.Services;
using OpenCvSharp;
using Size = OpenCvSharp.Size;

namespace DriveATrain.OpenCv;

public static class LayoutHelpers
{
    public static void DrawLayout(Config config, Vector2Int? trainPos, Mat frame)
    {
        Node? closetNode = trainPos.HasValue ? config.Layout.ClosestNode(trainPos.Value) : null;
        var trainNode = trainPos.HasValue ? PathProjector.ProjectDistance(config.Layout, closetNode.Value, trainPos.Value, 0)?.Node : null;

        var highlighted = trainNode.HasValue
            ? new HashSet<Edge>(config.Layout.ConnectedEdgesByTurnout(trainNode.Value))
            : new HashSet<Edge>();

        foreach (var edge in config.Layout.Edges)
        {
            var a = config.Layout.Nodes.First(n => n.Id == edge.A);
            var b = config.Layout.Nodes.First(n => n.Id == edge.B);

            var color = highlighted.Contains(edge) ? Colors.White : Colors.LightGray;
            var thickness = highlighted.Contains(edge) ? 2 : 1;
            Cv2.Line(frame, a.Point.ToPoint(), b.Point.ToPoint(), color, thickness);
        }

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