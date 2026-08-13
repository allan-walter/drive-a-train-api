// Replace with your actual enum values

using DriveATrain.Services;
using OpenCvSharp;

namespace DriveATrain.OpenCv;

// HSV-range based color used for marker lookup/classification.
public class LookupColor
{
    // HSV (H:0-179, S:0-255, V:0-255)
    public Scalar SingleColor { get; set; }
    public Scalar Lower { get; set; }
    public Scalar Upper { get; set; }

    public LookupColor(Scalar singleColor, int hTol = 10, int sTol = 60, int vTol = 60)
    {
        SingleColor = singleColor;
        Lower = new Scalar(
            Math.Max(0, SingleColor.Val0 - hTol),
            Math.Max(0, SingleColor.Val1 - sTol),
            Math.Max(0, SingleColor.Val2 - vTol));
        Upper = new Scalar(
            Math.Min(179, SingleColor.Val0 + hTol),
            Math.Min(255, SingleColor.Val1 + sTol),
            Math.Min(255, SingleColor.Val2 + vTol));
    }

    public static readonly List<LookupColor> Colors = new List<LookupColor>
    {
        // Black (train roof/body) - hue/sat are unreliable this dark,
        // so use a wide H/S tolerance and rely on a tight, low V ceiling instead.
        new LookupColor(
            singleColor: new Scalar(0, 0, 35),
            hTol: 179, // hue meaningless at low V - accept any hue
            sTol: 255, // saturation meaningless at low V - accept any sat
            vTol: 35 // only match dark pixels: V in [0, 70]
        ),

        // Yellow (connector block)
        new LookupColor(
            singleColor: new Scalar(19, 216, 147),
            hTol: 10,
            sTol: 60,
            vTol: 60
        ),
    };
}

public enum UnitType
{
    Locomotive,
    Wagon,
    // ...
}

// A list of units in a config file
public class UnitDefinition
{
    public string Name { get; set; }
    public UnitType Type { get; set; }
    public int Address { get; set; }
    public int FrontCouplerIndex { get; set; }
    public int BackCouplerIndex { get; set; }
    public bool? DirMarkerOnBack { get; set; }
}

// The midpoint when we've kinda identified something
public class Transform
{
    public Vector2Int Position { get; set; }
    public Vector2Double Direction { get; set; }

    public Transform(Vector2Int position, Vector2Double direction)
    {
        Position = position;
        Direction = direction;
    }
}

// The midpoint when we've kinda identified something
public class MarkerDef
{
    // The id used in opencv
    public int ComponentId { get; set; }

    // hsv
    public LookupColor Color { get; set; }
    public UnitDefinition? Unit { get; set; }

    public Point Center { get; set; }

    // Convex, clean but might include slightly too much
    public Mat Mask { get; set; }

    // Should really only be one
    public Point[] Contour { get; set; }

    public MarkerDef(int componentId, LookupColor color, UnitDefinition? unit, Point center, Mat mask, Point[] contour)
    {
        ComponentId = componentId;
        Color = color;
        Unit = unit;
        Center = center;
        Mask = mask;
        Contour = contour;
    }
}

// The class containing a lot of info about the detected results
public class UnitMarkerResponse
{
    public Vector2Int Front { get; set; }
    public Vector2Int Back { get; set; }
    public Vector2Int Center { get; set; }
    public MarkerDef Marker { get; set; }

    public UnitMarkerResponse(Vector2Int front, Vector2Int back, MarkerDef marker)
    {
        Front = front;
        Back = back;
        Center = new Vector2Int((front.X + back.X) / 2, (front.Y + back.Y) / 2);
        Marker = marker;
    }
}

public class LiveData
{
    public List<RailUnitGet> Units { get; set; }

    public SpeedLimit Forward { get; set; }
    public double ForwardValue { get; set; }
    public SpeedLimit Reverse { get; set; }
    public double ReverseValue { get; set; }

    public bool PowerOn { get; set; }
}

// What is returned to the frontend about the detected units
public class RailUnitGet
{
    public UnitDefinition Def { get; set; }

    // public Vector2Int A { get; set; }
    // public Vector2Int B { get; set; }
    // public Vector2Int C { get; set; }
    // public Vector2Int D { get; set; }
    public Vector2Int Front { get; set; }
    public Vector2Int Back { get; set; }

    public RailUnitGet(UnitDefinition def, Vector2Int front, Vector2Int back)
    {
        Def = def;
        // A = a;
        // B = b;
        // C = c;
        // D = d;
        Front = front;
        Back = back;
    }

    public RailUnitGet(UnitMarkerResponse model) : this(
        model.Marker.Unit,
        // model.Box[0],
        // model.Box[1],
        // model.Box[2],
        // model.Box[3],
        model.Front,
        model.Back)
    {
    }
}

public static class RailUnitMocks
{
    public static List<RailUnitGet> GetMocks(UnitDefinition loco, UnitDefinition unit)
    {
        // Determine which 5-second phase we're in.
        // Even 5s window = original positions, odd 5s window = mockB moved away.
        long secondsElapsed = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        bool isFarPhase = (secondsElapsed / 5) % 2 == 1;
        isFarPhase = false;
        // How far to push mockB away during the "far" phase.
        const int separationOffset = 400;
        int offsetB = isFarPhase ? separationOffset : 0;

        // var mockA = new RailUnitGet(
        //     loco,
        //     new Vector2Int(100, 100), // A
        //     new Vector2Int(300, 100), // B
        //     new Vector2Int(300, 300), // C
        //     new Vector2Int(100, 300), // D
        //     front: new Transform(new Vector2Int(300, 200), new Vector2Double(0, 0)), // midpoint of B-C (right side)
        //     back: new Transform(new Vector2Int(100, 200), new Vector2Double(0, 0)) // midpoint of A-D (left side)
        // );
        // var mockB = new RailUnitGet(
        //     unit,
        //     new Vector2Int(350 + offsetB, 100), // A
        //     new Vector2Int(750 + offsetB, 100), // B
        //     new Vector2Int(750 + offsetB, 400), // C
        //     new Vector2Int(350 + offsetB, 400), // D
        //     front: new Transform(new Vector2Int(750 + offsetB, 250),
        //         new Vector2Double(0, 0)), // midpoint of B-C (right side)
        //     back: new Transform(new Vector2Int(350 + offsetB, 250),
        //         new Vector2Double(0, 0)) // midpoint of A-D (left side)
        // );
        return new List<RailUnitGet> { };
    }
}