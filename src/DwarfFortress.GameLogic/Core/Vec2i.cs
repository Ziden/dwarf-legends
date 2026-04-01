namespace DwarfFortress.GameLogic.Core;

/// <summary>
/// Engine-agnostic immutable 2D integer vector.
/// Replaces Godot's Vector2I in all GameLogic code.
/// </summary>
public readonly record struct Vec2i(int X, int Y)
{
    public static readonly Vec2i Zero  = new(0, 0);
    public static readonly Vec2i One   = new(1, 1);
    public static readonly Vec2i North = new(0, -1);
    public static readonly Vec2i South = new(0,  1);
    public static readonly Vec2i East  = new(1,  0);
    public static readonly Vec2i West  = new(-1, 0);

    public Vec2i Offset(int dx, int dy) => new(X + dx, Y + dy);

    public static Vec2i operator +(Vec2i a, Vec2i b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2i operator -(Vec2i a, Vec2i b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2i operator *(Vec2i a, int s)   => new(a.X * s,   a.Y * s);

    public int ManhattanDistanceTo(Vec2i other)
        => System.Math.Abs(X - other.X) + System.Math.Abs(Y - other.Y);

    public Vec3i ToVec3i(int z = 0) => new(X, Y, z);

    public override string ToString() => $"({X}, {Y})";
}
