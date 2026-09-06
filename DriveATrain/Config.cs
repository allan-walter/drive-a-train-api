using System.Text.Json;
using System.Text.Json.Serialization;
using DriveATrain.Data;
using DriveATrain.OpenCv;
using DriveATrain.Services;
using OpenCvSharp;

namespace DriveATrain;

public class Config
{
    public DccConfig Dcc { get; set; }
    public MqttConfig Mqtt { get; set; } = new();
    public TurnoutConfig Turnout { get; set; }
    public CameraConfig Camera { get; set; }
    public VisionConfig Vision { get; set; }
    public List<UnitDefinition> Units { get; set; }

    [JsonIgnore] public Layout Layout;

    public Config()
    {
        string filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DriveATrain",
            "Static Images/layout.json");
        string jsonString = File.ReadAllText(filePath);

        Layout = JsonSerializer.Deserialize<Layout>(jsonString, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        });
    }
}

public class DccConfig
{
    public string Port { get; set; }
    public double MaxSpeed { get; set; }
    public double SlowThrottleValue { get; set; }
    public double ThrottleStep { get; set; }
}

public class MqttConfig
{
    public string Host { get; set; } = "192.168.20.201";
    public int Port { get; set; } = 1883;
}

public class TurnoutConfig
{
    public string CommandTopic { get; set; } = "driveatrain/turnout/cmd";
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
    public bool Flip { get; set; }
    public string BirdsEyeCameraName { get; set; }
    public string PovCameraUrl { get; set; }

    /// <summary>
    /// framesize_t value pushed to the POV camera before streaming starts. The sketch forces
    /// FRAMESIZE_QVGA on every boot and never persists the setting, so it has to be re-applied
    /// each time. Values (OV2640, the sensor on the AI-Thinker board):
    /// <code>
    ///   13 = UXGA   1600x1200
    ///   12 = SXGA   1280x1024
    ///   11 = HD     1280x720
    ///   10 = XGA    1024x768
    ///    9 = SVGA   800x600
    ///    8 = VGA    640x480
    ///    7 = HVGA   480x320
    ///    6 = CIF    400x296
    ///    5 = QVGA   320x240   &lt;- the sketch's boot default
    ///    4 =        240x240
    ///    3 = HQVGA  240x176
    ///    2 = QCIF   176x144
    ///    1 = QQVGA  160x120
    ///    0 =         96x96
    /// </code>
    /// </summary>
    public int PovCameraFrameSize { get; set; } = 9;
}

public class VisionConfig
{
    public int SlowWhenPixelsLessThan { get; set; }
    public int StopWhenPixelsLessThan { get; set; }
    public int TurnoutClearance { get; set; }
 
    public Mat goZone = Cv2.ImRead(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "DriveATrain",
        "Static Images/go zone.png"), ImreadModes.Grayscale);

    public VisionConfig()
    {
        
        Cv2.Resize(goZone, goZone, new Size(CaptureService.DETECTION_WIDTH, CaptureService.DETECTION_HEIGHT));
    }
}