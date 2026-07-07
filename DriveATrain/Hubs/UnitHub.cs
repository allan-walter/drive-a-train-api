using DriveATrain.Services;
using Microsoft.AspNetCore.SignalR;

namespace DriveATrain.Hubs;

public class UnitHub(DccService dccService) : Hub
{
    public override Task OnConnectedAsync()
    {
        return base.OnConnectedAsync();
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