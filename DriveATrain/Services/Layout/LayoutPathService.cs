using DriveATrain.Data;

namespace DriveATrain.Services.Layout;

// Functions for calculating the paths on a layout
public class LayoutPathService(LayoutService layoutService, Config config)
{
    
    public List<Edge> ConnectedEdgesByTurnout(Node node)
    {
        // TODO I dont think this is right, but you can hopefully see what im trying to do
        return layoutService.Paths.First(p => p.Edges.Any(e => e.A == node.Id || e.B == node.Id)).Edges;
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

            var path = TracePathFromEdge(startEdge, visitedEdges);
            paths.Add(path);
        }

        layoutService.Paths = paths;
    }

    private TrackPath TracePathFromEdge(Edge startEdge, HashSet<Edge> visitedEdges)
    {
        var pathEdges = new LinkedList<Edge>();
        pathEdges.AddFirst(startEdge);
        visitedEdges.Add(startEdge);

        // Get the initial nodes on both ends of the starting edge
        Node nodeA = config.Layout.Nodes.First(n => n.Id == startEdge.A);
        Node nodeB = config.Layout.Nodes.First(n => n.Id == startEdge.B);

        // 1. Trace outward from Node A (Heading "Backward")
        TraceDirection(nodeA, startEdge, pathEdges, visitedEdges, true);

        // 2. Trace outward from Node B (Heading "Forward")
        TraceDirection(nodeB, startEdge, pathEdges, visitedEdges, false);

        return new TrackPath { Edges = pathEdges.ToList() };
    }

    private void TraceDirection(
        Node current,
        Edge arrivedVia,
        LinkedList<Edge> pathEdges,
        HashSet<Edge> visitedEdges,
        bool addToFront)
    {
        while (true)
        {
            // Ask the layout where the track leads when coming from 'arrivedVia' into 'current'
            Edge? nextEdge = GetNextEdge(current, arrivedVia);

            // Stop if we hit a dead end, a turned-off switch, or a closed loop
            if (nextEdge == null || visitedEdges.Contains(nextEdge))
                break;

            visitedEdges.Add(nextEdge);

            if (addToFront)
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
        var edgesAtNode = layoutService.GetEdgesAt(currentNode);

        // Dead end
        if (edgesAtNode.Count <= 1)
            return null;

        var turnout = layoutService.Turnouts.FirstOrDefault(t => t.Turnout.NodeId == currentNode.Id);

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
}