using DriveATrain.Data;
using DriveATrain.OpenCv;

namespace DriveATrain.Services.Layout;

public class LayoutService
{
    private Config config;
    private Data.Layout layout;

    public List<TurnoutState> Turnouts = new();
    public List<TrackPath> Paths;

    public LayoutService(Config config)
    {
        this.config = config;
        layout = config.Layout;

        Turnouts = layout.Turnouts
            .Select(t => new TurnoutState(t, GetEdgesAt(layout.Nodes.First(n => n.Id == t.NodeId)))).ToList();
    }


    public List<Edge> GetEdgesAt(Node n)
    {
        return layout.Edges.Where(e => e.A == n.Id || e.B == n.Id).ToList();
    }

    // IMPORTANT, this isn't just the physically closest node, e.g. you could be kinda close to one, but closer to a line of 2 nodes far apart. so we're actually on that edge / path not 
    // the physically closer one
    // TODO theres kinda a double up between this and the path stuff, but this is needed before calling the path functions

    // Returns the node it deemed to be closest (there's a bit more going on than a simple distance check), and the point snapped to the path
    public (Node node, Vector2Int point) SnapToPath(Vector2Int p)
    {
        (Edge Edge, Node node, double dist)? best = null;

        for (int edgeIndex = 0; edgeIndex < layout.Edges.Count; edgeIndex++)
        {
            var edge = layout.Edges[edgeIndex];
            var nodeA = layout.Nodes.First(n => n.Id == edge.A);
            var nodeB = layout.Nodes.First(n => n.Id == edge.B);

            var a = nodeA.Point;
            var b = nodeB.Point;


            // The end needs moved back a bit so there is clear separation and it doesn’t accidentally get picked up by a train on the turnout. The path is still valid and there could be something on it, there just needs to be a clear separation 
            // So if there is only 1 connected node then thats going the other way, its disconnected
            // // TODO no, this would do it at the end of a path thats not close to a turnout
            // if (layout.Turnouts.Any(t => t.Node.Id == nodeA.Id))
            // {
            //     // Loose idea is the user couldnt move anything closer than this, so should be safe to move it by that much
            //     ProjectDistance(nodeA, a, config.Vision.StopWhenPixelsLessThan);
            // }
            //
            // if (layout.Turnouts.Any(t => t.Node.Id == nodeB.Id))
            // {
            //     // Loose idea is the user couldnt move anything closer than this, so should be safe to move it by that much
            //     ProjectDistance(nodeB, b, config.Vision.StopWhenPixelsLessThan);
            // }

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

            if (best == null || distSq < best.Value.dist)
            {
                // whichever endpoint the projection landed nearer to (by t) is the closest node on this edge
                var closestNode = t <= 0.5 ? nodeA : nodeB;

                best = (edge, closestNode, distSq);
            }
        }

        return (best.Value.node, new Vector2Int());
    }




    // Node is needed so it knows what edges to continue down on, it should probably be close the position but doesn' matter too much, TODO what does this do for a unit thats on an inactive path (not reachable by turnout)
    // public ProjectionResult ProjectDistance(Node pNode, Vector2Int p, double dist)
    // {
    //     ProjectionResult? best = null;
    //     var edges = ConnectedEdgesByTurnout(pNode);
    //
    //     // Step one, loop all edges and find the closest one, and out position on that edge
    //     for (int edgeIndex = 0; edgeIndex < edges.Count; edgeIndex++)
    //     {
    //         var edge = edges[edgeIndex];
    //         var nodeA = layout.Nodes.First(n => n.Id == edge.A);
    //         var nodeB = layout.Nodes.First(n => n.Id == edge.B);
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
    //         if (best == null || distSq < best.DistanceSq)
    //         {
    //             // whichever endpoint the projection landed nearer to (by t) is the closest node on this edge
    //             var closestNode = t <= 0.5 ? nodeA : nodeB;
    //
    //             best = new ProjectionResult
    //             {
    //                 Node = closestNode,
    //                 Point = new Vector2Int(roundedX, roundedY),
    //                 CurrentEdge = edge,
    //                 TouchedEdges = new List<Edge> { edge },
    //                 DistanceSq = distSq
    //             };
    //         }
    //     }
    //
    //     if (best == null)
    //         throw new Exception("Hmmmm");
    //
    //
    //     return best;
    // }
}

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