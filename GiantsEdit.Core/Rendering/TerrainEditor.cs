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
    private const float RayEpsilon = 1e-10f;
    private const int GaussianBrushRange = 3;
    private const float GaussianSigmaDivisor = 2f;
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
        if (MathF.Abs(denom) < RayEpsilon) return default;

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
        if (MathF.Abs(denom) < RayEpsilon) return default;

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
        if (MathF.Abs(denom) < RayEpsilon) return default;

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
        if (MathF.Abs(denom) < RayEpsilon) return default;

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
            int range = (int)MathF.Round(brushRadius * GaussianBrushRange);
            float sigma = brushRadius / GaussianSigmaDivisor;
            float invSigmaSq = 1f / (sigma * sigma);

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
            int range = (int)MathF.Round(brushRadius * GaussianBrushRange);
            float sigma = brushRadius / GaussianSigmaDivisor;
            float invSigmaSq = 1f / (sigma * sigma);

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

    #region Triangle editing

    /// <summary>
    /// Transition table for triangle painting. triamask[paintmode, quadrant, currentType] → newType.
    /// Ported directly from Delphi PaintTriangles const triamask.
    /// </summary>
    private static readonly byte[,,] TriaMask = new byte[2, 8, 8]
    {
        // paintmode 0 (right-click / remove)
        {
            { 0, 1, 2, 3, 4, 5, 6, 7 },
            { 0, 0, 2, 0, 0, 2, 2, 0 },
            { 0, 1, 0, 0, 0, 1, 1, 0 },
            { 0, 0, 0, 0, 4, 7, 7, 7 },
            { 0, 0, 0, 3, 0, 3, 3, 0 },
            { 0, 0, 0, 0, 0, 0, 0, 0 },
            { 0, 0, 0, 0, 0, 0, 0, 0 },
            { 0, 0, 0, 3, 0, 3, 3, 0 },
        },
        // paintmode 1 (left-click / add)
        {
            { 0, 1, 2, 3, 4, 5, 6, 7 },
            { 1, 1, 5, 5, 5, 5, 6, 5 },
            { 2, 5, 2, 5, 5, 5, 6, 5 },
            { 3, 5, 5, 3, 5, 5, 6, 5 },
            { 4, 5, 5, 5, 4, 5, 6, 7 },
            { 5, 5, 5, 5, 5, 5, 6, 5 },
            { 6, 5, 5, 5, 5, 5, 6, 5 },
            { 7, 5, 5, 5, 7, 5, 6, 7 },
        },
    };

    /// <summary>
    /// Paint/erase triangles by quadrant (SpeedButton4 in Delphi).
    /// Left-click adds, right-click removes.
    /// </summary>
    public static void PaintTriangleSet(
        TerrainData terrain, float gridX, float gridY, float brushRadius, bool rightButton)
    {
        int xl = terrain.Width;
        int yl = terrain.Height;
        int paintmode = rightButton ? 0 : 1;

        if (brushRadius <= 0)
        {
            int ix = (int)gridX;
            int iy = (int)gridY;
            if (ix < 0 || ix >= xl - 1 || iy < 0 || iy >= yl - 1) return;

            float s = gridX - ix;
            float t = gridY - iy;
            // Determine which quadrant of the cell was clicked
            int quadrant;
            if (s > 0.5f)
                quadrant = t > 0.5f ? 2 : 3;  // TR or BR
            else
                quadrant = t > 0.5f ? 7 : 1;  // BL or TL

            int ci = iy * xl + ix;
            int cur = terrain.Triangles[ci] & 7;
            terrain.Triangles[ci] = (byte)((terrain.Triangles[ci] & 0xF8) | TriaMask[paintmode, quadrant, cur]);
        }
        else if (brushRadius <= 1)
        {
            int rx = (int)MathF.Round(gridX);
            int ry = (int)MathF.Round(gridY);
            ChangeVertex(terrain, rx, ry, paintmode, xl, yl);
        }
        else
        {
            int rx = (int)MathF.Round(gridX);
            int ry = (int)MathF.Round(gridY);
            int range = (int)MathF.Round(brushRadius);
            for (int dy = -range; dy <= range; dy++)
                for (int dx = -range; dx <= range; dx++)
                    if (dx * dx + dy * dy < brushRadius * brushRadius)
                        ChangeVertex(terrain, rx + dx, ry + dy, paintmode, xl, yl);
        }
    }

    /// <summary>
    /// Toggle diagonal direction on full-quad cells (SpeedButton5 in Delphi).
    /// Left-click: type 5 (normal tiling), Right-click: type 6 (alternate tiling).
    /// </summary>
    public static void PaintTriangleDiagDirection(
        TerrainData terrain, float gridX, float gridY, float brushRadius, bool rightButton)
    {
        int xl = terrain.Width;
        int yl = terrain.Height;
        int paintmode = rightButton ? 0 : 1;

        int range = (int)MathF.Round(brushRadius);
        int cx = (int)gridX;
        int cy = (int)gridY;

        for (int dy = -range; dy <= range; dy++)
            for (int dx = -range; dx <= range; dx++)
            {
                if (brushRadius > 0 && dx * dx + dy * dy >= brushRadius * brushRadius) continue;
                int x3 = cx + dx;
                int y3 = cy + dy;
                if (x3 < 0 || y3 < 0 || x3 >= xl - 1 || y3 >= yl - 1) continue;
                int ci = y3 * xl + x3;
                int curType = terrain.Triangles[ci] & 7;
                if (curType == 5 || curType == 6)
                    terrain.Triangles[ci] = (byte)((terrain.Triangles[ci] & 0xF8) | (5 + paintmode));
            }
    }

    /// <summary>
    /// Auto-optimize diagonal direction based on height differences (SpeedButton6 in Delphi).
    /// Left-click: smooth (pick flattest diagonal), Right-click: coarse (pick steepest).
    /// </summary>
    public static void PaintTriangleDiagOptimize(
        TerrainData terrain, float gridX, float gridY, float brushRadius, bool rightButton)
    {
        int xl = terrain.Width;
        int yl = terrain.Height;
        int paintmode = rightButton ? 0 : 1;

        int range = (int)MathF.Round(brushRadius);
        int cx = (int)gridX;
        int cy = (int)gridY;

        for (int dy = -range; dy <= range; dy++)
            for (int dx = -range; dx <= range; dx++)
            {
                if (brushRadius > 0 && dx * dx + dy * dy >= brushRadius * brushRadius) continue;
                int x3 = cx + dx;
                int y3 = cy + dy;
                if (x3 < 0 || y3 < 0 || x3 >= xl - 1 || y3 >= yl - 1) continue;
                int ci = y3 * xl + x3;
                int curType = terrain.Triangles[ci] & 7;
                if (curType == 5 || curType == 6)
                {
                    // Compare the two possible diagonals
                    float diagA = MathF.Abs(terrain.Heights[y3 * xl + (x3 + 1)] - terrain.Heights[(y3 + 1) * xl + x3]);
                    float diagB = MathF.Abs(terrain.Heights[(y3 + 1) * xl + (x3 + 1)] - terrain.Heights[y3 * xl + x3]);
                    int heightChoice = (diagA > diagB) ? 1 : 0;
                    // XOR with paintmode: left-click=smooth (flattest), right-click=coarse (steepest)
                    terrain.Triangles[ci] = (byte)((terrain.Triangles[ci] & 0xF8) | (5 + (heightChoice ^ paintmode)));
                }
            }
    }

    /// <summary>
    /// Auto-determine triangle type from valid neighboring corners (SpeedButton9 in Delphi).
    /// </summary>
    public static void PaintTriangleOptCorner(
        TerrainData terrain, float gridX, float gridY, float brushRadius, bool rightButton)
    {
        int xl = terrain.Width;
        int yl = terrain.Height;
        int paintmode = rightButton ? 0 : 1;

        int range = (int)MathF.Round(brushRadius);
        int cx = (int)gridX;
        int cy = (int)gridY;

        for (int dy = -range; dy <= range; dy++)
            for (int dx = -range; dx <= range; dx++)
            {
                if (brushRadius > 0 && dx * dx + dy * dy >= brushRadius * brushRadius) continue;
                int x3 = cx + dx;
                int y3 = cy + dy;
                if (x3 < 0 || y3 < 0 || x3 >= xl - 1 || y3 >= yl - 1) continue;

                // Determine which corners are "valid" (have adjacent filled triangles)
                int cornerBits =
                    (CornerValid(terrain, x3, y3) ? 1 : 0)
                  | (CornerValid(terrain, x3 + 1, y3) ? 2 : 0)
                  | (CornerValid(terrain, x3, y3 + 1) ? 4 : 0)
                  | (CornerValid(terrain, x3 + 1, y3 + 1) ? 8 : 0);

                int ci = y3 * xl + x3;
                int newType = cornerBits switch
                {
                    7 => 1 * paintmode,   // BL-TL-TR
                    11 => 3 * paintmode,  // TR-TL-BR
                    13 => 7 * paintmode,  // BL-TL-BR
                    14 => 2 * paintmode,  // TR-BL-BR
                    15 => ComputeOptimalDiag(terrain, x3, y3, xl, paintmode), // Full quad
                    _ => -1, // Stays empty
                };

                if (newType >= 0)
                    terrain.Triangles[ci] = (byte)((terrain.Triangles[ci] & 0xF8) | newType);
            }
    }

    private static int ComputeOptimalDiag(TerrainData terrain, int x, int y, int xl, int paintmode)
    {
        float diagA = MathF.Abs(terrain.Heights[y * xl + (x + 1)] - terrain.Heights[(y + 1) * xl + x]);
        float diagB = MathF.Abs(terrain.Heights[(y + 1) * xl + (x + 1)] - terrain.Heights[y * xl + x]);
        int heightChoice = (diagA > diagB) ? 1 : 0;
        return 5 + (heightChoice ^ paintmode);
    }

    /// <summary>
    /// Checks if a corner vertex has at least one adjacent non-empty triangle.
    /// Port of Delphi cornervalid function.
    /// </summary>
    private static bool CornerValid(TerrainData terrain, int x, int y)
    {
        int xl = terrain.Width;
        int yl = terrain.Height;
        if (x < 0 || y < 0 || x >= xl || y >= yl) return false;

        // Check the four cells that share this corner
        byte[] validTR = [2, 3, 4, 5, 6, 7]; // cell at (x-1, y-1): corner is at TR
        byte[] validTL = [1, 2, 4, 5, 6, 7]; // cell at (x, y-1): corner is at TL
        byte[] validBR = [1, 2, 3, 5, 6];    // cell at (x-1, y): corner is at BR
        byte[] validBL = [1, 3, 4, 5, 6, 7]; // cell at (x, y): corner is at BL

        if (x > 0 && y > 0)
        {
            int t = terrain.Triangles[(y - 1) * xl + (x - 1)] & 7;
            if (Array.IndexOf(validTR, (byte)t) >= 0) return true;
        }
        if (x < xl - 1 && y > 0)
        {
            int t = terrain.Triangles[(y - 1) * xl + x] & 7;
            if (Array.IndexOf(validTL, (byte)t) >= 0) return true;
        }
        if (x > 0 && y < yl - 1)
        {
            int t = terrain.Triangles[y * xl + (x - 1)] & 7;
            if (Array.IndexOf(validBR, (byte)t) >= 0) return true;
        }
        if (x < xl - 1 && y < yl - 1)
        {
            int t = terrain.Triangles[y * xl + x] & 7;
            if (Array.IndexOf(validBL, (byte)t) >= 0) return true;
        }
        return false;
    }

    /// <summary>
    /// Changes the four cells surrounding a vertex (used for brush radius >= 1).
    /// Port of Delphi ChangeVertex.
    /// </summary>
    private static void ChangeVertex(TerrainData terrain, int x, int y, int paintmode, int xl, int yl)
    {
        void Apply(int cx, int cy, int quadrant)
        {
            if (cx < 0 || cy < 0 || cx >= xl - 1 || cy >= yl - 1) return;
            int ci = cy * xl + cx;
            int cur = terrain.Triangles[ci] & 7;
            terrain.Triangles[ci] = (byte)((terrain.Triangles[ci] & 0xF8) | TriaMask[paintmode, quadrant, cur]);
        }

        // Cell (x, y): vertex is at TL → quadrant 1
        Apply(x, y, 1);
        // Cell (x-1, y-1): vertex is at BR → quadrant 2
        Apply(x - 1, y - 1, 2);
        // Cell (x-1, y): vertex is at TR → quadrant 3
        Apply(x - 1, y, 3);
        // Cell (x, y-1): vertex is at BL → quadrant 7
        Apply(x, y - 1, 7);
    }

    #endregion

    #region Object picking

    /// <summary>
    /// Picks the nearest object hit by a screen ray using ray-sphere intersection.
    /// Matches the Delphi checkobjecthit logic.
    /// </summary>
    public static ObjectInstance? PickObject(
        int screenX, int screenY,
        int viewportWidth, int viewportHeight,
        EditorCamera camera,
        TerrainData? terrain,
        IReadOnlyList<ObjectInstance> objects,
        float hitRadius = 50f)
    {
        float fovRad = camera.FieldOfView / 360f * 2f * MathF.PI;
        float fovFactor = 2f * MathF.Tan(fovRad / 2f) / viewportHeight;
        float x2 = -(screenX - viewportWidth / 2f) * fovFactor;
        float y2 = -(screenY - viewportHeight / 2f) * fovFactor;

        Vector3 ray = camera.Forward + x2 * camera.Right + y2 * camera.Up;
        Vector3 eye = camera.Position;

        // Determine max hit distance from terrain (objects behind terrain are not selectable)
        float maxDist = 1e20f;
        if (terrain != null)
        {
            var terrainHit = Raytrace(eye, ray, terrain);
            if (terrainHit.Hit)
                maxDist = terrainHit.Distance;
        }

        float bestDist = maxDist;
        ObjectInstance? bestObj = null;

        foreach (var obj in objects)
        {
            // Use per-object bounding radius if available, otherwise fall back to default
            float r = obj.HitRadius > 0 ? obj.HitRadius * obj.Scale : hitRadius;

            // Ray-sphere intersection: sphere centered at obj.Position, radius = r
            Vector3 oc = eye - obj.Position;
            float a = Vector3.Dot(ray, ray);
            float b = 2f * Vector3.Dot(oc, ray);
            float c = Vector3.Dot(oc, oc) - r * r;
            float discriminant = b * b - 4f * a * c;

            if (discriminant < 0) continue;

            float sqrtD = MathF.Sqrt(discriminant);
            float t1 = (-b - sqrtD) / (2f * a);
            float t2 = (-b + sqrtD) / (2f * a);

            if (t2 < 0) continue; // Sphere is behind camera
            float t = t1 < 0 ? t2 : t1; // Use nearest positive intersection

            if (t < bestDist)
            {
                bestDist = t;
                bestObj = obj;
            }
        }

        return bestObj;
    }

    /// <summary>
    /// Gets the world-space position where a screen ray intersects the terrain surface,
    /// with height interpolated at the hit point. Used for placing objects.
    /// </summary>
    public static Vector3? GetWorldHitPosition(
        int screenX, int screenY,
        int viewportWidth, int viewportHeight,
        EditorCamera camera,
        TerrainData terrain)
    {
        var hit = ScreenToTerrain(screenX, screenY, viewportWidth, viewportHeight, camera, terrain);
        if (!hit.Hit) return null;

        // Convert grid coords to world coords
        float worldX = hit.GridX * terrain.Header.Stretch + terrain.Header.XOffset;
        float worldY = hit.GridY * terrain.Header.Stretch + terrain.Header.YOffset;

        // Interpolate height
        int ix = (int)hit.GridX;
        int iy = (int)hit.GridY;
        float s = hit.GridX - ix;
        float t = hit.GridY - iy;
        int xl = terrain.Width;
        int x0 = Math.Clamp(ix, 0, xl - 1);
        int x1 = Math.Clamp(ix + 1, 0, xl - 1);
        int y0 = Math.Clamp(iy, 0, terrain.Height - 1);
        int y1 = Math.Clamp(iy + 1, 0, terrain.Height - 1);

        float worldZ = terrain.Heights[y0 * xl + x0] * (1 - s) * (1 - t)
                     + terrain.Heights[y0 * xl + x1] * s * (1 - t)
                     + terrain.Heights[y1 * xl + x0] * (1 - s) * t
                     + terrain.Heights[y1 * xl + x1] * s * t;

        return new Vector3(worldX, worldY, worldZ);
    }

    #endregion
}
