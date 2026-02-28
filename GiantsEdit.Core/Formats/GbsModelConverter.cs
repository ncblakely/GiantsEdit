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
        bool hasNormals = model.HasNormals;
        bool calcNormals = (model.OptionsFlags & GbsModel.CalcNormalsFlag) != 0;

        // Count total triangles across all parts
        int totalTris = 0;
        foreach (var part in model.Parts)
            totalTris += part.Triangles.Count;

        // Pre-compute smooth vertex normals for CalcNormals models.
        // The game accumulates face normals per shared base point, then normalizes.
        Vector3[]? smoothNormals = null;
        if (hasNormals && calcNormals)
        {
            smoothNormals = new Vector3[model.BasePoints.Length];

            foreach (var part in model.Parts)
            {
                foreach (var tri in part.Triangles)
                {
                    int i0 = model.PointIndices1[tri[0]];
                    int i1 = model.PointIndices1[tri[1]];
                    int i2 = model.PointIndices1[tri[2]];

                    Vector3 v0 = model.BasePoints[i0];
                    Vector3 v1 = model.BasePoints[i1];
                    Vector3 v2 = model.BasePoints[i2];

                    // cross(v0-v1, v0-v2) — matching game's GEO_Vec3CrossFast(&normal, &src2, &src1)
                    Vector3 faceNormal = Vector3.Cross(v0 - v1, v0 - v2);
                    float len = faceNormal.Length();
                    if (len > 0) faceNormal /= len;

                    smoothNormals[i0] += faceNormal;
                    smoothNormals[i1] += faceNormal;
                    smoothNormals[i2] += faceNormal;
                }
            }

            for (int i = 0; i < smoothNormals.Length; i++)
            {
                float len = smoothNormals[i].Length();
                if (len > 0) smoothNormals[i] /= len;
            }
        }

        // For HasNormalsFlag models (with normal defs), compute vertex normals from face normal refs
        Vector3[]? ndefNormals = null;
        if (hasNormals && !calcNormals && model.HasNormalData && model.VertexRefs.Length > 0)
        {
            // First compute all face normals sequentially
            var faceNormals = new Vector3[totalTris];
            int fi = 0;
            foreach (var part in model.Parts)
            {
                foreach (var tri in part.Triangles)
                {
                    Vector3 p0 = model.BasePoints[model.PointIndices1[tri[0]]];
                    Vector3 p1 = model.BasePoints[model.PointIndices1[tri[1]]];
                    Vector3 p2 = model.BasePoints[model.PointIndices1[tri[2]]];
                    // Unnormalized face normal (game doesn't normalize in the stored-normals path)
                    faceNormals[fi++] = Vector3.Cross(p2 - p0, p1 - p0);
                }
            }

            // Walk normal defs: [count, faceIdx, faceIdx, ..., count, faceIdx, ...]
            int ndefCount = model.TexPos;
            ndefNormals = new Vector3[ndefCount];
            int refPos = 0;
            for (int ni = 0; ni < ndefCount && refPos < model.VertexRefs.Length; ni++)
            {
                int faceCount = model.VertexRefs[refPos++];
                Vector3 accum = Vector3.Zero;
                for (int fc = 0; fc < faceCount && refPos < model.VertexRefs.Length; fc++)
                {
                    int face = model.VertexRefs[refPos++];
                    if (face < faceNormals.Length)
                        accum += faceNormals[face];
                }
                float len = accum.Length();
                ndefNormals[ni] = len > 0 ? accum / len : Vector3.UnitZ;
            }
        }

        // Stride: pos(3) + normal(3) + uv(2) + color(3) = 11
        const int stride = 11;
        var vertices = new float[totalTris * 3 * stride];
        var indices = new uint[totalTris * 3];
        var parts = new List<ModelPartData>();
        int vertIdx = 0;
        int idxIdx = 0;

        // Bounds computation
        var boundsMin = new Vector3(float.MaxValue);
        var boundsMax = new Vector3(float.MinValue);

        foreach (var part in model.Parts)
        {
            int partIndexStart = idxIdx;

            foreach (var tri in part.Triangles)
            {
                // Flat face normal fallback (for HasNormalsFlag models without normal defs)
                Vector3 flatNormal = Vector3.UnitZ;
                if (hasNormals && smoothNormals == null && ndefNormals == null)
                {
                    Vector3 p0 = model.BasePoints[model.PointIndices1[tri[0]]];
                    Vector3 p1 = model.BasePoints[model.PointIndices1[tri[1]]];
                    Vector3 p2 = model.BasePoints[model.PointIndices1[tri[2]]];
                    flatNormal = Vector3.Normalize(Vector3.Cross(p1 - p0, p2 - p0));
                    if (float.IsNaN(flatNormal.X)) flatNormal = Vector3.UnitZ;
                }

                for (int k = 0; k < 3; k++)
                {
                    int l = tri[k]; // point index
                    int baseIdx = model.PointIndices1[l]; // map to basepoint

                    Vector3 pos = model.BasePoints[baseIdx];
                    boundsMin = Vector3.Min(boundsMin, pos);
                    boundsMax = Vector3.Max(boundsMax, pos);
                    float u = model.PointUVs[l][0];
                    float v = model.PointUVs[l][1];

                    // Vertex color: models without the RGBs flag or with zero vertex colors
                    // on unlit (no normals) models default to white so the texture is visible.
                    float cr = model.PointColors[l * 3 + 0] / 255f;
                    float cg = model.PointColors[l * 3 + 1] / 255f;
                    float cb = model.PointColors[l * 3 + 2] / 255f;
                    if (!model.HasRGBs || (!hasNormals && cr == 0f && cg == 0f && cb == 0f))
                    {
                        cr = cg = cb = 1f;
                    }

                    // Select normal based on model type
                    Vector3 normal;
                    if (smoothNormals != null)
                        normal = smoothNormals[baseIdx];
                    else if (ndefNormals != null && model.PointIndices2.Length > l)
                    {
                        int ni = model.PointIndices2[l];
                        normal = ni < ndefNormals.Length ? ndefNormals[ni] : Vector3.UnitZ;
                    }
                    else
                        normal = flatNormal;

                    int off = vertIdx * stride;
                    vertices[off + 0] = pos.X;
                    vertices[off + 1] = pos.Y;
                    vertices[off + 2] = pos.Z;
                    vertices[off + 3] = normal.X;
                    vertices[off + 4] = normal.Y;
                    vertices[off + 5] = normal.Z;
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
                HasAlpha = false, // determined at texture load time
                MaterialAmbient = UnpackColor(part.Ambient),
                MaterialDiffuse = UnpackColor(part.Diffuse),
                MaterialEmissive = UnpackColor(part.Emissive),
                MaterialSpecular = UnpackColor(part.Specular),
                SpecularPower = part.Power,
                Blend = part.Blend
            });
        }

        return new ModelRenderData
        {
            Vertices = vertices,
            Indices = indices,
            VertexCount = vertIdx,
            IndexCount = idxIdx,
            VertexStride = stride,
            Parts = parts,
            BoundsMin = boundsMin,
            BoundsMax = boundsMax,
            HasNormals = hasNormals
        };
    }

    private static Vector3 UnpackColor(uint color)
    {
        float r = (color & 0xFF) / 255f;
        float g = ((color >> 8) & 0xFF) / 255f;
        float b = ((color >> 16) & 0xFF) / 255f;
        return new Vector3(r, g, b);
    }
}
