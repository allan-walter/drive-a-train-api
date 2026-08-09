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
    public Node Node { get; set; }
    public List<(Edge A, Edge B)> Routes { get; set; }
    public int ActiveRoute { get; set; } // index into Routes — this is the "state" piece from before
}