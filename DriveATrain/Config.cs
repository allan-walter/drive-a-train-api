using System.Text.Json;
using System.Text.Json.Serialization;
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


    public Mat goZone = Cv2.ImRead(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "DriveATrain",
        "Static Images/go zone.png"), ImreadModes.Grayscale);

    [JsonIgnore] public Layout Layout;

    public VisionConfig()
    {
        Cv2.Resize(goZone, goZone, new Size(CaptureService.DETECTION_WIDTH, CaptureService.DETECTION_HEIGHT));

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

public class Layout
{
    public List<Node> Nodes { get; set; }
    public List<Edge> Edges { get; set; }
}

public class Edge
{
    public Guid A { get; set; }
    public Guid B { get; set; }
}

public class LayoutPoint
{
    public int X { get; set; }
    public int Y { get; set; }

    public LayoutPoint(int x, int y)
    {
        X = x;
        Y = y;
    }

    public Point ToPoint()
    {
        return new Point(X, Y);
    }

    public Vector2Int ToVector2Int()
    {
        return new Vector2Int(X, Y);
    }
}

public struct Node
{
    public Guid Id { get; set; }
    public LayoutPoint Point { get; set; }
    public SpeedLimit Speed { get; set; }
}
