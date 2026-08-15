using DriveATrain.Services;
using Microsoft.AspNetCore.SignalR;

namespace DriveATrain.Hubs;

public class UnitHub(DccService dccService, TurnoutService turnoutService) : Hub
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

    public async Task Turnout(Turnout turnout)
    {
        await turnoutService.Run(turnout);
    }

    public async Task DebugTurnout(DebugTurnout debugTurnout)
    {
        // throw new FormatException("Does this get cauise");
        // Some eceptions actualy break rider, others happen quietly, either way poweroff if it happens since the video feed might be errored and the train needs to stop
        // TODO don't actually know what is best to do here. if the limit stuff still works itll sto0p the train, but if riders on a breakpoint nothing will stop it
        // Maybe its needed during dev but not production?
        throw new InvalidOperationException("definitly caugght");
        // await turnoutService.Debug(debugTurnout);
    }
}

public class DebugTurnout
{
    public int Pin { get; set; }
    public int Degree { get; set; }
}

public class Turnout
{
    public int Pin { get; set; }
    public bool State { get; set; }
}

public class Uncouple
{
    public int Address { get; set; }
    public int Function { get; set; }
}