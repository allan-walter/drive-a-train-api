using DriveATrain.Data;
using DriveATrain.OpenCv;
using OpenCvSharp;

namespace DriveATrain.Services.Layout;

public class LayoutDrawingService(LayoutService layoutService, Config config)
{
    // The paths are highlighted
    // Trainpos highlights the paths the train can access. There could be units parked on inactive routes
    public void DrawLayout(Vector2Int? trainPos, Mat frame)
    {
        ProjectionResult? projection = trainPos.HasValue ? layoutService.ProjectOnPath(trainPos.Value) : null;
        // var trainNode = trainPos.HasValue
        //     ? config.Layout.ProjectDistance(closetNode.Value, trainPos.Value, 0)?.Node
        //     : null;

        var highlighted = projection != null
            ? new HashSet<Edge>(layoutService.Paths.First(p => p.Edges.Contains(projection.Edge)).Edges)
            : new HashSet<Edge>();
        //
        foreach (var edge in config.Layout.Edges)
        {
            var a = config.Layout.Nodes.First(n => n.Id == edge.A);
            var b = config.Layout.Nodes.First(n => n.Id == edge.B);

            var color = highlighted.Contains(edge) ? Colors.White : Colors.LightGray;
            var thickness = highlighted.Contains(edge) ? 2 : 1;
            Cv2.Line(frame, a.Point.ToPoint(), b.Point.ToPoint(), color, thickness);
        }
    }

    public void DrawUnits(Mat frame, List<UnitMarkerResponse> units)
    {
        foreach (var unit in units)
        {
            // Gradiant so as the ends overlap can still get a direction from them
            LineHelpers.DrawGradientLine(frame, unit.Front.ToPoint(), unit.Back.ToPoint(), Colors.Green, Colors.Red, 3);
        }
    }
}