using DriveATrain.Data;
using DriveATrain.OpenCv;
using DriveATrain.Services;

namespace DriveATrain;

public static class PathProjector
{
    public class ProjectionResult
    {
        public Vector2Int Point; // the projected point (rounded to int grid)

        public Node Node;
        public Edge CurrentEdge;

        // Depends on the distance
        public List<Edge> TouchedEdges;
        public SpeedLimit SpeedLimit;
        public double DistanceSq;
    }

    // Node is needed so it knows what edges to continue down on, TODO what does this do for a unit thats on an inactive path (not reachable by turnout)
    public static ProjectionResult ProjectDistance(Layout layout, Node pNode, Vector2Int p, double dist)
    {
        ProjectionResult? best = null;
        var edges = layout.ConnectedEdgesByTurnout(pNode);

        // Step one, loop all edges and find the closest one, and out position on that edge
        for (int edgeIndex = 0; edgeIndex < edges.Count; edgeIndex++)
        {
            var edge = edges[edgeIndex];
            var nodeA = layout.Nodes.First(n => n.Id == edge.A);
            var nodeB = layout.Nodes.First(n => n.Id == edge.B);

            var a = layout.Nodes.First(n => n.Id == edge.A).Point;
            var b = layout.Nodes.First(n => n.Id == edge.B).Point;

            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double lenSq = dx * dx + dy * dy;

            double t;
            double projX, projY;

            if (lenSq < 1e-12)
            {
                t = 0;
                projX = a.X;
                projY = a.Y;
            }
            else
            {
                t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq;
                t = Math.Clamp(t, 0.0, 1.0);
                projX = a.X + t * dx;
                projY = a.Y + t * dy;
            }

            int roundedX = (int)Math.Round(projX);
            int roundedY = (int)Math.Round(projY);

            double ddx = roundedX - p.X;
            double ddy = roundedY - p.Y;
            double distSq = ddx * ddx + ddy * ddy;

            if (best == null || distSq < best.DistanceSq)
            {
                // whichever endpoint the projection landed nearer to (by t) is the closest node on this edge
                var closestNode = t <= 0.5 ? nodeA : nodeB;

                best = new ProjectionResult
                {
                    Node = closestNode,
                    Point = new Vector2Int(roundedX, roundedY),
                    CurrentEdge = edge,
                    TouchedEdges = new List<Edge> { edge },
                    DistanceSq = distSq
                };
            }
        }

        if (best == null)
            throw new Exception("Hmmmm");


        return best;
    }

    // public ProjectionResult Project(Vector2Int p)
    // {
    //     ProjectionResult best = new ProjectionResult { DistanceSq = double.MaxValue };
    //     bool found = false;
    //
    //     for (int edgeIndex = 0; edgeIndex < layout.Edges.Count; edgeIndex++)
    //     {
    //         var edge = layout.Edges[edgeIndex];
    //
    //         var a = layout.Nodes.First(n => n.Id == edge.A).Point;
    //         var b = layout.Nodes.First(n => n.Id == edge.B).Point;
    //
    //         double dx = b.X - a.X;
    //         double dy = b.Y - a.Y;
    //         double lenSq = dx * dx + dy * dy;
    //
    //         double t;
    //         double projX, projY;
    //
    //         if (lenSq < 1e-12)
    //         {
    //             t = 0;
    //             projX = a.X;
    //             projY = a.Y;
    //         }
    //         else
    //         {
    //             t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq;
    //             t = Math.Clamp(t, 0.0, 1.0);
    //             projX = a.X + t * dx;
    //             projY = a.Y + t * dy;
    //         }
    //
    //         int roundedX = (int)Math.Round(projX);
    //         int roundedY = (int)Math.Round(projY);
    //
    //         double ddx = roundedX - p.X;
    //         double ddy = roundedY - p.Y;
    //         double distSq = ddx * ddx + ddy * ddy;
    //
    //         if (distSq < best.DistanceSq)
    //         {
    //             best = new ProjectionResult
    //             {
    //                 Point = new Vector2Int(roundedX, roundedY),
    //                 PathIndex = edgeIndex,
    //                 T = t,
    //                 DistanceSq = distSq
    //             };
    //             found = true;
    //         }
    //     }
    //
    //     if (!found)
    //         throw new InvalidOperationException("No valid paths with at least 2 points were provided.");
    //
    //     return best;
    // }
}