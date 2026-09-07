namespace GiantsEdit.Core.Formats;

/// <summary>
/// Subdivides a terrain grid using Catmull-Rom bicubic interpolation,
/// increasing resolution by a given factor. Near sea/empty edges,
/// interpolates on the covering source triangle to preserve topology.
/// </summary>
public static class TerrainSubdivider
{
    /// <summary>
    /// Subdivides the terrain by the given factor. A factor of 3 turns each
    /// original cell into 3×3 sub-cells, tripling resolution in each dimension.
    /// </summary>
    public static TerrainData Subdivide(TerrainData source, int factor)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(factor, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(factor, 20);

        int srcW = source.Width;
        int srcH = source.Height;

        int newW = (srcW - 1) * factor + 1;
        int newH = (srcH - 1) * factor + 1;
        float newStretch = source.Header.Stretch / factor;

        var result = new TerrainData
        {
            Header = source.Header,
            TextureName = source.TextureName,
            Heights = new float[newW * newH],
            Triangles = new byte[newW * newH],
            LightMap = new byte[newW * newH * 3],
        };
        result.Header.Width = newW;
        result.Header.Height = newH;
        result.Header.Stretch = newStretch;

        float invFactor = 1.0f / factor;

        // Default lightmap to magenta (same as GtiFormat.Load defaults).
        for (int i = 0; i < newW * newH; i++)
        {
            result.LightMap[i * 3 + 0] = 255;
            result.LightMap[i * 3 + 1] = 0;
            result.LightMap[i * 3 + 2] = 255;
        }

        // Default heights to MinHeight.
        Array.Fill(result.Heights, source.Header.MinHeight);

        // Interpolate heights and colors at each new grid point.
        for (int ny = 0; ny < newH; ny++)
        {
            float srcY = ny * invFactor;
            for (int nx = 0; nx < newW; nx++)
            {
                float srcX = nx * invFactor;

                // A source point can lie on the edge of an active cell while
                // floor() selects an adjacent empty cell.
                if (!IsTerrainPoint(source, srcX, srcY))
                {
                    continue;
                }

                float z = SampleZ(source, srcX, srcY);

                int idx = ny * newW + nx;
                result.Heights[idx] = z;

                // Interpolate lightmap RGB.
                result.LightMap[idx * 3 + 0] = SampleChannel(source, srcX, srcY, 0);
                result.LightMap[idx * 3 + 1] = SampleChannel(source, srcX, srcY, 1);
                result.LightMap[idx * 3 + 2] = SampleChannel(source, srcX, srcY, 2);
            }
        }

        // Assign triangle connectivity types.
        for (int ny = 0; ny < newH; ny++)
        {
            for (int nx = 0; nx < newW; nx++)
            {
                float centerX = (nx + 0.5f) * invFactor;
                float centerY = (ny + 0.5f) * invFactor;
                if (nx < newW - 1 && ny < newH - 1 &&
                    IsTerrainPoint(source, centerX, centerY))
                {
                    int sourceCellX = nx / factor;
                    int sourceCellY = ny / factor;
                    float localX = nx * invFactor - sourceCellX;
                    float localY = ny * invFactor - sourceCellY;
                    float step = invFactor;
                    byte sourceType = (byte)(source.Triangles[sourceCellY * srcW + sourceCellX] & 7);

                    result.Triangles[ny * newW + nx] = DetermineConnType(
                        IsPointInsideCell(sourceType, localX, localY),
                        IsPointInsideCell(sourceType, localX + step, localY),
                        IsPointInsideCell(sourceType, localX, localY + step),
                        IsPointInsideCell(sourceType, localX + step, localY + step));
                }
                // else stays 0 (no triangles at grid edge)
            }
        }

        // Update min/max height from interpolated data.
        result.Header.MinHeight = float.MaxValue;
        result.Header.MaxHeight = float.MinValue;
        for (int i = 0; i < newW * newH; i++)
        {
            float h = result.Heights[i];
            if (h < result.Header.MinHeight) result.Header.MinHeight = h;
            if (h > result.Header.MaxHeight) result.Header.MaxHeight = h;
        }

        return result;
    }

    private static bool IsActive(byte triType) => (triType & 7) is >= 1 and <= 7;

    /// <summary>
    /// Checks whether a fractional source-grid point is covered by any source
    /// triangle, including triangles in cells sharing an integer grid edge.
    /// </summary>
    private static bool IsTerrainPoint(TerrainData src, float fx, float fy)
    {
        int ix = (int)MathF.Floor(fx);
        int iy = (int)MathF.Floor(fy);
        bool onGridX = MathF.Abs(fx - MathF.Round(fx)) <= 1e-5f;
        bool onGridY = MathF.Abs(fy - MathF.Round(fy)) <= 1e-5f;
        int firstX = onGridX ? ix - 1 : ix;
        int firstY = onGridY ? iy - 1 : iy;

        for (int cellY = firstY; cellY <= iy; cellY++)
        {
            for (int cellX = firstX; cellX <= ix; cellX++)
            {
                if (cellX < 0 || cellX >= src.Width - 1 ||
                    cellY < 0 || cellY >= src.Height - 1)
                    continue;

                byte triType = (byte)(src.Triangles[cellY * src.Width + cellX] & 7);
                float localX = fx - cellX;
                float localY = fy - cellY;
                if (IsPointInsideCell(triType, localX, localY))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks whether a local cell coordinate lies inside the source cell's
    /// triangle geometry. Boundary points are included so adjacent cells share
    /// their terrain vertices.
    /// </summary>
    private static bool IsPointInsideCell(byte triType, float localX, float localY)
    {
        const float epsilon = 1e-5f;
        if (localX < -epsilon || localX > 1.0f + epsilon ||
            localY < -epsilon || localY > 1.0f + epsilon)
            return false;

        return triType switch
        {
            1 => localX + localY <= 1.0f + epsilon,
            2 => localX + localY >= 1.0f - epsilon,
            3 => localX >= localY - epsilon,
            4 or 7 => localX <= localY + epsilon,
            5 or 6 => true,
            _ => false,
        };
    }

    #region Height Interpolation

    /// <summary>
    /// Catmull-Rom cubic interpolation between p1 and p2.
    /// t ∈ [0,1] is position between p1 (t=0) and p2 (t=1).
    /// </summary>
    private static float CatmullRom(float p0, float p1, float p2, float p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * (
            (2.0f * p1) +
            (-p0 + p2) * t +
            (2.0f * p0 - 5.0f * p1 + 4.0f * p2 - p3) * t2 +
            (-p0 + 3.0f * p1 - 3.0f * p2 + p3) * t3
        );
    }

    private static float ClampedSampleZ(TerrainData src, int x, int y)
    {
        x = Math.Clamp(x, 0, src.Width - 1);
        y = Math.Clamp(y, 0, src.Height - 1);
        return src.Heights[y * src.Width + x];
    }

    private static bool IsEmptyCell(TerrainData src, int x, int y)
    {
        x = Math.Clamp(x, 0, src.Width - 1);
        y = Math.Clamp(y, 0, src.Height - 1);
        return !IsActive(src.Triangles[y * src.Width + x]);
    }

    /// <summary>
    /// Checks if the 4×4 bicubic neighborhood is fully terrain (no empty cells).
    /// </summary>
    private static bool IsFullyTerrain4x4(TerrainData src, int ix, int iy)
    {
        for (int j = -1; j <= 2; j++)
            for (int i = -1; i <= 2; i++)
                if (IsEmptyCell(src, ix + i, iy + j))
                    return false;
        return true;
    }

    /// <summary>
    /// Interpolates Z on the source triangle covering a fractional point.
    /// This preserves coastline triangle planes instead of blending inactive
    /// corners into valid underwater geometry.
    /// </summary>
    private static float TriangleSampleZ(TerrainData src, float fx, float fy)
    {
        int ix = (int)MathF.Floor(fx);
        int iy = (int)MathF.Floor(fy);
        bool onGridX = MathF.Abs(fx - MathF.Round(fx)) <= 1e-5f;
        bool onGridY = MathF.Abs(fy - MathF.Round(fy)) <= 1e-5f;
        int firstX = onGridX ? ix - 1 : ix;
        int firstY = onGridY ? iy - 1 : iy;

        for (int cellY = firstY; cellY <= iy; cellY++)
        {
            for (int cellX = firstX; cellX <= ix; cellX++)
            {
                if (cellX < 0 || cellX >= src.Width - 1 ||
                    cellY < 0 || cellY >= src.Height - 1)
                    continue;

                byte triType = (byte)(src.Triangles[cellY * src.Width + cellX] & 7);
                float localX = fx - cellX;
                float localY = fy - cellY;
                if (!IsPointInsideCell(triType, localX, localY))
                    continue;

                float tl = src.GetHeight(cellX, cellY);
                float tr = src.GetHeight(cellX + 1, cellY);
                float bl = src.GetHeight(cellX, cellY + 1);
                float br = src.GetHeight(cellX + 1, cellY + 1);

                return triType switch
                {
                    1 => InterpolateTriangleZ(
                        localX, localY,
                        0.0f, 1.0f, bl,
                        0.0f, 0.0f, tl,
                        1.0f, 0.0f, tr),
                    2 => InterpolateTriangleZ(
                        localX, localY,
                        1.0f, 0.0f, tr,
                        0.0f, 1.0f, bl,
                        1.0f, 1.0f, br),
                    3 => InterpolateTriangleZ(
                        localX, localY,
                        1.0f, 0.0f, tr,
                        0.0f, 0.0f, tl,
                        1.0f, 1.0f, br),
                    4 or 7 => InterpolateTriangleZ(
                        localX, localY,
                        0.0f, 1.0f, bl,
                        0.0f, 0.0f, tl,
                        1.0f, 1.0f, br),
                    5 when localX <= localY => InterpolateTriangleZ(
                        localX, localY,
                        0.0f, 1.0f, bl,
                        0.0f, 0.0f, tl,
                        1.0f, 1.0f, br),
                    5 => InterpolateTriangleZ(
                        localX, localY,
                        1.0f, 0.0f, tr,
                        0.0f, 0.0f, tl,
                        1.0f, 1.0f, br),
                    6 when localX + localY <= 1.0f => InterpolateTriangleZ(
                        localX, localY,
                        0.0f, 1.0f, bl,
                        0.0f, 0.0f, tl,
                        1.0f, 0.0f, tr),
                    6 => InterpolateTriangleZ(
                        localX, localY,
                        1.0f, 0.0f, tr,
                        0.0f, 1.0f, bl,
                        1.0f, 1.0f, br),
                    _ => src.Header.MinHeight,
                };
            }
        }

        return src.Header.MinHeight;
    }

    private static float InterpolateTriangleZ(
        float x, float y,
        float ax, float ay, float az,
        float bx, float by, float bz,
        float cx, float cy, float cz)
    {
        float denominator = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy);
        float weightA = ((by - cy) * (x - cx) + (cx - bx) * (y - cy)) / denominator;
        float weightB = ((cy - ay) * (x - cx) + (ax - cx) * (y - cy)) / denominator;
        float weightC = 1.0f - weightA - weightB;
        return weightA * az + weightB * bz + weightC * cz;
    }

    /// <summary>
    /// Bicubic (Catmull-Rom) interpolation of Z height.
    /// </summary>
    private static float BicubicSampleZ(TerrainData src, float fx, float fy)
    {
        int ix = (int)MathF.Floor(fx);
        int iy = (int)MathF.Floor(fy);
        float tx = fx - ix;
        float ty = fy - iy;

        Span<float> cols = stackalloc float[4];
        for (int j = -1; j <= 2; j++)
        {
            float r0 = ClampedSampleZ(src, ix - 1, iy + j);
            float r1 = ClampedSampleZ(src, ix, iy + j);
            float r2 = ClampedSampleZ(src, ix + 1, iy + j);
            float r3 = ClampedSampleZ(src, ix + 2, iy + j);
            cols[j + 1] = CatmullRom(r0, r1, r2, r3, tx);
        }
        return CatmullRom(cols[0], cols[1], cols[2], cols[3], ty);
    }

    /// <summary>
    /// Samples Z at fractional source coordinates, choosing interpolation method
    /// based on proximity to empty edges.
    /// </summary>
    private static float SampleZ(TerrainData src, float fx, float fy)
    {
        int ix = (int)MathF.Floor(fx);
        int iy = (int)MathF.Floor(fy);

        if (!IsFullyTerrain4x4(src, ix, iy))
            return TriangleSampleZ(src, fx, fy);

        float result = BicubicSampleZ(src, fx, fy);

        // Clamp to range of the 4 enclosing cell corners to prevent
        // Catmull-Rom ringing (overshoot/undershoot at sharp transitions).
        float z00 = ClampedSampleZ(src, ix, iy);
        float z10 = ClampedSampleZ(src, ix + 1, iy);
        float z01 = ClampedSampleZ(src, ix, iy + 1);
        float z11 = ClampedSampleZ(src, ix + 1, iy + 1);
        float localMin = Math.Min(Math.Min(z00, z10), Math.Min(z01, z11));
        float localMax = Math.Max(Math.Max(z00, z10), Math.Max(z01, z11));
        return Math.Clamp(result, localMin, localMax);
    }

    #endregion

    #region Color Interpolation

    private static byte ClampedSampleColor(TerrainData src, int x, int y, int channel)
    {
        x = Math.Clamp(x, 0, src.Width - 1);
        y = Math.Clamp(y, 0, src.Height - 1);
        return src.LightMap[(y * src.Width + x) * 3 + channel];
    }

    /// <summary>
    /// Bilinear interpolation of a single lightmap color channel,
    /// excluding empty/sea corners to avoid blending in default colors.
    /// </summary>
    private static byte BilinearSampleChannel(TerrainData src, float fx, float fy, int channel)
    {
        int ix = (int)MathF.Floor(fx);
        int iy = (int)MathF.Floor(fy);
        float tx = fx - ix;
        float ty = fy - iy;

        ReadOnlySpan<float> weights =
        [
            (1 - tx) * (1 - ty),
            tx * (1 - ty),
            (1 - tx) * ty,
            tx * ty,
        ];
        int[,] coords = { { ix, iy }, { ix + 1, iy }, { ix, iy + 1 }, { ix + 1, iy + 1 } };

        float sum = 0.0f, wsum = 0.0f;
        for (int i = 0; i < 4; i++)
        {
            int cx = coords[i, 0], cy = coords[i, 1];
            if (IsTerrainPoint(src, cx, cy))
            {
                sum += ClampedSampleColor(src, cx, cy, channel) * weights[i];
                wsum += weights[i];
            }
        }
        return wsum > 0.0f ? (byte)Math.Clamp(sum / wsum, 0.0f, 255.0f) : (byte)0;
    }

    /// <summary>
    /// Bicubic interpolation of a single lightmap color channel.
    /// </summary>
    private static byte BicubicSampleChannel(TerrainData src, float fx, float fy, int channel)
    {
        int ix = (int)MathF.Floor(fx);
        int iy = (int)MathF.Floor(fy);
        float tx = fx - ix;
        float ty = fy - iy;

        Span<float> cols = stackalloc float[4];
        for (int j = -1; j <= 2; j++)
        {
            float r0 = ClampedSampleColor(src, ix - 1, iy + j, channel);
            float r1 = ClampedSampleColor(src, ix, iy + j, channel);
            float r2 = ClampedSampleColor(src, ix + 1, iy + j, channel);
            float r3 = ClampedSampleColor(src, ix + 2, iy + j, channel);
            cols[j + 1] = CatmullRom(r0, r1, r2, r3, tx);
        }
        float result = CatmullRom(cols[0], cols[1], cols[2], cols[3], ty);
        return (byte)Math.Clamp(result, 0.0f, 255.0f);
    }

    /// <summary>
    /// Samples a lightmap color channel at fractional source coordinates,
    /// choosing interpolation method based on proximity to empty edges.
    /// </summary>
    private static byte SampleChannel(TerrainData src, float fx, float fy, int channel)
    {
        int ix = (int)MathF.Floor(fx);
        int iy = (int)MathF.Floor(fy);

        if (!IsFullyTerrain4x4(src, ix, iy))
            return BilinearSampleChannel(src, fx, fy, channel);

        return BicubicSampleChannel(src, fx, fy, channel);
    }

    #endregion

    #region Connectivity

    /// <summary>
    /// Triangle connectivity type for a terrain cell, describing which
    /// triangles are present based on the cell's four corners.
    /// </summary>
    private enum CellType : byte
    {
        None = 0x00,
        TopLeft = 0x01,
        BottomRight = 0x02,
        TopRight = 0x03,
        BottomLeft = 0x04,
        BottomLeftTopRight = 0x05,
    }

    /// <summary>
    /// Determines triangle connectivity for a subdivided cell based on
    /// which of its 4 corners are terrain.
    /// </summary>
    private static byte DetermineConnType(
        bool topLeft, bool topRight, bool bottomLeft, bool bottomRight)
    {
        int count = (topLeft ? 1 : 0) + (topRight ? 1 : 0) +
                    (bottomLeft ? 1 : 0) + (bottomRight ? 1 : 0);
        if (count == 4) return (byte)CellType.BottomLeftTopRight;

        if (count == 3)
        {
            if (!bottomRight) return (byte)CellType.TopLeft;
            if (!topLeft) return (byte)CellType.BottomRight;
            if (!bottomLeft) return (byte)CellType.TopRight;
            return (byte)CellType.BottomLeft;
        }

        return (byte)CellType.None;
    }

    #endregion
}
