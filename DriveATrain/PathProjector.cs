namespace DriveATrain;

public class PathProjector
{
    public struct ProjectionResult
    {
        public Node Point; // the projected point (rounded to int grid)
        public int PathIndex; // which path in the list
        public int SegmentIndex; // index i, meaning segment (path[i], path[i+1])
        public double T; // 0..1 position along that segment
        public double DistanceSq; // squared distance from original point (using rounded point)
    }

    private readonly Layout layout;

    public PathProjector(Layout layout)
    {
        this.layout = layout;
    }

    public ProjectionResult Project(Node p)
    {
        ProjectionResult best = new ProjectionResult { DistanceSq = double.MaxValue };
        bool found = false;

        for (int edgeIndex = 0; edgeIndex < layout.Edges.Count; edgeIndex++)
        {
            var edge = layout.Edges[edgeIndex];

            var a = edge.A;
            var b = edge.B;
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

            if (distSq < best.DistanceSq)
            {
                best = new ProjectionResult
                {
                    Point = new Node(roundedX, roundedY),
                    PathIndex = edgeIndex,
                    T = t,
                    DistanceSq = distSq
                };
                found = true;
            }
        }

        if (!found)
            throw new InvalidOperationException("No valid paths with at least 2 points were provided.");

        return best;
    }
}