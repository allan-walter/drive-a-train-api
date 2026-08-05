using DriveATrain;
using DriveATrain.OpenCv;
using DriveATrain.Services;
using OpenCvSharp;

public class LimiterService
{
    private VisionConfig config;

    public LimiterService(Config config)
    {
        this.config = config.Vision;
    }

    public SpeedResult ProcessLimits(Mat frame, Vector2Int front, Vector2Int back, Mat debugFrame)
    {
        var limits = new SpeedResult();

        limits.Forward = SpeedLimit.STOP;
        limits.Reverse = SpeedLimit.STOP;

        // var point = pathProjector.Project(front.Position.ToLayoutPoint());
        //
        // Cv2.Circle(debugFrame, point.Point.ToPoint(), 10, new Scalar(255, 0, 0, 255));

        // using var binary = new Mat();
        // Cv2.Threshold(config.blocks, binary, 254.0, 255.0, ThresholdTypes.Binary);
        //
        // using var distMap = new Mat();
        // Cv2.DistanceTransform(binary, distMap, DistanceTypes.L2, DistanceTransformMasks.Mask5);
        //
        //
        // // If position is in (x, y) space:
        // int row = (int)front.Position.Y; // row = y
        // int col = (int)front.Position.X; // col = x
        //
        // if (row < 0 || row >= distMap.Rows || col < 0 || col >= distMap.Cols)
        // {
        //     limits.Forward = SpeedLimit.STOP;
        //     limits.Reverse = SpeedLimit.STOP;
        //     return limits;
        // }
        //
        // // The detected bits will be in the frame but the front or back could be
        // // slightly outside the frame since it's an end of the rotated rect
        // if (front.Position.Y < distMap.Rows && front.Position.X < distMap.Cols)
        // {
        //     var closestBlack = GetNearestBlack(front, binary);
        //     var frontDist = closestBlack.DistanceTo(front.Position);
        //
        //     // Stop will be less than slow. Once passed the dists will be inverted and
        //     // start increasing again so the HasPassed check is also needed
        //     if (frontDist < config.SlowWhenPixelsLessThan
        //         && front.Position.HasPassed(closestBlack, front.Direction))
        //     {
        //         limits.Forward = SpeedLimit.STOP;
        //
        //         // Red
        //         Cv2.Circle(debugFrame, closestBlack.ToPoint(), 4, new Scalar(0, 0, 255, 255), -1);
        //     }
        //     else if (frontDist < config.StopWhenPixelsLessThan)
        //     {
        //         limits.Forward = SpeedLimit.STOP;
        //         // Red
        //         Cv2.Circle(debugFrame, closestBlack.ToPoint(), 4, new Scalar(0, 0, 255, 255), -1);
        //     }
        //     else if (frontDist < config.SlowWhenPixelsLessThan)
        //     {
        //         limits.Forward = SpeedLimit.SLOW;
        //         // Orange
        //         Cv2.Circle(debugFrame, closestBlack.ToPoint(), 4, new Scalar(0, 165, 255, 255), -1);
        //     }
        // }
        // else
        // {
        //     limits.Forward = SpeedLimit.STOP;
        //     // Cv2.Circle(debugFrame, closestBlack.ToPoint(), 4, new Scalar(0, 165, 255, 255), -1);
        // }
        //
        // if (back.Position.Y < distMap.Rows && back.Position.X < distMap.Cols)
        // {
        //     var closestBlack = GetNearestBlack(back, binary);
        //     var backDist = closestBlack.DistanceTo(back.Position);
        //
        //     if (backDist < config.SlowWhenPixelsLessThan
        //         && back.Position.HasPassed(closestBlack, back.Direction))
        //     {
        //         limits.Reverse = SpeedLimit.STOP;
        //     }
        //     else if (backDist < config.StopWhenPixelsLessThan)
        //     {
        //         limits.Reverse = SpeedLimit.STOP;
        //     }
        //     else if (backDist < config.SlowWhenPixelsLessThan)
        //     {
        //         limits.Reverse = SpeedLimit.SLOW;
        //     }
        // }
        // else
        // {
        //     limits.Reverse = SpeedLimit.STOP;
        // }

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