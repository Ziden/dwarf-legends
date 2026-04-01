using System.Collections.Generic;

namespace DwarfFortress.GameLogic.Core;

/// <summary>
/// Engine-agnostic immutable 3D integer vector.
/// Replaces Godot's Vector3I in all GameLogic code.
/// </summary>
public readonly record struct Vec3i(int X, int Y, int Z)
{
    public static readonly Vec3i Zero    = new(0,  0,  0);
    public static readonly Vec3i One     = new(1,  1,  1);
    public static readonly Vec3i Up      = new(0,  0,  1);
    public static readonly Vec3i Down    = new(0,  0, -1);
    public static readonly Vec3i North   = new(0, -1,  0);
    public static readonly Vec3i South   = new(0,  1,  0);
    public static readonly Vec3i East    = new(1,  0,  0);
    public static readonly Vec3i West    = new(-1, 0,  0);

    public Vec3i Offset(int dx, int dy, int dz) => new(X + dx, Y + dy, Z + dz);

    public static Vec3i operator +(Vec3i a, Vec3i b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3i operator -(Vec3i a, Vec3i b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3i operator *(Vec3i a, int s)   => new(a.X * s,   a.Y * s,   a.Z * s);

    public float LengthSquared() => X * X + Y * Y + Z * Z;

    /// <summary>Manhattan distance (no diagonals, no z movement cost).</summary>
    public int ManhattanDistanceTo(Vec3i other)
        => System.Math.Abs(X - other.X) + System.Math.Abs(Y - other.Y) + System.Math.Abs(Z - other.Z);

    /// <summary>Returns the 6 orthogonal face-neighbours (NSEW + up + down).</summary>
    public IEnumerable<Vec3i> Neighbours6()
    {
        yield return new(X + 1, Y,     Z);
        yield return new(X - 1, Y,     Z);
        yield return new(X,     Y + 1, Z);
        yield return new(X,     Y - 1, Z);
        yield return new(X,     Y,     Z + 1);
        yield return new(X,     Y,     Z - 1);
    }

    /// <summary>Returns the 4 horizontal neighbours (NSEW only, no z change).</summary>
    public IEnumerable<Vec3i> Neighbours4()
    {
        yield return new(X + 1, Y,     Z);
        yield return new(X - 1, Y,     Z);
        yield return new(X,     Y + 1, Z);
        yield return new(X,     Y - 1, Z);
    }

    public override string ToString() => $"({X}, {Y}, {Z})";
}
