using System;

namespace DwarfFortress.WorldGen.Generation;

/// <summary>
/// Deterministic coherent-noise helpers used across world/region/local generation.
/// </summary>
internal static class CoherentNoise
{
    public static float Value2D(int seed, float x, float y, int salt = 0)
    {
        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        var x1 = x0 + 1;
        var y1 = y0 + 1;

        var tx = x - x0;
        var ty = y - y0;
        var sx = Smooth(tx);
        var sy = Smooth(ty);

        var v00 = Hash01(seed, x0, y0, salt);
        var v10 = Hash01(seed, x1, y0, salt);
        var v01 = Hash01(seed, x0, y1, salt);
        var v11 = Hash01(seed, x1, y1, salt);

        var ix0 = Lerp(v00, v10, sx);
        var ix1 = Lerp(v01, v11, sx);
        return Lerp(ix0, ix1, sy);
    }

    public static float Fractal2D(
        int seed,
        float x,
        float y,
        int octaves = 4,
        float lacunarity = 2f,
        float gain = 0.5f,
        int salt = 0)
    {
        if (octaves <= 0)
            throw new ArgumentOutOfRangeException(nameof(octaves));

        var sum = 0f;
        var norm = 0f;
        var amp = 1f;
        var freq = 1f;

        for (var i = 0; i < octaves; i++)
        {
            sum += Value2D(seed, x * freq, y * freq, salt + (i * 1013)) * amp;
            norm += amp;
            amp *= gain;
            freq *= lacunarity;
        }

        return norm <= 0f ? 0f : sum / norm;
    }

    public static float Ridged2D(
        int seed,
        float x,
        float y,
        int octaves = 4,
        float lacunarity = 2f,
        float gain = 0.5f,
        int salt = 0)
    {
        var n = Fractal2D(seed, x, y, octaves, lacunarity, gain, salt);
        return 1f - MathF.Abs((n * 2f) - 1f);
    }

    public static float DomainWarpedFractal2D(
        int seed,
        float x,
        float y,
        int octaves = 4,
        float lacunarity = 2f,
        float gain = 0.5f,
        float warpStrength = 0.35f,
        int salt = 0)
    {
        var warpX = (Fractal2D(seed, x + 13.17f, y - 7.91f, 2, 2f, 0.5f, salt + 401) - 0.5f) * warpStrength;
        var warpY = (Fractal2D(seed, x - 11.03f, y + 5.77f, 2, 2f, 0.5f, salt + 809) - 0.5f) * warpStrength;
        return Fractal2D(seed, x + warpX, y + warpY, octaves, lacunarity, gain, salt + 1237);
    }

    private static float Hash01(int seed, int x, int y, int salt)
        => SeedHash.Unit(seed, x, y, salt);

    private static float Smooth(float t)
        => t * t * (3f - (2f * t));

    private static float Lerp(float a, float b, float t)
        => a + ((b - a) * t);
}
