using DriveATrain.OpenCv;
using DriveATrain.Services.Layout;
using OpenCvSharp;

namespace DriveATrain.Services;

public class LimiterService
{
    private Config config;
    private LayoutService _layoutService;

    public LimiterService(Config config, LayoutService layoutService)
    {
        _layoutService = layoutService;
        this.config = config;
    }

    private SpeedLimit ProcessLimit(Vector2Int pos, Mat debugFrame)
    {
        return SpeedLimit.NORMAL;
    }

    private (MoveProjectionResult frontCollision, MoveProjectionResult backCollision)? Projection(Vector2Int front,
        Vector2Int back, int dist)
    {
        var frontProjection = _layoutService.ProjectOnPath(front);
        var backProjection = _layoutService.ProjectOnPath(back);
        var direction = _layoutService.GetTravelDirection(frontProjection, backProjection);

        if (!direction.HasValue)
            return null;

        var collisionFrontProjection = _layoutService.MoveAlongPath(frontProjection.Point, frontProjection.Path,
            frontProjection.Edge, dist, direction.Value);

        // Frlip because its backjwards
        var collisionBackProjection = _layoutService.MoveAlongPath(backProjection.Point, backProjection.Path,
            backProjection.Edge, dist, direction == SeekDirection.Down ? SeekDirection.Up : SeekDirection.Down);

        return (collisionFrontProjection, collisionBackProjection);
    }

    public SpeedResult ProcessLimits(Mat frame, Vector2Int front, Vector2Int back, Mat debugFrame)
    {
        var limits = new SpeedResult();

        limits.Forward = SpeedLimit.NORMAL;
        limits.Reverse = SpeedLimit.NORMAL;

        var stopProjection = Projection(front, back, config.Vision.StopWhenPixelsLessThan);
        var slowProjection = Projection(front, back, config.Vision.SlowWhenPixelsLessThan);

        // Split path, something bad has gone wrong
        if (stopProjection != null && slowProjection != null)
        {
            if (stopProjection.Value.frontCollision.ReachedEnd)
            {
                limits.Forward = SpeedLimit.STOP;
                Cv2.Circle(debugFrame, stopProjection.Value.frontCollision.Point.ToPoint(), 3, Colors.Red, -1);
            }
            else if (slowProjection.Value.frontCollision.ReachedEnd)
            {
                limits.Forward = SpeedLimit.SLOW;
                Cv2.Circle(debugFrame, slowProjection.Value.frontCollision.Point.ToPoint(), 3, Colors.Orange, -1);
            }

            if (stopProjection.Value.backCollision.ReachedEnd)
            {
                limits.Reverse = SpeedLimit.STOP;
                Cv2.Circle(debugFrame, stopProjection.Value.backCollision.Point.ToPoint(), 3, Colors.Red, -1);
            }
            else if (slowProjection.Value.backCollision.ReachedEnd)
            {
                limits.Reverse = SpeedLimit.SLOW;
                Cv2.Circle(debugFrame, slowProjection.Value.backCollision.Point.ToPoint(), 3, Colors.Orange, -1);
            }
        }
        else
        {
            limits.Forward = SpeedLimit.STOP;
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