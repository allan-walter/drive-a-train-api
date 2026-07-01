namespace DriveATrain;

public class DccConfig
{
    public string Port { get; set; }
    public int LocoAddress { get; set; }
    public double SlowThrottleValue { get; set; }
}

public class TurnoutConfig
{
    public string Port { get; set; }
}

public class CameraConfig
{
    public int Index { get; set; }
    public bool Flip { get; set; }
}

public class VisionConfig
{
    public int SlowWhenPixelsLessThan { get; set; }
    public int StopWhenPixelsLessThan { get; set; }
}

public class AppConfig
{
    public bool FirstRun { get; set; }
    public bool Headless { get; set; }
}