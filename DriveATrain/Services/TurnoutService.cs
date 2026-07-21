using System.IO.Ports;
using DriveATrain.Hubs;

namespace DriveATrain.Services;

public class TurnoutService
{
    public SerialPort Port;
    private TurnoutConfig _config;

    public TurnoutService(Config config)
    {
        Port = new SerialPort(config.Turnout.Port, 115200); // change this
        _config = config.Turnout;
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

        if (_config.Locations.FirstOrDefault(l => l.Pin == turnout.Pin)?.Reverse ?? false)
            state = !state;
        
        await SendCommand($"{turnout.Pin}{(state ? "f" : "b")}");
    }

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
}