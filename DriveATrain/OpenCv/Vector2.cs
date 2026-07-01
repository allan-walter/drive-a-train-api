using System.Drawing;

namespace DriveATrain.OpenCv;

using System;

public readonly struct Vector2Double
{
    public double X { get; }
    public double Y { get; }

    public Vector2Double(double x = 0.0, double y = 0.0)
    {
        X = x;
        Y = y;
    }

    public static Vector2Double operator -(Vector2Double v) => new Vector2Double(-v.X, -v.Y);

    public static Vector2Double operator *(Vector2Double v, double scalar) =>
        new Vector2Double(v.X * scalar, v.Y * scalar);

    public Vector2Int ToInt()
    {
        return new Vector2Int((int)X, (int)Y);
    }

    public override bool Equals(object obj) =>
        obj is Vector2Double other && X.Equals(other.X) && Y.Equals(other.Y);

    public override int GetHashCode() => HashCode.Combine(X, Y);
    
    public Vector2Double Rotate90CW()
    {
        return new Vector2Double(Y, -X);
    }
}

public readonly struct Vector2Int
{
    public int X { get; }
    public int Y { get; }

    public Vector2Int(int x = 0, int y = 0)
    {
        X = x;
        Y = y;
    }

    public static Vector2Int operator -(Vector2Int v) => new Vector2Int(-v.X, -v.Y);

    public static Vector2Int operator /(Vector2Int v, int scalar) =>
        new Vector2Int(v.X / scalar, v.Y / scalar);

    public static Vector2Int operator +(Vector2Int a, Vector2Int b) =>
        new Vector2Int(a.X + b.X, a.Y + b.Y);

    public static Vector2Int operator -(Vector2Int a, Vector2Int b) =>
        new Vector2Int(a.X - b.X, a.Y - b.Y);

    public static Vector2Int operator *(Vector2Int v, int scalar) =>
        new Vector2Int(v.X * scalar, v.Y * scalar);

    public Point ToPoint()
    {
        return new Point(X, Y);
    }

    public bool HasPassed(Vector2Int target, Vector2Double direction)
    {
        // Not technically correct but happens when the loco fully overlaps block
        if (this.X == target.X && this.Y == target.Y) return true;

        var toTarget = target - this;
        var value = toTarget.Dot(direction) < 0.0;
        return value;
    }

    public Vector2Int Back()
    {
        return new Vector2Int(-X, -Y);
    }

    public double Dot(Vector2Double other) => X * other.X + Y * other.Y;

    public double DistanceTo(OpenCvSharp.Point other)
    {
        return Math.Sqrt(Math.Pow(X - other.X, 2) + Math.Pow(Y - other.Y, 2));
    }

    public double DistanceTo(Vector2Int other)
    {
        return Math.Sqrt(Math.Pow((double)(X - other.X), 2) + Math.Pow((double)(Y - other.Y), 2));
    }

    public double Magnitude()
    {
        return Math.Sqrt((double)(X * X + Y * Y));
    }

    public Vector2Double Normalized()
    {
        var mag = Magnitude();
        if (mag == 0.0)
            return new Vector2Double(0.0, 0.0); // avoid divide by zero
        return new Vector2Double(X / mag, Y / mag);
    }


    public override bool Equals(object obj) =>
        obj is Vector2Int other && X == other.X && Y == other.Y;

    public override int GetHashCode() => HashCode.Combine(X, Y);
}