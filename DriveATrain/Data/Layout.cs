using DriveATrain.OpenCv;
using DriveATrain.Services;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DriveATrain.Data;

public class Layout
{
    public List<Node> Nodes { get; set; }
    public List<Edge> Edges { get; set; }

    public List<Turnout> Turnouts { get; set; }
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
    // Arduino pin
    public int Id { get; set; }

    public Guid NodeId { get; set; }
}

public class TurnoutState
{
    public Turnout Turnout { get; set; }
    public List<(Edge A, Edge B)> Routes { get; set; }
    public int ActiveRoute { get; set; } // index into Routes — this is the "state" piece from before

    public TurnoutState(Turnout turnout, List<Edge> routes)
    {
        Turnout = turnout;
        
        if (routes.Count != 3)
            throw new ArgumentException("Currently only 3 point turnouts supported");

        Routes = new List<(Edge, Edge)>
        {
            (routes[0], routes[1]),
            (routes[0], routes[2]),
        };
    }
}

// Calculated from the active state spending on how turnouts are switched
public class TrackPath
{
    public List<Edge> Edges { get; set; } = new();
    // public List<Node> Nodes { get; set; } = new(); // Optional, but handy for traversal
}