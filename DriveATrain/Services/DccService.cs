using Microsoft.Extensions.Options;

namespace DriveATrain.Services;

using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

public class DccService
{
    private const int CMD_TIME = 500; // adjust to match your original CMD_TIME constant


    private TaskCompletionSource<bool> connectionReady = new TaskCompletionSource<bool>();

    public SpeedLimit ForwardLimit { get; set; } = SpeedLimit.NORMAL;
    public SpeedLimit ReverseLimit { get; set; } = SpeedLimit.NORMAL;
    public Throttle Throttle { get; set; } = new Throttle(0, 0, false);

    public SerialPort Port { get; private set; }

    private DccConfig config;

    public DccService(Config config)
    {
        this.config = config.Dcc;

        Connect();
    }

    public void SetLimits(SpeedLimit forwardSpeed, SpeedLimit backwardSpeed)
    {
        bool change = ForwardLimit != forwardSpeed || ReverseLimit != backwardSpeed;
        ForwardLimit = forwardSpeed;
        ReverseLimit = backwardSpeed;

        // if (change) {
        _ = Task.Run(async () => await SetThrottleAsync());
        // Send to the frontend so it shows the user it changed
        // }
    }

    public void Connect()
    {
        // reset signal
        connectionReady = new TaskCompletionSource<bool>();

        Port = new SerialPort(config.Port, 115200); // change this

        try
        {
            Port.Open();

            // Wait for connect
            Thread.Sleep(2000);

            PowerOn();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
        {
            try
            {
                // Turn track power OFF safely
                PowerOff();
                Thread.Sleep(CMD_TIME); // give it time to send
                Port.Close();
            }
            catch (Exception)
            {
            }
        };
    }

    public void PowerOn()
    {
        if (!Port.IsOpen)
        {
            Connect();
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes("<1>\n"); // track power ON
        Port.Write(bytes, 0, bytes.Length);
    }

    public void PowerOff()
    {
        if (!Port.IsOpen)
        {
            Connect();
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes("<0>\n");
        Port.Write(bytes, 0, bytes.Length);
    }

    // Run the function for a short time then turn off automatically. Used for couplers
    public void RunCoupleFunction(int address, int function)
    {
        if (!Port.IsOpen)
        {
            Connect();
        }

        // Function mode, address 3, function 0, 1 = on
        var onBytes = System.Text.Encoding.UTF8.GetBytes($"<F {address} {function} 1>\n");
        Port.Write(onBytes, 0, onBytes.Length);

        _ = Task.Run(async () =>
        {
            // Wait for user to drive away
            await Task.Delay(TimeSpan.FromMilliseconds(2000));
            var offBytes = System.Text.Encoding.UTF8.GetBytes($"<F {address} {function} 0>\n");
            Port.Write(offBytes, 0, offBytes.Length);
        });
    }

    public LimitValues GetThrottleLimits()
    {
        var res = new LimitValues();

        if (ForwardLimit == SpeedLimit.SLOW)
            res.Forward = config.SlowThrottleValue;
        else if (ForwardLimit == SpeedLimit.STOP)
            res.Forward = 0.0;

        if (ReverseLimit == SpeedLimit.SLOW)
            res.Reverse = config.SlowThrottleValue;
        else if (ReverseLimit == SpeedLimit.STOP)
            res.Reverse = 0.0;

        return res;
    }

    // Set a new throttle and check limits, or use null to just recheck limits
    public async Task SetThrottleAsync(Throttle throttle = null)
    {
        if (throttle != null)
            Throttle = throttle;

        double throttleValue = Throttle.Value;
        bool reverse = Throttle.Reverse;
        var limits = GetThrottleLimits();

        if (!reverse)
        {
            throttleValue = Math.Min(throttleValue, limits.Forward);
        }
        else
        {
            throttleValue = Math.Min(throttleValue, limits.Reverse);
        }

        if (!Port.IsOpen)
        {
            Connect();
        }

        // For dev
        if (throttle != null && throttle.OverrideValue > 0)
            throttleValue = throttle.OverrideValue;

        var data = $"<t {config.LocoAddress} {(int)(throttleValue * 100)} {(reverse ? 0 : 1)}>\n";
        var bytes = System.Text.Encoding.UTF8.GetBytes(data); // loco 3, speed 50, forward
        Port.Write(bytes, 0, bytes.Length);

        await Task.CompletedTask;
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
    public double OverrideValue { get; set; }
    public bool Reverse { get; set; }

    public Throttle(double value, double overrideValue, bool reverse)
    {
        Value = value;
        OverrideValue = overrideValue;
        Reverse = reverse;
    }
}

public class LimitValues
{
    public double Forward { get; set; } = double.MaxValue;
    public double Reverse { get; set; } = double.MaxValue;
}