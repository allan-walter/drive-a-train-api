using DriveATrain;
using DriveATrain.OpenCv;
using DriveATrain.Services;
using OpenCvSharp;

public class LimiterService
{
    private VisionConfig config;
    public Mat blocks;

    public LimiterService(Config config)
    {
        this.config = config.Vision;
        blocks = Cv2.ImRead(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "DriveATrain",
            "Static Images/blocks.png"), ImreadModes.Grayscale);
    }

    public Vector2Int GetNearestBlack(Transform to, Mat mask)
    {
        using var invertedBinary = new Mat();
        Cv2.BitwiseNot(mask, invertedBinary);

        using var blackPixels = new Mat();
        Cv2.FindNonZero(invertedBinary, blackPixels);

        // Find the closest one to the dot
        double minDist = double.MaxValue;
        int nearestX = -1;
        int nearestY = -1;

        for (int i = 0; i < blackPixels.Rows; i++)
        {
            var pt = blackPixels.At<Point>(i, 0);
            double px = pt.X;
            double py = pt.Y;
            double dx = px - to.Position.X;
            double dy = py - to.Position.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);

            if (dist < minDist)
            {
                minDist = dist;
                nearestX = (int)px;
                nearestY = (int)py;
            }
        }

        return new Vector2Int(nearestX, nearestY);
    }

    public SpeedResult ProcessLimits(Mat frame, Transform front, Transform back)
    {
        using var binary = new Mat();
        Cv2.Threshold(blocks, binary, 254.0, 255.0, ThresholdTypes.Binary);

        using var distMap = new Mat();
        Cv2.DistanceTransform(binary, distMap, DistanceTypes.L2, DistanceTransformMasks.Mask5);

        var limits = new SpeedResult();

        // If position is in (x, y) space:
        int row = (int)front.Position.Y; // row = y
        int col = (int)front.Position.X; // col = x

        if (row < 0 || row >= distMap.Rows || col < 0 || col >= distMap.Cols)
        {
            limits.Forward = SpeedLimit.STOP;
            limits.Reverse = SpeedLimit.STOP;
            return limits;
        }

        // The detected bits will be in the frame but the front or back could be
        // slightly outside the frame since it's an end of the rotated rect
        if (front.Position.Y < distMap.Rows && front.Position.X < distMap.Cols)
        {
            var closestBlack = GetNearestBlack(front, binary);
            var frontDist = closestBlack.DistanceTo(front.Position);

            // Stop will be less than slow. Once passed the dists will be inverted and
            // start increasing again so the HasPassed check is also needed
            if (frontDist < config.SlowWhenPixelsLessThan
                && front.Position.HasPassed(closestBlack, front.Direction))
            {
                limits.Forward = SpeedLimit.STOP;
            }
            else if (frontDist < config.StopWhenPixelsLessThan)
            {
                limits.Forward = SpeedLimit.STOP;
            }
            else if (frontDist < config.SlowWhenPixelsLessThan)
            {
                limits.Forward = SpeedLimit.SLOW;
            }
        }
        else
        {
            limits.Forward = SpeedLimit.STOP;
        }

        if (back.Position.Y < distMap.Rows && back.Position.X < distMap.Cols)
        {
            var closestBlack = GetNearestBlack(back, binary);
            var backDist = closestBlack.DistanceTo(back.Position);

            if (backDist < config.SlowWhenPixelsLessThan
                && back.Position.HasPassed(closestBlack, back.Direction))
            {
                limits.Reverse = SpeedLimit.STOP;
            }
            else if (backDist < config.StopWhenPixelsLessThan)
            {
                limits.Reverse = SpeedLimit.STOP;
            }
            else if (backDist < config.SlowWhenPixelsLessThan)
            {
                limits.Reverse = SpeedLimit.SLOW;
            }
        }
        else
        {
            limits.Reverse = SpeedLimit.STOP;
        }

        return limits;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}