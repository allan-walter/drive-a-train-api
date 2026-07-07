using System.Diagnostics;
using DriveATrain.Hubs;
using DriveATrain.OpenCv;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using OpenCvSharp;

namespace DriveATrain.Services;

public class DetectorService(
    Try4 try4,
    LimiterService limiter,
    DccService dccService,
    IHubContext<UnitHub> unitHub,
    Config config)
{
    // public static double CAMERA_WIDTH = 640.0;
    // public static double CAMERA_HEIGHT = 360.0;
    public static double CAMERA_WIDTH = 1920.0;
    public static double CAMERA_HEIGHT = 1080.0;

    public static int STREAM_WIDTH = 1920;
    public static int STREAM_HEIGHT = 1080;
    public static int STREAM_FPS = 30;

    public Mat currentDebugFrame = new Mat();

    private LiveData? _pendingLiveData;
    private List<Uncouple>? _pendingConnections;
    private int _publishScheduled;

    public void Process(Mat frame)
    {
        return;
        // DebugWindow.Show("test frame", frame.Clone());
        using var processingFrame = frame.Clone();
        using var debugFrame = frame.Clone();
        var markers = try4.GetMarkerSeeds(processingFrame, debugFrame);

        try
        {
            using var combinedMask = Helpers.CombineMasks(markers.Select(m => m.Mask).ToList());

            var dirMarkers = try4.IdentifyDirectionMarkers(frame, debugFrame, markers, combinedMask);
            var units = try4.GetRects(frame, debugFrame, markers, dirMarkers);

            debugFrame.CopyTo(currentDebugFrame);

            var train = units.FirstOrDefault(u => u.Marker.Unit?.Type == UnitType.Locomotive);

            // DebugWindow.Show("debug frame", debugFrame);
            if (train != null)
            {
                var limits = limiter.ProcessLimits(frame, train.Front, train.Back);
                dccService.SetLimits(limits.Forward, limits.Reverse);
            }
            else
            {
                // _dccService.SetLimits(SpeedLimit.Stop, SpeedLimit.Stop);
                dccService.SetLimits(SpeedLimit.NORMAL, SpeedLimit.NORMAL);
            }

            var throttleLimits = dccService.GetThrottleLimits();
            var railUnits = units.Select(u => new RailUnitGet(u)).ToList();

            // var railUnits = new List<RailUnitGet>();
            // railUnits = RailUnitMocks.GetMocks(config.Units.First(u => u.Type == UnitType.Locomotive),
            //     config.Units.First(u => u.Type == UnitType.Wagon));

            SchedulePublish(
                new LiveData
                {
                    Units = railUnits,
                    Forward = dccService.ForwardLimit,
                    ForwardValue = throttleLimits.Forward,
                    Reverse = dccService.ReverseLimit,
                    ReverseValue = throttleLimits.Reverse,
                },
                GetConnections(railUnits));
        }
        finally
        {
            foreach (var marker in markers)
                marker.Mask.Dispose();
        }
    }

    private void SchedulePublish(LiveData liveData, List<Uncouple> connections)
    {
        _pendingLiveData = liveData;
        _pendingConnections = connections;
        if (Interlocked.CompareExchange(ref _publishScheduled, 1, 0) != 0)
            return;

        _ = PublishLoopAsync();
    }

    private async Task PublishLoopAsync()
    {
        try
        {
            while (true)
            {
                var liveData = _pendingLiveData;
                var connections = _pendingConnections;
                if (liveData == null || connections == null)
                    break;

                _pendingLiveData = null;
                _pendingConnections = null;

                await unitHub.Clients.All.SendAsync("units", liveData);
                await unitHub.Clients.All.SendAsync("connections", connections);

                if (_pendingLiveData == null)
                    break;
            }
        }
        finally
        {
            Interlocked.Exchange(ref _publishScheduled, 0);
            if (_pendingLiveData != null)
                SchedulePublish(_pendingLiveData, _pendingConnections!);
        }
    }

    private Vector2Int GetMidpoint(Vector2Int p1, Vector2Int p2)
    {
        return new Vector2Int(
            (p1.X + p2.X) / 2,
            (p1.Y + p2.Y) / 2
        );
    }

    // Find units that's front / back is close to each other (assume coupled) so they can be uncoupled
    public List<Uncouple> GetConnections(List<RailUnitGet> railUnits)
    {
        var connections = new List<Uncouple>();
        const int maxDist = 100;

        // Flatten every unit's two couplers into one list of (unit, index, position).
        var couplers = railUnits
            .SelectMany(u => new[]
            {
                new { Unit = u, Index = u.Def.FrontCouplerIndex, Position = u.Front.Position },
                new { Unit = u, Index = u.Def.BackCouplerIndex, Position = u.Back.Position }
            })
            .ToList();

        foreach (var coupler in couplers)
        {
            double bestDist = double.MaxValue;
            RailUnitGet? bestUnit = null;
            int bestIndex = -1;
            object? bestPos = null;

            foreach (var other in couplers)
            {
                if (other.Unit == coupler.Unit) continue; // skip same unit's own couplers

                double dist = other.Position.DistanceTo(coupler.Position);
                if (dist <= maxDist && dist < bestDist)
                {
                    bestDist = dist;
                    bestUnit = other.Unit;
                    bestIndex = other.Index;
                    bestPos = other.Position;
                }
            }

            if (bestUnit != null && !connections.Any(c =>
                    c.Address == coupler.Unit.Def.Address || c.Address == bestUnit.Def.Address))
            {
                connections.Add(new Uncouple
                {
                    Address = coupler.Unit.Def.Address,
                    Coupler = coupler.Index,
                    Position = GetMidpoint(coupler.Position, (dynamic)bestPos!)
                });
            }
        }

        return connections;
    }
}

public class Uncouple
{
    public int Address { get; set; }

    public int Coupler { get; set; }

    public Vector2Int Position { get; set; }
}