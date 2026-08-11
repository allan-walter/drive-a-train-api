using System.IO.Ports;
using DriveATrain.Hubs;
using DriveATrain.Services.Layout;

namespace DriveATrain.Services;

public class TurnoutService : IHostedService
{
    public SerialPort Port;
    private Config config;
    private LayoutService _layoutService;

    public TurnoutService(Config config, LayoutService layoutService)
    {
        _layoutService = layoutService;
        Port = new SerialPort(config.Turnout.Port, 115200); // change this
        this.config = config;
    }

    private async Task<bool> SendCommand(string command)
    {
        if (!Port.IsOpen)
            await Connect();

        if (!Port.IsOpen)
            return false;

        if (!command.EndsWith("\n"))
            command += "\n";


        var bytes = System.Text.Encoding.UTF8.GetBytes(command);
        Port.Write(bytes, 0, bytes.Length);

        return true;
    }

    public async Task Debug(DebugTurnout debugTurnout)
    {
        await SendCommand($"{debugTurnout.Pin}:{debugTurnout.Degree}");
    }

    public async Task Run(Turnout turnout)
    {
        var state = turnout.State;

        if (config.Turnout.Locations.FirstOrDefault(l => l.Pin == turnout.Pin)?.Reverse ?? false)
            state = !state;

        var layoutTurnout = _layoutService.Turnouts.First(t => t.Turnout.Id == turnout.Pin);
        // TODO how to define which way around this is
        layoutTurnout.ActiveRoute = state ? 1 : 0;
        _layoutService.CalculatePathsByTurnout();

        await SendCommand($"{turnout.Pin}{(state ? "f" : "b")}");
    }

    // TODO there is an issue where if the center of the train is right over the turnout we dont't know which active path its actually on. 
    // Needs to be the 2 paths joined by the turnout which should be what the train is on even if its marginally futher away than the other point
    // Remember the train could actuaally be on the other path though just in a stop state so don't be too harsh with it
    private async Task Connect()
    {
        try
        {
            Port.Open();

            // Wait for connect
            await Task.Delay(2000);
        }
        catch (Exception e)
        {
            // Console.WriteLine(e);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _layoutService.CalculatePathsByTurnout();

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}