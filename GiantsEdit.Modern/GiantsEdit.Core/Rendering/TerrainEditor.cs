using System.Numerics;
using GiantsEdit.Core.DataModel;
using GiantsEdit.Core.Formats;

namespace GiantsEdit.Core.Rendering;

/// <summary>
/// Performs ray-terrain intersection and terrain editing operations.
/// Port of the Delphi GetTerrainVoxel/Raytrace/EditTerrain logic.
/// </summary>
public static class TerrainEditor
{
    /// <summary>
    /// Result of a terrain ray hit. GridX/GridY are fractional grid coordinates.
    /// </summary>
    public record struct HitResult(float GridX, float GridY, float Distance, bool Hit);

    /// <summary>
    /// Casts a ray from screen pixel coordinates into the terrain grid.
    /// Returns the fractional grid position hit, or Hit=false if nothing was hit.
    /// </summary>
    public static HitResult ScreenToTerrain(
        int screenX, int screenY,
        int viewportWidth, int viewportHeight,
        EditorCamera camera,
        TerrainData terrain)
    {
        // Build the ray direction in world space (matches Delphi GetTerrainVoxel)
        float fovRad = camera.FieldOfView / 360f * 2f * MathF.PI;
        float fovFactor = 2f * MathF.Tan(fovRad / 2f) / viewportHeight;
        float x2 = -(screenX - viewportWidth / 2f) * fovFactor;
        float y2 = -(screenY - viewportHeight / 2f) * fovFactor;

        Vector3 ray = camera.Forward + x2 * camera.Right + y2 * camera.Up;

        return Raytrace(camera.Position, ray, terrain);
    }

    /// <summary>
    /// Ray-terrain intersection. Tests all terrain triangles (brute force, matching Delphi).
    /// Returns fractional grid coordinates of the closest hit.
    /// </summary>
    private static HitResult Raytrace(Vector3 eye, Vector3 ray, TerrainData terrain)
    {
        int xl = terrain.Width;
        int yl = terrain.Height;

        // Transform eye and ray into grid space
        float camX = (eye.X - terrain.Header.XOffset) / terrain.Header.Stretch;
        float camY = (eye.Y - terrain.Header.YOffset) / terrain.Header.Stretch;
        float camZ = eye.Z;
        float rayX = ray.X / terrain.Header.Stretch;
        float rayY = ray.Y / terrain.Header.Stretch;
        float rayZ = ray.Z;

        float bestDist = float.MaxValue;
        float bestX = 0, bestY = 0;
        bool hasHit = false;

        for (int y = 0; y < yl - 1; y++)
        {
            for (int x = 0; x < xl - 1; x++)
            {
                byte b = (byte)(terrain.Triangles[y * xl + x] & 7);

                // Lower-left triangle (types 4,5,7): vertices (x,y), (x,y+1), (x+1,y+1)
                if (b is 4 or 5 or 7)
                {
                    var hit = TestTriangleLower(x, y, camX, camY, camZ, rayX, rayY, rayZ, terrain, xl);
                    if (hit.Hit && hit.Distance < bestDist)
                    {
                        bestDist = hit.Distance;
                        bestX = hit.GridX;
                        bestY = hit.GridY;
                        hasHit = true;
                    }
                }

                // Upper-right triangle (types 3,5): vertices (x,y), (x+1,y), (x+1,y+1)
                if (b is 3 or 5)
                {
                    var hit = TestTriangleUpper(x, y, camX, camY, camZ, rayX, rayY, rayZ, terrain, xl);
                    if (hit.Hit && hit.Distance < bestDist)
                    {
                        bestDist = hit.Distance;
                        bestX = hit.GridX;
                        bestY = hit.GridY;
                        hasHit = true;
                    }
                }

                // Alt lower-left triangle (types 1,6): vertices (x,y), (x,y+1), (x+1,y)
                if (b is 1 or 6)
                {
                    var hit = TestTriangleAltLower(x, y, camX, camY, camZ, rayX, rayY, rayZ, terrain, xl);
                    if (hit.Hit && hit.Distance < bestDist)
                    {
                        bestDist = hit.Distance;
                        bestX = hit.GridX;
                        bestY = hit.GridY;
                        hasHit = true;
                    }
                }

                // Alt upper-right triangle (types 2,6): vertices (x,y+1), (x+1,y), (x+1,y+1)
                if (b is 2 or 6)
                {
                    var hit = TestTriangleAltUpper(x, y, camX, camY, camZ, rayX, rayY, rayZ, terrain, xl);
                    if (hit.Hit && hit.Distance < bestDist)
                    {
                        bestDist = hit.Distance;
                        bestX = hit.GridX;
                        bestY = hit.GridY;
                        hasHit = true;
                    }
                }
            }
        }

        return new HitResult(bestX, bestY, bestDist, hasHit);
    }

    // Triangle test for lower-left half: (x,y)-(x,y+1)-(x+1,y+1)
    // Delphi uses plane intersection: ortho = cross of two edges, then r = dot/dot
    private static HitResult TestTriangleLower(int x, int y,
        float camX, float camY, float camZ, float rayX, float rayY, float rayZ,
        TerrainData t, int xl)
    {
        float z00 = t.Heights[y * xl + x];
        float z01 = t.Heights[(y + 1) * xl + x];
        float z11 = t.Heights[(y + 1) * xl + (x + 1)];

        float ox = z11 - z01;
        float oy = z01 - z00;
        float oz = -1f;

        float vx = x - camX, vy = y - camY, vz = z00 - camZ;
        float denom = rayX * ox + rayY * oy + rayZ * oz;
        if (MathF.Abs(denom) < 1e-10f) return default;

        float r = (vx * ox + vy * oy + vz * oz) / denom;
        float s = camX + r * rayX - x;
        float tt = camY + r * rayY - y;

        if (r > 0 && s >= 0 && tt >= 0 && s < 1 && tt < 1 && tt - s >= 0)
            return new HitResult(s + x, tt + y, r, true);
        return default;
    }

    // Triangle test for upper-right half: (x,y)-(x+1,y)-(x+1,y+1)
    private static HitResult TestTriangleUpper(int x, int y,
        float camX, float camY, float camZ, float rayX, float rayY, float rayZ,
        TerrainData t, int xl)
    {
        float z00 = t.Heights[y * xl + x];
        float z10 = t.Heights[y * xl + (x + 1)];
        float z11 = t.Heights[(y + 1) * xl + (x + 1)];

        float ox = z10 - z00;
        float oy = z11 - z10;
        float oz = -1f;

        float vx = x - camX, vy = y - camY, vz = z00 - camZ;
        float denom = rayX * ox + rayY * oy + rayZ * oz;
        if (MathF.Abs(denom) < 1e-10f) return default;

        float r = (vx * ox + vy * oy + vz * oz) / denom;
        float s = camX + r * rayX - x;
        float tt = camY + r * rayY - y;

        if (r > 0 && s >= 0 && tt >= 0 && s < 1 && tt < 1 && tt - s <= 0)
            return new HitResult(s + x, tt + y, r, true);
        return default;
    }

    // Triangle test for alt lower-left: (x,y)-(x,y+1)-(x+1,y) — diagonal goes other way
    private static HitResult TestTriangleAltLower(int x, int y,
        float camX, float camY, float camZ, float rayX, float rayY, float rayZ,
        TerrainData t, int xl)
    {
        float z00 = t.Heights[y * xl + x];
        float z10 = t.Heights[y * xl + (x + 1)];
        float z01 = t.Heights[(y + 1) * xl + x];

        float ox = z10 - z00;
        float oy = z01 - z00;
        float oz = -1f;

        float vx = x - camX, vy = y - camY, vz = z00 - camZ;
        float denom = rayX * ox + rayY * oy + rayZ * oz;
        if (MathF.Abs(denom) < 1e-10f) return default;

        float r = (vx * ox + vy * oy + vz * oz) / denom;
        float s = camX + r * rayX - x;
        float tt = camY + r * rayY - y;

        if (r > 0 && s >= 0 && tt >= 0 && s < 1 && tt < 1 && tt + s < 1)
            return new HitResult(s + x, tt + y, r, true);
        return default;
    }

    // Triangle test for alt upper-right: (x,y+1)-(x+1,y)-(x+1,y+1)
    private static HitResult TestTriangleAltUpper(int x, int y,
        float camX, float camY, float camZ, float rayX, float rayY, float rayZ,
        TerrainData t, int xl)
    {
        float z01 = t.Heights[(y + 1) * xl + x];
        float z10 = t.Heights[y * xl + (x + 1)];
        float z11 = t.Heights[(y + 1) * xl + (x + 1)];

        float ox = z11 - z01;
        float oy = z11 - z10;
        float oz = -1f;

        float vx = (x + 1) - camX, vy = y - camY, vz = z10 - camZ;
        float denom = rayX * ox + rayY * oy + rayZ * oz;
        if (MathF.Abs(denom) < 1e-10f) return default;

        float r = (vx * ox + vy * oy + vz * oz) / denom;
        float s = camX + r * rayX - x;
        float tt = camY + r * rayY - y;

        if (r > 0 && s >= 0 && tt >= 0 && s < 1 && tt < 1 && tt + s >= 1)
            return new HitResult(s + x, tt + y, r, true);
        return default;
    }

    /// <summary>
    /// Applies a height brush at the given grid position. Matches Delphi EditTerrain.
    /// Left-click blends toward targetHeight; right-click picks height.
    /// </summary>
    public static void ApplyHeightBrush(
        TerrainData terrain,
        float gridX, float gridY,
        float targetHeight,
        float brushRadius,
        float brushAlpha)
    {
        int xl = terrain.Width;
        int yl = terrain.Height;
        int ix = (int)gridX;
        int iy = (int)gridY;
        float s = gridX - ix;
        float t = gridY - iy;

        if (brushRadius <= 0)
        {
            // Single-pixel brush
            int rx = Math.Clamp((int)MathF.Round(gridX), 0, xl - 1);
            int ry = Math.Clamp((int)MathF.Round(gridY), 0, yl - 1);
            ref float h = ref terrain.Heights[ry * xl + rx];
            h = h * (1 - brushAlpha) + targetHeight * brushAlpha;
        }
        else if (brushRadius <= 1)
        {
            // Bilinear brush (4 corners)
            ApplyBilinear(terrain, ix, iy, s, t, targetHeight, brushAlpha, xl, yl);
        }
        else
        {
            // Gaussian brush
            int range = (int)MathF.Round(brushRadius * 3);
            float invSigmaSq = 1f / (brushRadius / 2f * (brushRadius / 2f));

            for (int dy = -range; dy <= range; dy++)
            {
                int cy = iy + dy;
                if (cy < 0 || cy >= yl) continue;
                for (int dx = -range; dx <= range; dx++)
                {
                    int cx = ix + dx;
                    if (cx < 0 || cx >= xl) continue;

                    float distSq = (cx - gridX) * (cx - gridX) + (cy - gridY) * (cy - gridY);
                    float a = MathF.Exp(-distSq * invSigmaSq) * brushAlpha;

                    ref float h = ref terrain.Heights[cy * xl + cx];
                    h = h * (1 - a) + targetHeight * a;
                }
            }
        }
    }

    /// <summary>
    /// Picks the terrain height at the given grid position (right-click behavior).
    /// </summary>
    public static float PickHeight(TerrainData terrain, float gridX, float gridY, float brushRadius)
    {
        int xl = terrain.Width;
        int ix = (int)gridX;
        int iy = (int)gridY;

        if (brushRadius <= 0)
        {
            int rx = Math.Clamp((int)MathF.Round(gridX), 0, xl - 1);
            int ry = Math.Clamp((int)MathF.Round(gridY), 0, terrain.Height - 1);
            return terrain.Heights[ry * xl + rx];
        }

        float s = gridX - ix;
        float t = gridY - iy;
        int x0 = Math.Clamp(ix, 0, xl - 1);
        int x1 = Math.Clamp(ix + 1, 0, xl - 1);
        int y0 = Math.Clamp(iy, 0, terrain.Height - 1);
        int y1 = Math.Clamp(iy + 1, 0, terrain.Height - 1);

        return terrain.Heights[y0 * xl + x0] * (1 - s) * (1 - t)
             + terrain.Heights[y0 * xl + x1] * s * (1 - t)
             + terrain.Heights[y1 * xl + x0] * (1 - s) * t
             + terrain.Heights[y1 * xl + x1] * s * t;
    }

    private static void ApplyBilinear(TerrainData terrain, int ix, int iy,
        float s, float t, float h, float alpha, int xl, int yl)
    {
        void Blend(int cx, int cy, float a)
        {
            if (cx < 0 || cx >= xl || cy < 0 || cy >= yl) return;
            ref float v = ref terrain.Heights[cy * xl + cx];
            v = v * (1 - a) + h * a;
        }

        Blend(ix, iy, (1 - s) * (1 - t) * alpha);
        Blend(ix + 1, iy, s * (1 - t) * alpha);
        Blend(ix, iy + 1, (1 - s) * t * alpha);
        Blend(ix + 1, iy + 1, s * t * alpha);
    }

    /// <summary>
    /// Applies a light/color brush at the given grid position. Matches Delphi PaintTerrain.
    /// </summary>
    public static void ApplyLightBrush(
        TerrainData terrain,
        float gridX, float gridY,
        byte colorR, byte colorG, byte colorB,
        float brushRadius,
        float brushAlpha)
    {
        int xl = terrain.Width;
        int yl = terrain.Height;
        int ix = (int)gridX;
        int iy = (int)gridY;
        float s = gridX - ix;
        float t = gridY - iy;

        if (brushRadius <= 0)
        {
            int rx = Math.Clamp((int)MathF.Round(gridX), 0, xl - 1);
            int ry = Math.Clamp((int)MathF.Round(gridY), 0, yl - 1);
            int ci = ry * xl + rx;
            BlendLight(terrain.LightMap, ci, colorR, colorG, colorB, brushAlpha);
        }
        else if (brushRadius <= 1)
        {
            void BlendAt(int cx, int cy, float a)
            {
                if (cx < 0 || cx >= xl || cy < 0 || cy >= yl) return;
                BlendLight(terrain.LightMap, cy * xl + cx, colorR, colorG, colorB, a);
            }
            BlendAt(ix, iy, (1 - s) * (1 - t) * brushAlpha);
            BlendAt(ix + 1, iy, s * (1 - t) * brushAlpha);
            BlendAt(ix, iy + 1, (1 - s) * t * brushAlpha);
            BlendAt(ix + 1, iy + 1, s * t * brushAlpha);
        }
        else
        {
            int range = (int)MathF.Round(brushRadius * 3);
            float invSigmaSq = 1f / (brushRadius / 2f * (brushRadius / 2f));

            for (int dy = -range; dy <= range; dy++)
            {
                int cy = iy + dy;
                if (cy < 0 || cy >= yl) continue;
                for (int dx = -range; dx <= range; dx++)
                {
                    int cx = ix + dx;
                    if (cx < 0 || cx >= xl) continue;

                    float distSq = (cx - gridX) * (cx - gridX) + (cy - gridY) * (cy - gridY);
                    float a = MathF.Exp(-distSq * invSigmaSq) * brushAlpha;
                    BlendLight(terrain.LightMap, cy * xl + cx, colorR, colorG, colorB, a);
                }
            }
        }
    }

    /// <summary>
    /// Picks the light color at the given grid position (right-click in light mode).
    /// Returns (R, G, B).
    /// </summary>
    public static (byte R, byte G, byte B) PickLight(TerrainData terrain, float gridX, float gridY, float brushRadius)
    {
        int xl = terrain.Width;
        int ix = (int)gridX;
        int iy = (int)gridY;

        if (brushRadius <= 0)
        {
            int rx = Math.Clamp((int)MathF.Round(gridX), 0, xl - 1);
            int ry = Math.Clamp((int)MathF.Round(gridY), 0, terrain.Height - 1);
            int ci = ry * xl + rx;
            return (terrain.LightMap[ci * 3], terrain.LightMap[ci * 3 + 1], terrain.LightMap[ci * 3 + 2]);
        }

        float s = gridX - ix;
        float t = gridY - iy;
        int x0 = Math.Clamp(ix, 0, xl - 1);
        int x1 = Math.Clamp(ix + 1, 0, xl - 1);
        int y0 = Math.Clamp(iy, 0, terrain.Height - 1);
        int y1 = Math.Clamp(iy + 1, 0, terrain.Height - 1);

        byte Interp(int ch) => (byte)Math.Clamp((int)MathF.Round(
            terrain.LightMap[(y0 * xl + x0) * 3 + ch] * (1 - s) * (1 - t)
          + terrain.LightMap[(y0 * xl + x1) * 3 + ch] * s * (1 - t)
          + terrain.LightMap[(y1 * xl + x0) * 3 + ch] * (1 - s) * t
          + terrain.LightMap[(y1 * xl + x1) * 3 + ch] * s * t), 0, 255);

        return (Interp(0), Interp(1), Interp(2));
    }

    private static void BlendLight(byte[] lightMap, int cellIdx, byte r, byte g, byte b, float a)
    {
        int i = cellIdx * 3;
        lightMap[i + 0] = (byte)Math.Clamp((int)MathF.Round(lightMap[i + 0] * (1 - a) + r * a), 0, 255);
        lightMap[i + 1] = (byte)Math.Clamp((int)MathF.Round(lightMap[i + 1] * (1 - a) + g * a), 0, 255);
        lightMap[i + 2] = (byte)Math.Clamp((int)MathF.Round(lightMap[i + 2] * (1 - a) + b * a), 0, 255);
    }
}
