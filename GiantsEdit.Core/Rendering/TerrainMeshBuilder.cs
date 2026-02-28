using System.Numerics;
using GiantsEdit.Core.Formats;

namespace GiantsEdit.Core.Rendering;

/// <summary>
/// Builds GPU-ready mesh data from terrain (GTI) data.
/// Ported from Unit1.pas terrain vertex/index array construction.
/// </summary>
public static class TerrainMeshBuilder
{
    /// <summary>
    /// Converts TerrainData to render-ready vertex/index arrays.
    /// Triangle types from the GTI data control which diagonals are generated.
    /// </summary>
    public static TerrainRenderData Build(TerrainData terrain, Vector3? sunDirection = null, float bumpClampValue = 0f)
    {
        int w = terrain.Width;
        int h = terrain.Height;
        int vertexCount = w * h;

        // Build vertex positions: 3 floats per vertex
        var positions = new float[vertexCount * 3];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int vi = y * w + x;
                positions[vi * 3 + 0] = x * terrain.Header.Stretch + terrain.Header.XOffset;
                positions[vi * 3 + 1] = y * terrain.Header.Stretch + terrain.Header.YOffset;
                positions[vi * 3 + 2] = terrain.Heights[vi];
            }
        }

        // Build vertex colors: RGBA packed uint per vertex
        var colors = new uint[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            byte r = terrain.LightMap[i * 3 + 0];
            byte g = terrain.LightMap[i * 3 + 1];
            byte b = terrain.LightMap[i * 3 + 2];
            colors[i] = (uint)(r | (g << 8) | (b << 16) | (255 << 24));
        }

        // Compute per-vertex dot3 bump diffuse colors if sun direction is provided
        uint[]? bumpDiffuse = null;
        if (sunDirection is { } sunDir)
        {
            bumpDiffuse = ComputeBumpDiffuse(terrain, sunDir, bumpClampValue);
        }

        // Build triangle indices based on triangle type
        // Each cell (x,y) can generate 0-2 triangles
        // Triangle type byte encodes the diagonal direction
        var indices = new List<uint>((w - 1) * (h - 1) * 6);

        for (int y = 0; y < h - 1; y++)
        {
            for (int x = 0; x < w - 1; x++)
            {
                int idx = y * w + x;
                byte triType = (byte)(terrain.Triangles[idx] & 7);

                uint tl = (uint)(y * w + x);         // top-left
                uint tr = (uint)(y * w + x + 1);     // top-right
                uint bl = (uint)((y + 1) * w + x);   // bottom-left
                uint br = (uint)((y + 1) * w + x + 1); // bottom-right

                switch (triType)
                {
                    case 0: // Empty — no triangles
                        break;
                    case 1: // One triangle: BL, TL, TR (TR-BL diagonal, upper-left)
                        indices.Add(bl); indices.Add(tl); indices.Add(tr);
                        break;
                    case 2: // One triangle: TR, BL, BR (TR-BL diagonal, lower-right)
                        indices.Add(tr); indices.Add(bl); indices.Add(br);
                        break;
                    case 3: // One triangle: TR, TL, BR (TL-BR diagonal, upper-right)
                        indices.Add(tr); indices.Add(tl); indices.Add(br);
                        break;
                    case 4: // One triangle: BL, TL, BR (TL-BR diagonal, lower-left)
                        indices.Add(bl); indices.Add(tl); indices.Add(br);
                        break;
                    case 5: // Full quad, TL-BR diagonal: BL,TL,BR + TR,TL,BR
                        indices.Add(bl); indices.Add(tl); indices.Add(br);
                        indices.Add(tr); indices.Add(tl); indices.Add(br);
                        break;
                    case 6: // Full quad, TR-BL diagonal: BL,TL,TR + TR,BL,BR
                        indices.Add(bl); indices.Add(tl); indices.Add(tr);
                        indices.Add(tr); indices.Add(bl); indices.Add(br);
                        break;
                    case 7: // One triangle: BL, TL, BR (same as type 4)
                        indices.Add(bl); indices.Add(tl); indices.Add(br);
                        break;
                }
            }
        }

        return new TerrainRenderData
        {
            Positions = positions,
            Colors = colors,
            BumpDiffuseColors = bumpDiffuse,
            Indices = indices.ToArray(),
            VertexCount = vertexCount,
            IndexCount = indices.Count
        };
    }

    /// <summary>
    /// Computes per-vertex dot3 bump diffuse colors by transforming the sun direction
    /// into each vertex's tangent space and packing to [0,1] range.
    /// </summary>
    private static uint[] ComputeBumpDiffuse(TerrainData terrain, Vector3 sunDir, float bumpClampValue)
    {
        int w = terrain.Width;
        int h = terrain.Height;
        float s = terrain.Header.Stretch;
        var result = new uint[w * h];

        sunDir = Vector3.Normalize(sunDir);
        var worldX = new Vector3(1f, 0f, 0f);

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                // Compute vertex normal from height neighbors (central differences)
                int xl = Math.Max(x - 1, 0);
                int xr = Math.Min(x + 1, w - 1);
                int yb = Math.Max(y - 1, 0);
                int yt = Math.Min(y + 1, h - 1);

                float dzdx = terrain.Heights[y * w + xr] - terrain.Heights[y * w + xl];
                float dzdy = terrain.Heights[yt * w + x] - terrain.Heights[yb * w + x];
                float dx = (xr - xl) * s;
                float dy = (yt - yb) * s;

                // Normal from cross product of grid tangent and binormal
                var n = Vector3.Normalize(new Vector3(-dzdx / dx * s, -dzdy / dy * s, s));

                // Build tangent frame using world X axis as reference
                var yaxis = Vector3.Normalize(Vector3.Cross(n, worldX));
                var xaxis = Vector3.Normalize(Vector3.Cross(yaxis, n));

                // Transform sun direction: game order is (tangent, normal, binormal)
                float lx = Vector3.Dot(sunDir, xaxis);
                float ly = Vector3.Dot(sunDir, n);      // normal component → G channel
                float lz = Vector3.Dot(sunDir, yaxis);

                var localDir = Vector3.Normalize(new Vector3(lx, ly, lz));

                // Clamp normal component to ensure bump detail is visible in shaded areas
                if (localDir.Y < bumpClampValue)
                    localDir.Y = bumpClampValue;

                // Pack from [-1,1] to [0,1] then to byte
                byte rb = (byte)Math.Clamp((int)((localDir.X * 0.5f + 0.5f) * 255f), 0, 255);
                byte gb = (byte)Math.Clamp((int)((localDir.Y * 0.5f + 0.5f) * 255f), 0, 255);
                byte bb = (byte)Math.Clamp((int)((localDir.Z * 0.5f + 0.5f) * 255f), 0, 255);

                result[y * w + x] = (uint)(rb | (gb << 8) | (bb << 16) | (255 << 24));
            }
        }

        return result;
    }
}
