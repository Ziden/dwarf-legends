namespace DwarfFortress.WorldGen.Generation;

internal static class SeedHash
{
    public static int Hash(int a, int b, int c, int d)
    {
        unchecked
        {
            uint h = 2166136261u;
            h = Mix(h, (uint)a);
            h = Mix(h, (uint)b);
            h = Mix(h, (uint)c);
            h = Mix(h, (uint)d);
            h ^= h >> 16;
            h *= 0x7feb352d;
            h ^= h >> 15;
            h *= 0x846ca68b;
            h ^= h >> 16;
            return (int)h;
        }
    }

    public static float Unit(int a, int b, int c, int d)
    {
        var h = Hash(a, b, c, d) & int.MaxValue;
        return h / (float)int.MaxValue;
    }

    private static uint Mix(uint h, uint v)
    {
        h ^= v + 0x9e3779b9u + (h << 6) + (h >> 2);
        return h;
    }
}
