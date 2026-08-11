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

    // TODO do yiu really need a separate path service? Everytgubng ended up in it so it might aswell just be here
    // IMPORTANT, this isn't just the physically closest node, e.g. you could be kinda close to one, but closer to a line of 2 nodes far apart. so we're actually on that edge / path not 
    // the physically closer one
    // TODO theres kinda a double up between this and the path stuff, but this is needed before calling the path functions

    // Returns the node it deemed to be closest (there's a bit more going on than a simple distance check), and the point snapped to the path
    public ProjectionResult ProjectOnPath(Vector2Int p)
    {
        var nodesById = layout.Nodes.ToDictionary(n => n.Id);

        Edge? bestEdge = null;
        Vector2Double bestProj = default;
        double bestDistSq = double.MaxValue;
        double bestT = 0;
        var targetPoint = new Vector2Double(p.X, p.Y);

        foreach (var edge in layout.Edges)
        {
            var a = new Vector2Double(nodesById[edge.A].Point.X, nodesById[edge.A].Point.Y);
            var b = new Vector2Double(nodesById[edge.B].Point.X, nodesById[edge.B].Point.Y);

            var (proj, t) = ClosestPointOnSegment(a, b, targetPoint);
            double dx = proj.X - targetPoint.X;
            double dy = proj.Y - targetPoint.Y;
            double distSq = dx * dx + dy * dy;

            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                bestEdge = edge;
                bestProj = proj;
                bestT = t;
            }
        }

        if (bestEdge == null)
            throw new InvalidOperationException("Cannot project onto a path with no edges.");

        var bestNode = bestT <= 0.5 ? nodesById[bestEdge.A] : nodesById[bestEdge.B];

        return new ProjectionResult
        {
            Node = bestNode,
            Point = new Vector2Int((int)Math.Round(bestProj.X), (int)Math.Round(bestProj.Y)),
            Direction = bestT <= 0.5 ? SeekDirection.Up : SeekDirection.Down,
            Distance = Math.Sqrt(bestDistSq)
        };
    }

    private static (Vector2Double point, double t) ClosestPointOnSegment(Vector2Double a, Vector2Double b, Vector2Double p)
    {
        double abX = b.X - a.X;
        double abY = b.Y - a.Y;
        double lenSq = abX * abX + abY * abY;

        if (lenSq < 1e-12)
            return (a, 0);

        double apX = p.X - a.X;
        double apY = p.Y - a.Y;
        double t = Math.Clamp(((apX * abX) + (apY * abY)) / lenSq, 0.0, 1.0);

        return (new Vector2Double(a.X + (abX * t), a.Y + (abY * t)), t);
    }


    // Active path a given node is on
    public List<Edge> ActivePath(Node node)
    {
        // TODO I dont think this is right, but you can hopefully see what im trying to do
        return Paths.First(p => p.Edges.Any(e => e.A == node.Id || e.B == node.Id)).Edges;
    }

    // Update the current track state to match the turnout state
    public void CalculatePathsByTurnout()
    {
        var paths = new List<TrackPath>();
        var visitedEdges = new HashSet<Edge>();

        foreach (var startEdge in config.Layout.Edges)
        {
            // Skip if this edge was already claimed by an earlier path trace
            if (visitedEdges.Contains(startEdge))
                continue;

            var path = CreatePath(startEdge, visitedEdges);
            paths.Add(path);
        }

        Paths = paths;
    }

    private TrackPath CreatePath(Edge startEdge, HashSet<Edge> visitedEdges)
    {
        var pathEdges = new LinkedList<Edge>();
        pathEdges.AddFirst(startEdge);
        visitedEdges.Add(startEdge);

        // Get the initial nodes on both ends of the starting edge
        Node nodeA = config.Layout.Nodes.First(n => n.Id == startEdge.A);
        Node nodeB = config.Layout.Nodes.First(n => n.Id == startEdge.B);

        // 1. Trace outward from Node A (Heading "Backward")
        TraceDirection(nodeA, startEdge, pathEdges, visitedEdges, SeekDirection.Down);

        // 2. Trace outward from Node B (Heading "Forward")
        TraceDirection(nodeB, startEdge, pathEdges, visitedEdges, SeekDirection.Up);

        return new TrackPath { Edges = pathEdges.ToList() };
    }

    private void TraceDirection(
        Node current,
        Edge arrivedVia,
        LinkedList<Edge> pathEdges,
        HashSet<Edge> visitedEdges,
        SeekDirection direction)
    {
        while (true)
        {
            // Ask the layout where the track leads when coming from 'arrivedVia' into 'current'
            Edge? nextEdge = GetNextEdge(current, arrivedVia);

            // Stop if we hit a dead end, a turned-off switch, or a closed loop
            if (nextEdge == null || visitedEdges.Contains(nextEdge))
                break;

            visitedEdges.Add(nextEdge);

            if (direction == SeekDirection.Down)
                pathEdges.AddFirst(nextEdge);
            else
                pathEdges.AddLast(nextEdge);

            // Advance to the node at the far side of nextEdge
            Guid nextNodeId = (nextEdge.A == current.Id) ? nextEdge.B : nextEdge.A;
            current = config.Layout.Nodes.First(n => n.Id == nextNodeId);
            arrivedVia = nextEdge;
        }
    }


    private Edge? GetNextEdge(Node currentNode, Edge arrivedVia)
    {
        var edgesAtNode = GetEdgesAt(currentNode);

        // Dead end
        if (edgesAtNode.Count <= 1)
            return null;

        var turnout = Turnouts.FirstOrDefault(t => t.Turnout.NodeId == currentNode.Id);

        if (turnout == null)
        {
            // Regular point: just take the other edge
            return edgesAtNode.First(e => e != arrivedVia);
        }

        // Turnout: only the edge paired with arrivedVia in the active route is legal
        var (a, b) = turnout.Routes[turnout.ActiveRoute];

        if (a.Equals(arrivedVia)) return b;
        if (b.Equals(arrivedVia)) return a;

        return null; // arrived via an edge not part of the active route — blocked
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

    // 
    public Node Node;

    public Edge Edge;

    public TrackPath Path;

    // Depends on the distance
    public List<Edge> TouchedEdges;
    public double Distance;
    public SeekDirection Direction;
}

public enum SeekDirection
{
    // Up the edge graph, so closer to zero
    Up,

    // Continue down the graph, increasing index
    Down
}