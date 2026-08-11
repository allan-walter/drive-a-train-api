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
        var turnoutsByNode = layout.Turnouts.ToDictionary(n => n.NodeId);

        Edge? bestEdge = null;
        Vector2Double bestProj = default;
        double bestDistSq = double.MaxValue;
        double bestT = 0;
        var targetPoint = new Vector2Double(p.X, p.Y);

        foreach (var edge in layout.Edges)
        {
            var nodeA = nodesById[edge.A];
            var nodeB = nodesById[edge.B];
            var a = new Vector2Double(nodesById[edge.A].Point.X, nodesById[edge.A].Point.Y);
            var b = new Vector2Double(nodesById[edge.B].Point.X, nodesById[edge.B].Point.Y);


            // If we're and the start or end of a path, and the node is a turnout
            // The end needs moved back a bit so there is clear separation and it doesn’t accidentally get picked up by a train on the turnout. The path is still valid and there could be something on it, there just needs to be a clear separation 
            var nodeAPath = Paths.FirstOrDefault(p =>
                p.StartNode.Id == edge.A || p.StartNode.Id == edge.B ||
                p.EndNode.Id == edge.A || p.EndNode.Id == edge.B);
            if (turnoutsByNode.ContainsKey(edge.A) && nodeAPath != null)
            {
                MoveAlongPath(a, nodeAPath)
            }

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

        var bestNode = bestT <= 0.5 ? nodesById[bestEdge.A] : nodesById[bestEdge.B];

        return new ProjectionResult
        {
            Node = bestNode,
            Point = new Vector2Int((int)Math.Round(bestProj.X), (int)Math.Round(bestProj.Y)),
            Direction = bestT <= 0.5 ? SeekDirection.Up : SeekDirection.Down,
            Distance = Math.Sqrt(bestDistSq)
        };
    }

    public ProjectionResult MoveAlongPath(
        Vector2Int position,
        TrackPath path,
        Edge currentEdge,
        double distance,
        SeekDirection direction)
    {
        if (distance < 0)
            throw new ArgumentOutOfRangeException(nameof(distance), "Distance must be non-negative.");

        if (path.Edges.Count == 0)
            throw new InvalidOperationException("Cannot move along an empty path.");

        int edgeIndex = FindEdgeIndex(path, currentEdge);
        var touchedEdges = new List<Edge> { path.Edges[edgeIndex] };
        var projectedPosition = new Vector2Double(position.X, position.Y);
        double remainingDistance = distance;

        while (true)
        {
            var edge = path.Edges[edgeIndex];
            Node fromNode = GetTraversalStartNode(path, edgeIndex, direction);
            Node toNode = GetTraversalEndNode(path, edgeIndex, direction);

            var fromPoint = new Vector2Double(fromNode.Point.X, fromNode.Point.Y);
            var toPoint = new Vector2Double(toNode.Point.X, toNode.Point.Y);
            var (snappedPoint, t) = ClosestPointOnSegment(fromPoint, toPoint, projectedPosition);

            double dx = toPoint.X - fromPoint.X;
            double dy = toPoint.Y - fromPoint.Y;
            double edgeLength = Math.Sqrt((dx * dx) + (dy * dy));

            if (edgeLength < 1e-12)
            {
                if (IsPathEnd(edgeIndex, path, direction))
                {
                    return new ProjectionResult
                    {
                        Point = toNode.Point,
                        Node = toNode,
                        Edge = edge,
                        Path = path,
                        TouchedEdges = touchedEdges,
                        Distance = distance - remainingDistance,
                        Direction = direction
                    };
                }

                edgeIndex += direction == SeekDirection.Down ? 1 : -1;
                var nextEdge = path.Edges[edgeIndex];
                if (!EdgesMatch(touchedEdges[^1], nextEdge))
                    touchedEdges.Add(nextEdge);
                projectedPosition = toPoint;
                continue;
            }

            double distanceToEdgeEnd = edgeLength * (1.0 - t);

            if (remainingDistance <= distanceToEdgeEnd)
            {
                double travelT = t + (remainingDistance / edgeLength);
                var resultPoint = new Vector2Double(
                    fromPoint.X + (dx * travelT),
                    fromPoint.Y + (dy * travelT));

                return new ProjectionResult
                {
                    Point = new Vector2Int((int)Math.Round(resultPoint.X), (int)Math.Round(resultPoint.Y)),
                    Node = GetClosestNode(fromNode, toNode, resultPoint),
                    Edge = edge,
                    Path = path,
                    TouchedEdges = touchedEdges,
                    Distance = distance,
                    Direction = direction
                };
            }

            remainingDistance -= distanceToEdgeEnd;
            projectedPosition = toPoint;

            if (IsPathEnd(edgeIndex, path, direction))
            {
                return new ProjectionResult
                {
                    Point = toNode.Point,
                    Node = toNode,
                    Edge = edge,
                    Path = path,
                    TouchedEdges = touchedEdges,
                    Distance = distance - remainingDistance,
                    Direction = direction
                };
            }

            edgeIndex += direction == SeekDirection.Down ? 1 : -1;
            var nextPathEdge = path.Edges[edgeIndex];
            if (!EdgesMatch(touchedEdges[^1], nextPathEdge))
                touchedEdges.Add(nextPathEdge);
        }
    }

    private static (Vector2Double point, double t) ClosestPointOnSegment(Vector2Double a, Vector2Double b,
        Vector2Double p)
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

        var orderedEdges = pathEdges.ToList();

        return new TrackPath
        {
            Edges = orderedEdges,
            StartNode = GetPathStartNode(orderedEdges),
            EndNode = GetPathEndNode(orderedEdges)
        };
    }

    private int FindEdgeIndex(TrackPath path, Edge edge)
    {
        int edgeIndex = path.Edges.FindIndex(e => EdgesMatch(e, edge));

        if (edgeIndex < 0)
            throw new InvalidOperationException("The provided edge is not part of the supplied path.");

        return edgeIndex;
    }

    private static bool EdgesMatch(Edge left, Edge right)
    {
        return (left.A == right.A && left.B == right.B) ||
               (left.A == right.B && left.B == right.A);
    }

    private Node GetPathStartNode(List<Edge> edges)
    {
        if (edges.Count == 0)
            throw new InvalidOperationException("Cannot determine the start node of an empty path.");

        if (edges.Count == 1)
            return layout.Nodes.First(n => n.Id == edges[0].A);

        return GetPathOuterNode(edges[0], edges[1]);
    }

    private Node GetPathEndNode(List<Edge> edges)
    {
        if (edges.Count == 0)
            throw new InvalidOperationException("Cannot determine the end node of an empty path.");

        if (edges.Count == 1)
            return layout.Nodes.First(n => n.Id == edges[0].B);

        return GetPathOuterNode(edges[^1], edges[^2]);
    }

    private Node GetPathOuterNode(Edge edge, Edge adjacent)
    {
        Guid sharedNodeId = GetSharedNodeId(edge, adjacent);
        Guid outerNodeId = edge.A == sharedNodeId ? edge.B : edge.A;
        return GetNodeById(outerNodeId);
    }

    private Node GetTraversalStartNode(TrackPath path, int edgeIndex, SeekDirection direction)
    {
        return direction == SeekDirection.Down
            ? GetNodeBeforeEdge(path, edgeIndex)
            : GetNodeAfterEdge(path, edgeIndex);
    }

    private Node GetTraversalEndNode(TrackPath path, int edgeIndex, SeekDirection direction)
    {
        return direction == SeekDirection.Down
            ? GetNodeAfterEdge(path, edgeIndex)
            : GetNodeBeforeEdge(path, edgeIndex);
    }

    private Node GetNodeBeforeEdge(TrackPath path, int edgeIndex)
    {
        if (edgeIndex == 0)
            return path.StartNode;

        Guid sharedNodeId = GetSharedNodeId(path.Edges[edgeIndex - 1], path.Edges[edgeIndex]);
        return GetNodeById(sharedNodeId);
    }

    private Node GetNodeAfterEdge(TrackPath path, int edgeIndex)
    {
        if (edgeIndex == path.Edges.Count - 1)
            return path.EndNode;

        Guid sharedNodeId = GetSharedNodeId(path.Edges[edgeIndex], path.Edges[edgeIndex + 1]);
        return GetNodeById(sharedNodeId);
    }

    private bool IsPathEnd(int edgeIndex, TrackPath path, SeekDirection direction)
    {
        return direction == SeekDirection.Down
            ? edgeIndex == path.Edges.Count - 1
            : edgeIndex == 0;
    }

    private Guid GetSharedNodeId(Edge first, Edge second)
    {
        if (first.A == second.A || first.A == second.B)
            return first.A;

        if (first.B == second.A || first.B == second.B)
            return first.B;

        throw new InvalidOperationException("Path edges must be connected.");
    }

    private Node GetNodeById(Guid nodeId)
    {
        return layout.Nodes.First(n => n.Id == nodeId);
    }

    private static Node GetClosestNode(Node a, Node b, Vector2Double point)
    {
        double distToA = DistanceSquared(point, a.Point);
        double distToB = DistanceSquared(point, b.Point);
        return distToA <= distToB ? a : b;
    }

    private static double DistanceSquared(Vector2Double point, Vector2Int target)
    {
        double dx = point.X - target.X;
        double dy = point.Y - target.Y;
        return (dx * dx) + (dy * dy);
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