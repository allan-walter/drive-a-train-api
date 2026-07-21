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
    public double MaxSpeed { get; set; }
    public int LocoAddress { get; set; }
    public double SlowThrottleValue { get; set; }
    public double ThrottleStep { get; set; }
}

public class TurnoutConfig
{
    public string Port { get; set; }
    public List<TurnoutLocation> Locations { get; set; }
}

public class TurnoutLocation
{
    public int Pin { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Rotation { get; set; }
    public bool Reverse { get; set; }
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