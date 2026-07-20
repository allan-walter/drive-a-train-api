using DriveATrain.Services;
using Microsoft.AspNetCore.SignalR;

namespace DriveATrain.Hubs;

public class UnitHub(DccService dccService) : Hub
{
    public override Task OnConnectedAsync()
    {
        return base.OnConnectedAsync();
    }

    public async Task PowerOn()
    {
        await dccService.PowerOn();
    }

    public async Task PowerOff()
    {
        await dccService.PowerOff();
    }

    public void Uncouple(Uncouple uncouple)
    {
        dccService.RunCoupleFunction(uncouple);
    }
}

public class Uncouple
{
    public int Address { get; set; }
    public int Function { get; set; }
}