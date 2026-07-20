using DriveATrain.Services;

namespace DriveATrain.Hubs;

using Microsoft.AspNetCore.SignalR;

public class InfoHub(Config config) : Hub
{
    public override Task OnConnectedAsync()
    {
        object data = new
        {
            width = DetectorService.CAMERA_WIDTH,
            height = DetectorService.CAMERA_HEIGHT,
            maxThrottle = config.Dcc.MaxSpeed,
            throttleStep = config.Dcc.ThrottleStep,
        };

        Clients.All.SendAsync("info", data);

        return base.OnConnectedAsync();
    }
}