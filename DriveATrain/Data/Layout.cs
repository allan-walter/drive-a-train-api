using DriveATrain.OpenCv;
using DriveATrain.Services;

namespace DriveATrain.Data;

public class Layout
{
    public List<Node> Nodes { get; set; }
    public List<Edge> Edges { get; set; }

    public List<Turnout> Turnouts { get; set; }

    IEnumerable<Node> GetTurnouts()
    {
        return Nodes.Where(n => Edges.Count(e => e.A == n.Id || e.B == n.Id) >= 3);
    }

    List<Edge> GetEdgesAt(Node n)
    {
        return Edges.Where(e => e.A == n.Id || e.B == n.Id).ToList();
    }

    // IMPORTANT, this isn't just the physically closest node, e.g. you could be kinda close to one, but closer to a line of 2 nodes far apart. so we're actually on that edge / path not 
    // the physically closer one
    // TODO theres kinda a double up between this and the path stuff, but this is needed before calling the path functions
    public Node ClosestNode(Vector2Int p)
    {
        (Edge Edge, Node node, double dist)? best = null;

        for (int edgeIndex = 0; edgeIndex < Edges.Count; edgeIndex++)
        {
            var edge = Edges[edgeIndex];
            var nodeA = Nodes.First(n => n.Id == edge.A);
            var nodeB = Nodes.First(n => n.Id == edge.B);

            var a = Nodes.First(n => n.Id == edge.A).Point;
            var b = Nodes.First(n => n.Id == edge.B).Point;

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

        return best.Value.node;
    }

    // Probably only one set since the layout is small but for a large layout there could be multiple connected edges that the turnouts define
    public List<Edge> ConnectedEdgesByTurnout(Node start)
    {
        var result = new List<Edge>();
        var visited = new HashSet<Edge>();
        var queue = new Queue<(Node node, Edge arrivedVia)>();

        var startTurnout = Turnouts.FirstOrDefault(t => t.Node.Id == start.Id);

        if (startTurnout != null)
        {
            // only seed with the active route's pair — not every edge touching this node
            var (a, b) = startTurnout.Routes[startTurnout.ActiveRoute];
            queue.Enqueue((start, a));
            queue.Enqueue((start, b));
        }
        else
        {
            // regular point: direction unknown, all edges are valid seeds
            foreach (var e in GetEdgesAt(start))
                queue.Enqueue((start, e));
        }

        while (queue.Count > 0)
        {
            var (node, edge) = queue.Dequeue();
            if (!visited.Add(edge))
                continue;

            result.Add(edge);

            var id = edge.A == node.Id ? edge.B : edge.A;
            Node next = Nodes.First(n => n.Id == id);

            var nextEdge = GetNextEdge(Turnouts, next, edge);
            if (nextEdge != null)
                queue.Enqueue((next, nextEdge));
        }

        return result;
    }


    Edge? GetNextEdge(List<Turnout> turnouts, Node currentNode, Edge arrivedVia)
    {
        var edgesAtNode = GetEdgesAt(currentNode);

        // Dead end
        if (edgesAtNode.Count <= 1)
            return null;

        var turnout = turnouts.FirstOrDefault(t => t.Node.Id == currentNode.Id);

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

    public void Load()
    {
        Turnouts = GetTurnouts().Select(node =>
        {
            var edgesAtNode = GetEdgesAt(node); // e.g. [e1, e2, e3]

            var turnout = new Turnout
            {
                Node = node,
                Routes = new List<(Edge, Edge)>
                {
                    (edgesAtNode[0], edgesAtNode[1]),
                    (edgesAtNode[0], edgesAtNode[2]),
                },
                ActiveRoute = 0
            };

            return turnout;
        }).ToList();
    }
}

public struct Node
{
    public Guid Id { get; set; }

    // Arduino pin
    public int? TurnoutId { get; set; }
    public Vector2Int Point { get; set; }
    public SpeedLimit Speed { get; set; }
}

public class Edge
{
    public Guid A { get; set; }
    public Guid B { get; set; }
}

public class Turnout
{
    public int Id => Node.TurnoutId!.Value;

    public Node Node { get; set; }
    public List<(Edge A, Edge B)> Routes { get; set; }
    public int ActiveRoute { get; set; } // index into Routes — this is the "state" piece from before
}