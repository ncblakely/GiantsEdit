using System.Numerics;
using GiantsEdit.Core.Rendering;

namespace GiantsEdit.Core.Formats;

/// <summary>
/// Converts a parsed GbsModel into ModelRenderData suitable for GPU upload.
/// Matches the Delphi DrawModel rendering: for each triangle vertex,
/// position comes from basepoint[point_1[l]], UV from point_uv[l],
/// color from point_c[l] + part diffuse.
/// </summary>
public static class GbsModelConverter
{
    public static ModelRenderData ToRenderData(GbsModel model)
    {
        // Count total triangles across all parts
        int totalTris = 0;
        foreach (var part in model.Parts)
            totalTris += part.Triangles.Count;

        // Stride: pos(3) + normal(3) + uv(2) + color(3) = 11
        const int stride = 11;
        var vertices = new float[totalTris * 3 * stride];
        var indices = new uint[totalTris * 3];
        var parts = new List<ModelPartData>();
        int vertIdx = 0;
        int idxIdx = 0;

        foreach (var part in model.Parts)
        {
            int partIndexStart = idxIdx;

            // Extract diffuse color components (RGBA packed as uint)
            float diffR = (part.Diffuse & 0xFF) / 255f;
            float diffG = ((part.Diffuse >> 8) & 0xFF) / 255f;
            float diffB = ((part.Diffuse >> 16) & 0xFF) / 255f;

            foreach (var tri in part.Triangles)
            {
                for (int k = 0; k < 3; k++)
                {
                    int l = tri[k]; // point index
                    int baseIdx = model.PointIndices1[l]; // map to basepoint

                    Vector3 pos = model.BasePoints[baseIdx];
                    float u = model.PointUVs[l][0];
                    float v = model.PointUVs[l][1];

                    // Color = vertex color + diffuse (clamped to 0..1 like Delphi)
                    float cr = model.PointColors[l * 3 + 0] / 255f + diffR;
                    float cg = model.PointColors[l * 3 + 1] / 255f + diffG;
                    float cb = model.PointColors[l * 3 + 2] / 255f + diffB;
                    cr = MathF.Min(cr, 1f);
                    cg = MathF.Min(cg, 1f);
                    cb = MathF.Min(cb, 1f);

                    int off = vertIdx * stride;
                    vertices[off + 0] = pos.X;
                    vertices[off + 1] = pos.Y;
                    vertices[off + 2] = pos.Z;
                    // Normal: zero (Delphi doesn't use normals for lighting)
                    vertices[off + 3] = 0;
                    vertices[off + 4] = 0;
                    vertices[off + 5] = 1;
                    vertices[off + 6] = u;
                    vertices[off + 7] = v;
                    vertices[off + 8] = cr;
                    vertices[off + 9] = cg;
                    vertices[off + 10] = cb;

                    indices[idxIdx] = (uint)vertIdx;
                    vertIdx++;
                    idxIdx++;
                }
            }

            parts.Add(new ModelPartData
            {
                IndexOffset = partIndexStart,
                IndexCount = idxIdx - partIndexStart,
                TextureName = part.TextureName,
                HasAlpha = false // determined at texture load time
            });
        }

        return new ModelRenderData
        {
            Vertices = vertices,
            Indices = indices,
            VertexCount = vertIdx,
            IndexCount = idxIdx,
            VertexStride = stride,
            Parts = parts
        };
    }
}
