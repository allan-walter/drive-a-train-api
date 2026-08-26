// Replace with your actual enum values

using DriveATrain.Services;
using OpenCvSharp;

namespace DriveATrain.OpenCv;

public class ColorRange
{
    public Scalar Color { get; set; }
    public Scalar Lower { get; set; }
    public Scalar Upper { get; set; }
    public int HTol { get; set; }
    public int STol { get; set; }
    public int VTol { get; set; }

    public string Name { get; set; }

    public ColorRange(Scalar color, string name, int hTol = 10, int sTol = 60, int vTol = 60)
    {
        Color = color;
        Name = name;
        HTol = hTol;
        STol = sTol;
        VTol = vTol;

        Lower = new Scalar(
            Math.Max(0, color.Val0 - hTol),
            Math.Max(0, color.Val1 - sTol),
            Math.Max(0, color.Val2 - vTol));
        Upper = new Scalar(
            Math.Min(179, color.Val0 + hTol),
            Math.Min(255, color.Val1 + sTol),
            Math.Min(255, color.Val2 + vTol));
    }
}

// HSV-range based color used for marker lookup/classification.
public class UnitColor
{
    // HSV (H:0-179, S:0-255, V:0-255)
    public ColorRange Color { get; set; }

    public UnitColor(ColorRange color)
    {
        Color = color;
    }

    // Red (painted loco body). Sampled from the camera: unlit paint sits at hue 171-186
    // (wrapping through 180), but the direction LED's blue-white light shifts the lit part
    // of the shell down to ~160 and brightens it, so the range reaches down and up to keep
    // the unit in one piece. The old V ceiling of 105 existed to exclude the magenta paint
    // direction marker, which the LED replaced. Bright red wires that now share the range
    // are rejected by the corridor mask and the brick shape filters instead.
    public static UnitColor UnitRed = new UnitColor(
        new ColorRange(
            color: new Scalar(173, 160, 78),
            "Red",
            hTol: 13,
            sTol: 95,
            vTol: 62
        )
    );

    public static UnitColor UnitYellow = new UnitColor(
        // Yellow (connector block)
        new ColorRange(
            color: new Scalar(19, 216, 147),
            "Yellow",
            hTol: 10,
            sTol: 60,
            vTol: 60
        )
    );

    public static readonly List<UnitColor> Colors = new List<UnitColor>
    {
        UnitRed,
        UnitYellow
    };

    public static ColorRange DirMarkerColor { get; set; } = new ColorRange(
        color: new Scalar(174, 200, 170),
        name: "Magenta",
        hTol: 6, // H range: [168..179], no wraparound into pure red
        sTol: 70,
        vTol: 70
    );
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
    public UnitColor Color { get; set; }
    public UnitDefinition? Unit { get; set; }

    // Convex, clean but might include slightly too much
    public Mat Mask { get; set; }

    // Should really only be one
    public Point[] Contour { get; set; }

    public MarkerDef(int componentId, UnitColor color, UnitDefinition? unit, Mat mask, Point[] contour)
    {
        ComponentId = componentId;
        Color = color;
        Unit = unit;
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

public struct LiveData
{
    public List<RailUnitGet> Units { get; set; }

    public SpeedLimit Forward { get; set; }
    public double ForwardLimitValue { get; set; }
    public SpeedLimit Reverse { get; set; }
    public double ReverseLimitValue { get; set; }

    public bool PowerOn { get; set; }
    public List<Uncouple> Connections { get; set; }
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