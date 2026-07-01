using DriveATrain.Services;

namespace DriveATrain.Hubs;

using Microsoft.AspNetCore.SignalR;

public class InfoHub : Hub
{
    public override Task OnConnectedAsync()
    {
        object data = new
        {
            width = DetectorService.CAMERA_WIDTH,
            height = DetectorService.CAMERA_HEIGHT,
        };

        Clients.All.SendAsync("info", data);

        return base.OnConnectedAsync();
    }
}