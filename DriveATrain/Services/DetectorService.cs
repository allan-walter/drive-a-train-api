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
    public static double CAMERA_WIDTH = 640.0;
    public static double CAMERA_HEIGHT = 360.0;
    public static int STREAM_WIDTH = 640;

    public static int STREAM_HEIGHT = 360;

    // public static int STREAM_WIDTH = 1920;
    // public static int STREAM_HEIGHT = 1080;
    public static int STREAM_FPS = 30;

    public List<UnitMarkerResponse> Process(Mat frame)
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

        railUnits = RailUnitMocks.GetMocks(config.Units.First(u => u.Type == UnitType.Locomotive),
            config.Units.First(u => u.Type == UnitType.Wagon));

        unitHub.Clients.All.SendAsync("units", new LiveData
        {
            Units = railUnits,
            Forward = dccService.ForwardLimit,
            ForwardValue = throttleLimits.Forward,
            Reverse = dccService.ReverseLimit,
            ReverseValue = throttleLimits.Reverse,
        });

        // return units;
        return new List<UnitMarkerResponse>();
    }
}