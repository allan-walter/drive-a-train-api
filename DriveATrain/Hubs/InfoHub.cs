using DriveATrain.Services;

namespace DriveATrain.Hubs;

using Microsoft.AspNetCore.SignalR;

public class InfoHub(Config config) : Hub
{
    public override Task OnConnectedAsync()
    {
        object data = new
        {
            width = CaptureService.CAMERA_WIDTH,
            height = CaptureService.CAMERA_HEIGHT,
            detectionWidth = CaptureService.DETECTION_WIDTH,
            detectionHeight = CaptureService.DETECTION_HEIGHT,
            maxThrottle = config.Dcc.MaxSpeed,
            throttleStep = config.Dcc.ThrottleStep,
            turnoutLocations = config.Turnout.Locations
        };

        Clients.All.SendAsync("info", data);

        return base.OnConnectedAsync();
    }
}