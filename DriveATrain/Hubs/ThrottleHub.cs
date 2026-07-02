using DriveATrain.Services;
using Microsoft.AspNetCore.SignalR;

namespace DriveATrain.Hubs;

public class ThrottleHub(DccService dccService) : Hub
{
    public async Task SetThrottle(Throttle throttle)
    {
        await dccService.SetThrottleAsync(throttle);
    }
}