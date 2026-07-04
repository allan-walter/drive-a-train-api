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

    public void Process(Mat frame)
    {
        var markers = try4.GetMarkerSeeds(frame.Clone());
        var combinedMask = Helpers.CombineMasks(markers.Select(m => m.RawMask).ToList());
        var dirMarkers = try4.IdentifyDirectionMarkers(frame.Clone(), markers, combinedMask);
        var units = try4.GetRects(frame.Clone(), markers, dirMarkers);
        var train = units.FirstOrDefault(u => u.Marker.Unit?.Type == UnitType.Locomotive);

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

        unitHub.Clients.All.SendAsync("units", new LiveData
        {
            Units = railUnits,
            Forward = dccService.ForwardLimit,
            ForwardValue = throttleLimits.Forward,
            Reverse = dccService.ReverseLimit,
            ReverseValue = throttleLimits.Reverse,
        });

        unitHub.Clients.All.SendAsync("connections", GetConnections(railUnits));
    }

    private Vector2Int GetMidpoint(Vector2Int p1, Vector2Int p2)
    {
        return new Vector2Int(
            (p1.X + p2.X) / 2,
            (p1.Y + p2.Y) / 2
        );
    }

    private bool ConnectionExists(List<RailConnection> connections, int addrOne, int couplerOne, int addrTwo,
        int couplerTwo)
    {
        return connections.Any(c =>
            (c.AddressOne == addrOne && c.CouplerOne == couplerOne &&
             c.AddressTwo == addrTwo && c.CouplerTwo == couplerTwo)
            ||
            (c.AddressOne == addrTwo && c.CouplerOne == couplerTwo &&
             c.AddressTwo == addrOne && c.CouplerTwo == couplerOne)
        );
    }

    // Find units that's front / back is close to each other (assume coupled) so they can be uncoupled
    public List<RailConnection> GetConnections(List<RailUnitGet> railUnits)
    {
        var connections = new List<RailConnection>();
        int maxDist = 100;
        foreach (var unit in railUnits)
        {
            RailUnitGet? frontFrontMatch = null;
            RailUnitGet? frontBackMatch = null;
            RailUnitGet? backFrontMatch = null;
            RailUnitGet? backBackMatch = null;

            double minFF = double.MaxValue, minFB = double.MaxValue;
            double minBF = double.MaxValue, minBB = double.MaxValue;

// Cache target positions
            var targetFrontPos = unit.Front.Position;
            var targetBackPos = unit.Back.Position;

            foreach (var u in railUnits)
            {
                if (u == unit) continue;

                var uFrontPos = u.Front.Position;
                var uBackPos = u.Back.Position;

                // ---- TARGET FRONT CONNECTIONS ----
                // Front to Front
                double distFF = uFrontPos.DistanceTo(targetFrontPos);
                if (distFF <= maxDist && distFF < minFF)
                {
                    minFF = distFF;
                    frontFrontMatch = u;
                }

                // Front to Back
                double distFB = uBackPos.DistanceTo(targetFrontPos);
                if (distFB <= maxDist && distFB < minFB)
                {
                    minFB = distFB;
                    frontBackMatch = u;
                }

                // ---- TARGET BACK CONNECTIONS ----
                // Back to Front
                double distBF = uFrontPos.DistanceTo(targetBackPos);
                if (distBF <= maxDist && distBF < minBF)
                {
                    minBF = distBF;
                    backFrontMatch = u;
                }

                // Back to Back
                double distBB = uBackPos.DistanceTo(targetBackPos);
                if (distBB <= maxDist && distBB < minBB)
                {
                    minBB = distBB;
                    backBackMatch = u;
                }
            }

// ---- ADD CONNECTIONS TO LIST ----

            if (frontFrontMatch != null &&
                !ConnectionExists(connections, unit.Def.Address, unit.Def.FrontCouplerIndex,
                    frontFrontMatch.Def.Address, frontFrontMatch.Def.FrontCouplerIndex))
            {
                connections.Add(new RailConnection
                {
                    AddressOne = unit.Def.Address,
                    CouplerOne = unit.Def.FrontCouplerIndex,
                    AddressTwo = frontFrontMatch.Def.Address,
                    CouplerTwo = frontFrontMatch.Def.FrontCouplerIndex,
                    Midpoint = GetMidpoint(targetFrontPos, frontFrontMatch.Front.Position)
                });
            }

            if (frontBackMatch != null &&
                !ConnectionExists(connections, unit.Def.Address, unit.Def.FrontCouplerIndex,
                    frontBackMatch.Def.Address, frontBackMatch.Def.BackCouplerIndex))
            {
                connections.Add(new RailConnection
                {
                    AddressOne = unit.Def.Address,
                    CouplerOne = unit.Def.FrontCouplerIndex,
                    AddressTwo = frontBackMatch.Def.Address,
                    CouplerTwo = frontBackMatch.Def.BackCouplerIndex,
                    Midpoint = GetMidpoint(targetFrontPos, frontBackMatch.Back.Position)
                });
            }

            if (backFrontMatch != null &&
                !ConnectionExists(connections, unit.Def.Address, unit.Def.BackCouplerIndex,
                    backFrontMatch.Def.Address, backFrontMatch.Def.FrontCouplerIndex))
            {
                connections.Add(new RailConnection
                {
                    AddressOne = unit.Def.Address,
                    CouplerOne = unit.Def.BackCouplerIndex,
                    AddressTwo = backFrontMatch.Def.Address,
                    CouplerTwo = backFrontMatch.Def.FrontCouplerIndex,
                    Midpoint = GetMidpoint(targetBackPos, backFrontMatch.Front.Position)
                });
            }

            if (backBackMatch != null &&
                !ConnectionExists(connections, unit.Def.Address, unit.Def.BackCouplerIndex,
                    backBackMatch.Def.Address, backBackMatch.Def.BackCouplerIndex))
            {
                connections.Add(new RailConnection
                {
                    AddressOne = unit.Def.Address,
                    CouplerOne = unit.Def.BackCouplerIndex,
                    AddressTwo = backBackMatch.Def.Address,
                    CouplerTwo = backBackMatch.Def.BackCouplerIndex,
                    Midpoint = GetMidpoint(targetBackPos, backBackMatch.Back.Position)
                });
            }
        }

        return connections;
    }
}

public class RailConnection
{
    public int AddressOne { get; set; }
    public int AddressTwo { get; set; }

    // May not be 2 couplers, will be -1 if so
    public int CouplerOne { get; set; }
    public int CouplerTwo { get; set; }

    public Vector2Int Midpoint { get; set; }
}