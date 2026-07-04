// Replace with your actual enum values

using DriveATrain.OpenCv;
using DriveATrain.Services;
using OpenCvSharp;

public static class Colors
{
    public static readonly Scalar ORANGE = new Scalar(0, 165, 255);
    public static readonly Scalar RED = new Scalar(0, 0, 255);
    public static readonly Scalar GREEN = new Scalar(0, 255, 0);
}

// HSV-range based color used for marker lookup/classification.
public class LookupColor
{
    // Hsv
    public Scalar Lower { get; set; }

    public Scalar Upper { get; set; }

    // bgr
    public Scalar SingleColor { get; set; }

    public LookupColor(Scalar lower, Scalar upper, Scalar singleColor)
    {
        Lower = lower;
        Upper = upper;
        SingleColor = singleColor;
    }

    public static readonly List<LookupColor> Colors = new List<LookupColor>
    {
        // Black (train roof) - actually blue-grey under this lighting
        new LookupColor(
            lower: new Scalar(95.0, 20.0, 30.0),
            upper: new Scalar(140.0, 150.0, 85.0), // V: 100 -> 75 to cut bright shadows
            singleColor: new Scalar(0.298 * 255, 0.249, 0.252)
        ),
        // Yellow
        new LookupColor(
            lower: new Scalar(10.0, 50.0, 70.0),
            upper: new Scalar(37.0, 180.0, 220.0),
            singleColor: new Scalar(0.396 * 255, 0.585 * 255, 0.788 * 255.0)
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
    public Mat RawMask { get; set; }

    public MarkerDef(int componentId, LookupColor color, UnitDefinition? unit, Point center, Mat mask, Mat rawMask)
    {
        ComponentId = componentId;
        Color = color;
        Unit = unit;
        Center = center;
        Mask = mask;
        RawMask = rawMask;
    }
}

// The class containing a lot of info about the detected results
public class UnitMarkerResponse
{
    public List<Vector2Int> Box { get; set; }
    public Transform Front { get; set; }
    public Transform Back { get; set; }
    public MarkerDef Marker { get; set; }

    public UnitMarkerResponse(List<Vector2Int> box, Transform front, Transform back, MarkerDef marker)
    {
        Box = box;
        Front = front;
        Back = back;
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
}

// What is returned to the frontend about the detected units
public class RailUnitGet
{
    public UnitDefinition Def { get; set; }
    public Vector2Int A { get; set; }
    public Vector2Int B { get; set; }
    public Vector2Int C { get; set; }
    public Vector2Int D { get; set; }
    public Transform Front { get; set; }
    public Transform Back { get; set; }

    public RailUnitGet(UnitDefinition def, Vector2Int a, Vector2Int b, Vector2Int c, Vector2Int d, Transform front,
        Transform back)
    {
        Def = def;
        A = a;
        B = b;
        C = c;
        D = d;
        Front = front;
        Back = back;
    }

    public RailUnitGet(UnitMarkerResponse model) : this(
        model.Marker.Unit,
        model.Box[0],
        model.Box[1],
        model.Box[2],
        model.Box[3],
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

        var mockA = new RailUnitGet(
            loco,
            new Vector2Int(100, 100), // A
            new Vector2Int(300, 100), // B
            new Vector2Int(300, 300), // C
            new Vector2Int(100, 300), // D
            front: new Transform(new Vector2Int(300, 200), new Vector2Double(0, 0)), // midpoint of B-C (right side)
            back: new Transform(new Vector2Int(100, 200), new Vector2Double(0, 0)) // midpoint of A-D (left side)
        );
        var mockB = new RailUnitGet(
            unit,
            new Vector2Int(350 + offsetB, 100), // A
            new Vector2Int(750 + offsetB, 100), // B
            new Vector2Int(750 + offsetB, 400), // C
            new Vector2Int(350 + offsetB, 400), // D
            front: new Transform(new Vector2Int(750 + offsetB, 250),
                new Vector2Double(0, 0)), // midpoint of B-C (right side)
            back: new Transform(new Vector2Int(350 + offsetB, 250),
                new Vector2Double(0, 0)) // midpoint of A-D (left side)
        );
        return new List<RailUnitGet> { mockA, mockB };
    }
}