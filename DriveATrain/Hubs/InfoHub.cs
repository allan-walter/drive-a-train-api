using DriveATrain.Services;

namespace DriveATrain.Hubs;

using Microsoft.AspNetCore.SignalR;

public class InfoHub : Hub
{
    public override Task OnConnectedAsync()
    {
        object data = new
        {
            width = CameraService.CAMERA_WIDTH,
            height = CameraService.CAMERA_HEIGHT,
        };

        Clients.All.SendAsync("info", data);

        return base.OnConnectedAsync();
    }
}