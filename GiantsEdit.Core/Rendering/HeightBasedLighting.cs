using System.Numerics;
using GiantsEdit.Core.Formats;

namespace GiantsEdit.Core.Rendering;

/// <summary>
/// RGB color used by the height-based lighting editor.
/// </summary>
public readonly record struct LightingColor(byte R, byte G, byte B)
{
    public static readonly LightingColor White = new(255, 255, 255);
}

/// <summary>
/// Settings for the original editor's height-based lightmap generator.
/// </summary>
public sealed class HeightBasedLightingSettings
{
    public int LevelCount { get; set; } = 7;
    public float[] HeightLevels { get; set; } = [-40, 0, 100, 200, 400, 700, 1000];
    public LightingColor[] HeightColors { get; set; } =
    [
        new(128, 255, 0),
        new(255, 255, 0),
        new(255, 192, 32),
        new(255, 128, 64),
        new(192, 64, 64),
        new(128, 64, 32),
        LightingColor.White
    ];
    public bool ApplyAutomatically { get; set; }
    public bool EnableSunSky { get; set; }
    public Vector3 SunDirection { get; set; } = new(0.8f, 0f, 0.6f);
    public LightingColor SunColor { get; set; } = new(255, 224, 0);
    public LightingColor SkyColor { get; set; } = new(0, 31, 255);
    public LightingColor TopColor { get; set; } = new(0, 0, 0);

    public static HeightBasedLightingSettings CreateDefault() => new();

    public HeightBasedLightingSettings Clone()
    {
        return new HeightBasedLightingSettings
        {
            LevelCount = LevelCount,
            HeightLevels = (float[])HeightLevels.Clone(),
            HeightColors = (LightingColor[])HeightColors.Clone(),
            ApplyAutomatically = ApplyAutomatically,
            EnableSunSky = EnableSunSky,
            SunDirection = SunDirection,
            SunColor = SunColor,
            SkyColor = SkyColor,
            TopColor = TopColor
        };
    }

    /// <summary>
    /// Returns a user-facing validation error, or null when the settings can be applied.
    /// </summary>
    public string? Validate()
    {
        if (LevelCount is < 1 or > 7)
            return "Select between one and seven height levels.";
        if (HeightLevels.Length < LevelCount || HeightColors.Length < LevelCount)
            return "Every selected height level must have a height and color.";

        for (int i = 0; i < LevelCount; i++)
        {
            if (!float.IsFinite(HeightLevels[i]))
                return $"Height level {i + 1} is not a valid number.";
            if (i > 0 && HeightLevels[i] <= HeightLevels[i - 1])
                return "Height values must increase from bottom to top.";
        }

        if (EnableSunSky && SunDirection.LengthSquared() < 0.000001f)
            return "The sun direction cannot be zero.";

        return null;
    }
}

/// <summary>
/// Recreates the Delphi Heightbasedlighting.pas lightmap algorithm.
/// </summary>
public static class HeightBasedLighting
{
    /// <summary>
    /// Generates lightmap colors for the specified terrain region.
    /// The upper bounds are exclusive, matching the Delphi implementation.
    /// </summary>
    public static void Apply(
        TerrainData terrain,
        HeightBasedLightingSettings settings,
        int x1 = 0,
        int y1 = 0,
        int x2 = int.MaxValue,
        int y2 = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(settings);

        string? error = settings.Validate();
        if (error != null)
            throw new ArgumentException(error, nameof(settings));
        if (terrain.Width < 1 || terrain.Height < 1)
            return;
        if (terrain.Heights.Length < terrain.Width * terrain.Height ||
            terrain.Triangles.Length < terrain.Width * terrain.Height ||
            terrain.LightMap.Length < terrain.Width * terrain.Height * 3)
            throw new ArgumentException("Terrain arrays do not match the terrain dimensions.", nameof(terrain));

        int startX = Math.Max(x1, 0);
        int startY = Math.Max(y1, 0);
        int endX = Math.Min(x2, terrain.Width);
        int endY = Math.Min(y2, terrain.Height);
        if (startX >= endX || startY >= endY)
            return;

        Vector3 sunDirection = settings.EnableSunSky
            ? Vector3.Normalize(settings.SunDirection)
            : default;

        for (int y = startY; y < endY; y++)
        {
            for (int x = startX; x < endX; x++)
            {
                int heightIndex = y * terrain.Width + x;
                float height = Math.Clamp(
                    terrain.Heights[heightIndex],
                    settings.HeightLevels[0],
                    settings.HeightLevels[settings.LevelCount - 1]);

                int level = FindLevel(settings.HeightLevels, settings.LevelCount, height);
                float upperWeight = level == settings.LevelCount - 1
                    ? 0f
                    : (height - settings.HeightLevels[level]) /
                      (settings.HeightLevels[level + 1] - settings.HeightLevels[level]);
                float lowerWeight = 1f - upperWeight;

                LightingColor lower = settings.HeightColors[level];
                LightingColor upper = settings.HeightColors[Math.Min(level + 1, settings.LevelCount - 1)];
                float red = lower.R * lowerWeight + upper.R * upperWeight;
                float green = lower.G * lowerWeight + upper.G * upperWeight;
                float blue = lower.B * lowerWeight + upper.B * upperWeight;

                if (settings.EnableSunSky)
                {
                    Vector3 normal = CalculateNormal(terrain, x, y);
                    float sunAmount = MathF.Max(Vector3.Dot(normal, sunDirection), 0f);
                    red *= (settings.TopColor.R * normal.Z
                            + settings.SkyColor.R
                            + settings.SunColor.R * sunAmount) / 128f;
                    green *= (settings.TopColor.G * normal.Z
                              + settings.SkyColor.G
                              + settings.SunColor.G * sunAmount) / 128f;
                    blue *= (settings.TopColor.B * normal.Z
                             + settings.SkyColor.B
                             + settings.SunColor.B * sunAmount) / 128f;
                }

                int lightIndex = heightIndex * 3;
                terrain.LightMap[lightIndex + 0] = ToByte(red);
                terrain.LightMap[lightIndex + 1] = ToByte(green);
                terrain.LightMap[lightIndex + 2] = ToByte(blue);
            }
        }
    }

    private static int FindLevel(float[] levels, int levelCount, float height)
    {
        int level = 0;
        while (level < levelCount - 2 && height > levels[level + 1])
            level++;
        return level;
    }

    private static Vector3 CalculateNormal(TerrainData terrain, int x, int y)
    {
        float nx;
        float ny;
        float nz = terrain.Header.Stretch;

        if (HasTriangleOne(terrain, x, y) || HasTriangleTwo(terrain, x, y - 1))
        {
            nx = HasTriangleOne(terrain, x - 1, y) || HasTriangleTwo(terrain, x - 1, y - 1)
                ? (HeightAt(terrain, x + 1, y) - HeightAt(terrain, x - 1, y)) / 2f
                : HeightAt(terrain, x + 1, y) - HeightAt(terrain, x, y);
        }
        else
        {
            nx = HasTriangleOne(terrain, x - 1, y) || HasTriangleTwo(terrain, x - 1, y - 1)
                ? HeightAt(terrain, x, y) - HeightAt(terrain, x - 1, y)
                : 0f;
        }

        if (HasTriangleTwo(terrain, x, y) || HasTriangleOne(terrain, x - 1, y))
        {
            ny = HasTriangleTwo(terrain, x, y - 1) || HasTriangleOne(terrain, x - 1, y - 1)
                ? (HeightAt(terrain, x, y + 1) - HeightAt(terrain, x, y - 1)) / 2f
                : HeightAt(terrain, x, y + 1) - HeightAt(terrain, x, y);
        }
        else
        {
            ny = HasTriangleTwo(terrain, x, y - 1) || HasTriangleOne(terrain, x - 1, y - 1)
                ? HeightAt(terrain, x, y) - HeightAt(terrain, x, y - 1)
                : 0f;
        }

        Vector3 normal = new(nx, ny, nz);
        return normal.LengthSquared() < 0.000001f
            ? Vector3.UnitZ
            : Vector3.Normalize(normal);
    }

    private static float HeightAt(TerrainData terrain, int x, int y)
    {
        x = Math.Clamp(x, 0, terrain.Width - 1);
        y = Math.Clamp(y, 0, terrain.Height - 1);
        return terrain.Heights[y * terrain.Width + x];
    }

    private static bool HasTriangleOne(TerrainData terrain, int x, int y)
    {
        if (x < 0 || y < 0 || x >= terrain.Width - 1 || y >= terrain.Height - 1)
            return false;

        return (terrain.Triangles[y * terrain.Width + x] & 7) is 1 or 3 or 5 or 6;
    }

    private static bool HasTriangleTwo(TerrainData terrain, int x, int y)
    {
        if (x < 0 || y < 0 || x >= terrain.Width - 1 || y >= terrain.Height - 1)
            return false;

        return (terrain.Triangles[y * terrain.Width + x] & 7) is 2 or 4 or 5 or 6 or 7;
    }

    private static byte ToByte(float value)
    {
        return (byte)Math.Clamp((int)MathF.Round(value), 0, 255);
    }
}
