using System.Diagnostics;
using DriveATrain.Hubs;
using Microsoft.Extensions.Options;

namespace DriveATrain.Services;

using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

public class DccService : IHostedService
{
    private const int CMD_TIME = 500; // adjust to match your original CMD_TIME constant


    private TaskCompletionSource<bool> connectionReady = new TaskCompletionSource<bool>();

    public SpeedLimit ForwardLimit { get; set; } = SpeedLimit.NORMAL;
    public SpeedLimit ReverseLimit { get; set; } = SpeedLimit.NORMAL;
    public Throttle Throttle { get; set; } = new Throttle(0, false, false);

    public SerialPort Port { get; private set; }

    private DccConfig config;
    IHostApplicationLifetime _lifetime;
    private BroadcastService _broadcastService;

    public DccService(Config config, IHostApplicationLifetime lifetime, BroadcastService broadcastService)
    {
        this.config = config.Dcc;
        _lifetime = lifetime;
        _broadcastService = broadcastService;
    }

    public void SetLimits(SpeedLimit forwardSpeed, SpeedLimit backwardSpeed)
    {
        if (ForwardLimit == forwardSpeed && ReverseLimit == backwardSpeed)
            return;

        ForwardLimit = forwardSpeed;
        ReverseLimit = backwardSpeed;

        _ = SetThrottleAsync();
    }

    private void Connect()
    {
        // reset signal
        connectionReady = new TaskCompletionSource<bool>();

        Port = new SerialPort(config.Port, 115200); // change this

        try
        {
            Port.Open();

            // Wait for connect
            System.Threading.Tasks.Task.Delay(2000);

            PowerOn();
        }
        catch (Exception e)
        {
            // Console.WriteLine(e);
        }
    }

    private void SendCommand(string command)
    {
        if (!Port.IsOpen)
            Connect();

        if (!Port.IsOpen)
            return;

        if (!command.EndsWith("\n"))
            command += "\n";


        var bytes = System.Text.Encoding.UTF8.GetBytes(command);
        Port.Write(bytes, 0, bytes.Length);
    }

    public void PowerOn()
    {
        SendCommand("<1>"); // track power ON
    }

    public void PowerOff()
    {
        SendCommand("<0>");
    }

    // Run the function for a short time then turn off automatically. Used for couplers
    public void RunCoupleFunction(Hubs.Uncouple uncouple)
    {
        if (!Port.IsOpen)
        {
            Connect();
        }

        // Function mode, address 3, function 0, 1 = on
        SendCommand($"<F {uncouple.Address} {uncouple.Function} 1>");

        _ = Task.Run(async () =>
        {
            // Wait for user to drive away (hopefully)
            await Task.Delay(TimeSpan.FromMilliseconds(2000));

            SendCommand($"<F {uncouple.Address} {uncouple.Function} 0>");
        });
    }

    public LimitValues GetThrottleLimits(bool @override)
    {
        var res = new LimitValues(config);

        // Dev override. Default max speed still applies
        if (!@override)
        {
            if (ForwardLimit == SpeedLimit.SLOW)
                res.Forward = config.SlowThrottleValue;
            else if (ForwardLimit == SpeedLimit.STOP)
                res.Forward = 0.0;

            if (ReverseLimit == SpeedLimit.SLOW)
                res.Reverse = config.SlowThrottleValue;
            else if (ReverseLimit == SpeedLimit.STOP)
                res.Reverse = 0.0;
        }

        return res;
    }

    // Set a new throttle and check limits, or use null to just recheck limits
    public async Task SetThrottleAsync(Throttle? throttle = null)
    {
        if (throttle != null)
            Throttle = throttle;

        double throttleValue = Throttle.Value;
        bool reverse = Throttle.Reverse;
        var limits = GetThrottleLimits(Throttle.Override);

        if (!reverse)
        {
            throttleValue = Math.Min(throttleValue, limits.Forward);
        }
        else
        {
            throttleValue = Math.Min(throttleValue, limits.Reverse);
        }

        throttleValue = Math.Min(throttleValue, config.MaxSpeed);

        Debug.WriteLine($"Throttle: {throttleValue}, Reverse: {reverse}");

        _broadcastService.engineAudio.SetSpeed(throttleValue);

        SendCommand($"<t {config.LocoAddress} {(int)(throttleValue * 100)} {(reverse ? 0 : 1)}>");

        await Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Connect();
        _lifetime.ApplicationStopping.Register(OnStopping);
        return Task.CompletedTask;
    }

    void OnStopping()
    {
        try
        {
            // Turn track power OFF safely
            PowerOff();
            Task.Delay(CMD_TIME); // give it time to send

            Port.Close();
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

public enum SpeedLimit
{
    NORMAL,
    SLOW,
    STOP
}

public class SpeedResult
{
    public SpeedLimit Forward { get; set; } = SpeedLimit.NORMAL;
    public SpeedLimit Reverse { get; set; } = SpeedLimit.NORMAL;
}

public class Throttle
{
    public double Value { get; set; }
    public bool Override { get; set; }
    public bool Reverse { get; set; }

    public Throttle(double value, bool @override, bool reverse)
    {
        Value = value;
        Override = @override;
        Reverse = reverse;
    }
}

public class LimitValues
{
    public double Forward { get; set; }
    public double Reverse { get; set; }

    public LimitValues(DccConfig config)
    {
        Forward = config.MaxSpeed;
        Reverse = config.MaxSpeed;
    }
}