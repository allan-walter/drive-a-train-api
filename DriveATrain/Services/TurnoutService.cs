using DriveATrain.Hubs;
using DriveATrain.Services.Layout;

namespace DriveATrain.Services;

public class TurnoutService : IHostedService
{
    private Config config;
    private LayoutService _layoutService;
    private readonly MqttService _mqtt;

    public TurnoutService(Config config, LayoutService layoutService, MqttService mqtt)
    {
        _layoutService = layoutService;
        _mqtt = mqtt;
        this.config = config;
    }

    private Task<bool> SendCommand(string command) =>
        _mqtt.PublishAsync(config.Turnout.CommandTopic, command);

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

        await SendCommand($"{turnout.Pin}{(state ? "f" : "c")}");
    }

    // TODO there is an issue where if the center of the train is right over the turnout we dont't know which active path its actually on. 
    // Needs to be the 2 paths joined by the turnout which should be what the train is on even if its marginally futher away than the other point
    // Remember the train could actuaally be on the other path though just in a stop state so don't be too harsh with it

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
