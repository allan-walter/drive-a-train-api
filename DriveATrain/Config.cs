namespace DriveATrain;

public class Config
{
    public DccConfig Dcc { get; set; }
    public TurnoutConfig Turnout { get; set; }
    public CameraConfig Camera { get; set; }
    public VisionConfig Vision { get; set; }
    public List<UnitDefinition> Units { get; set; }
}

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
    public string Camera { get; set; }
    public int SlowWhenPixelsLessThan { get; set; }
    public int StopWhenPixelsLessThan { get; set; }
}

public class AppConfig
{
    public bool FirstRun { get; set; }
    public bool Headless { get; set; }
}