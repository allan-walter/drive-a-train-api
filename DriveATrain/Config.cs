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
    public bool Flip { get; set; }
    public string BirdsEyeCameraName { get; set; }
    public string PovCameraUrl { get; set; }
}

public class VisionConfig
{
    public string Camera { get; set; }
    public int SlowWhenPixelsLessThan { get; set; }
    public int StopWhenPixelsLessThan { get; set; }

    public Mat goZone = Cv2.ImRead(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "DriveATrain",
        "Static Images/go zone.png"), ImreadModes.Grayscale);

    public VisionConfig()
    {
        
        Cv2.Resize(goZone, goZone, new Size(CaptureService.DETECTION_WIDTH, CaptureService.DETECTION_HEIGHT));
    }
}