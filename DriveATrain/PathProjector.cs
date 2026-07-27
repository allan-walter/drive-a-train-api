namespace DriveATrain;

public class PathProjector
{
    public struct ProjectionResult
    {
        public LayoutPoint Point; // the projected point (rounded to int grid)
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

    public ProjectionResult Project(LayoutPoint p)
    {
        ProjectionResult best = new ProjectionResult { DistanceSq = double.MaxValue };
        bool found = false;

        for (int pathIdx = 0; pathIdx < layout.Paths.Count; pathIdx++)
        {
            var path = layout.Paths[pathIdx];
            if (path == null || path.Count < 2)
                continue; // skip degenerate paths instead of throwing

            for (int i = 0; i < path.Count - 1; i++)
            {
                LayoutPoint a = path[i];
                LayoutPoint b = path[i + 1];

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
                        Point = new LayoutPoint(roundedX, roundedY),
                        PathIndex = pathIdx,
                        SegmentIndex = i,
                        T = t,
                        DistanceSq = distSq
                    };
                    found = true;
                }
            }
        }

        if (!found)
            throw new InvalidOperationException("No valid paths with at least 2 points were provided.");

        return best;
    }
}