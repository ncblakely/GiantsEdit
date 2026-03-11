using System.Numerics;
using GiantsEdit.Core.Formats;

namespace GiantsEdit.Core.Rendering;

/// <summary>
/// Computes per-vertex ambient occlusion for terrain heightfields using horizon-based ray marching.
/// Supports sun-directional weighting for baking into GTI v4 files.
/// </summary>
public static class AmbientOcclusionComputer
{
    /// <summary>
    /// Default sea height threshold. Vertices at or below this height are treated as water and skipped.
    /// </summary>
    private const float SeaHeight = -4096.0f;

    /// <summary>
    /// Computes sun-weighted ambient occlusion for each terrain cell.
    /// </summary>
    /// <param name="terrain">Source terrain data with heights.</param>
    /// <param name="sunDirection">Sun direction vector (toward light, will be projected onto XY plane).</param>
    /// <param name="numDirections">Number of evenly-spaced ray directions (default 64).</param>
    /// <param name="maxRadius">Maximum ray distance in grid cells (default 100).</param>
    /// <param name="progress">Optional progress callback (0.0 to 1.0).</param>
    /// <returns>Byte array of AO values (0=fully occluded, 255=fully open), one per cell.</returns>
    public static byte[] Compute(TerrainData terrain, Vector3 sunDirection, int numDirections = 64, int maxRadius = 100, Action<float>? progress = null)
    {
        int w = terrain.Width;
        int h = terrain.Height;
        int cellCount = w * h;
        var ao = new byte[cellCount];

        // Project sun direction onto horizontal plane for directional weighting
        float sunDirX = sunDirection.X;
        float sunDirY = sunDirection.Y;
        float sunLen = MathF.Sqrt(sunDirX * sunDirX + sunDirY * sunDirY);
        if (sunLen > 0.001f)
        {
            sunDirX /= sunLen;
            sunDirY /= sunLen;
        }

        // Pre-compute direction vectors and sun weights
        float[] dirX = new float[numDirections];
        float[] dirY = new float[numDirections];
        float[] sunWeight = new float[numDirections];
        float totalWeight = 0.0f;
        for (int d = 0; d < numDirections; d++)
        {
            float angle = d * (2.0f * MathF.PI / numDirections);
            dirX[d] = MathF.Cos(angle);
            dirY[d] = MathF.Sin(angle);

            // Weight rays by alignment with sun direction
            float alignment = dirX[d] * sunDirX + dirY[d] * sunDirY;
            sunWeight[d] = 0.1f + 0.9f * (alignment * 0.5f + 0.5f);
            totalWeight += sunWeight[d];
        }

        float gridStep = terrain.Header.Stretch;

        for (int vy = 0; vy < h; vy++)
        {
            for (int vx = 0; vx < w; vx++)
            {
                int idx = vy * w + vx;
                float height = terrain.Heights[idx];

                // Skip sea/water vertices — no meaningful AO
                if (height <= SeaHeight)
                {
                    ao[idx] = 0xFF;
                    continue;
                }

                float weightedOcclusion = 0.0f;

                for (int d = 0; d < numDirections; d++)
                {
                    float maxSlope = 0.0f;

                    for (int s = 1; s <= maxRadius; s++)
                    {
                        int sx = vx + (int)MathF.Round(s * dirX[d]);
                        int sy = vy + (int)MathF.Round(s * dirY[d]);

                        if (sx < 0 || sx >= w || sy < 0 || sy >= h)
                            break;

                        float sh = terrain.Heights[sy * w + sx];

                        // Skip sea vertices as occluders
                        if (sh <= SeaHeight)
                            continue;

                        float dist = s * gridStep;
                        float slope = (sh - height) / dist;

                        if (slope > maxSlope)
                            maxSlope = slope;
                    }

                    // sin(atan(x)) = x / sqrt(1 + x²)
                    if (maxSlope > 0.0f)
                        weightedOcclusion += sunWeight[d] * maxSlope / MathF.Sqrt(1.0f + maxSlope * maxSlope);
                }

                float aoValue = 1.0f - (weightedOcclusion / totalWeight);
                aoValue = Math.Clamp(aoValue, 0.0f, 1.0f);

                ao[idx] = (byte)(aoValue * 255.0f);
            }

            progress?.Invoke((float)(vy + 1) / h);
        }

        return ao;
    }
}
