using GiantsEdit.Core.Formats;
using Silk.NET.OpenGL;

namespace GiantsEdit.App.Rendering;

/// <summary>
/// Static utility for uploading textures to the GPU.
/// </summary>
internal static class TextureUploader
{
    private const float NormalMapScale = 1.0f / 64.0f;

    /// <summary>
    /// Uploads a TGA image as a mipmapped GL texture.
    /// </summary>
    public static unsafe uint UploadTgaTexture(GL gl, TgaImage? img)
    {
        if (img == null) return 0;

        uint tex = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, tex);
        SetTexParam(gl, TextureParameterName.TextureWrapS, (int)GLEnum.Repeat);
        SetTexParam(gl, TextureParameterName.TextureWrapT, (int)GLEnum.Repeat);
        SetTexParam(gl, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        SetTexParam(gl, TextureParameterName.TextureMinFilter, (int)GLEnum.LinearMipmapLinear);

        var (format, pixelFormat) = GetImageFormats(img.Channels);

        fixed (byte* px = img.Pixels)
            gl.TexImage2D(TextureTarget.Texture2D, 0, format,
                (uint)img.Width, (uint)img.Height, 0,
                pixelFormat, PixelType.UnsignedByte, px);

        gl.GenerateMipmap(TextureTarget.Texture2D);
        return tex;
    }

    /// <summary>
    /// Uploads a terrain texture with manually generated mipmaps that fade to black at higher levels.
    /// </summary>
    public static unsafe uint UploadTerrainTexWithFalloff(GL gl, TgaImage? img, float c0, float c1, float c2)
    {
        if (img == null) return 0;

        uint tex = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, tex);
        SetTexParam(gl, TextureParameterName.TextureWrapS, (int)GLEnum.Repeat);
        SetTexParam(gl, TextureParameterName.TextureWrapT, (int)GLEnum.Repeat);
        SetTexParam(gl, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        SetTexParam(gl, TextureParameterName.TextureMinFilter, (int)GLEnum.LinearMipmapLinear);

        int ch = img.Channels;
        var (format, pixelFmt) = GetImageFormats(ch);

        int mipW = img.Width, mipH = img.Height;
        int maxLevels = 1 + (int)MathF.Floor(MathF.Log2(MathF.Max(mipW, mipH)));
        SetTexParam(gl, TextureParameterName.TextureMaxLevel, maxLevels - 1);

        fixed (byte* px = img.Pixels)
            gl.TexImage2D(TextureTarget.Texture2D, 0, format, (uint)mipW, (uint)mipH, 0, pixelFmt, PixelType.UnsignedByte, px);

        byte[] src = img.Pixels;
        int srcW = mipW, srcH = mipH;
        float mapIndex = 0f;

        for (int level = 1; level < maxLevels; level++)
        {
            int newW = Math.Max(1, srcW / 2);
            int newH = Math.Max(1, srcH / 2);

            mapIndex += 1f;
            float brightness = Math.Clamp(c0 + mapIndex * c1 + mapIndex * mapIndex * c2, 0f, 1f);

            byte[] mip = DownscaleWithBrightness(src, srcW, srcH, ch, newW, newH, brightness);

            fixed (byte* px = mip)
                gl.TexImage2D(TextureTarget.Texture2D, level, format, (uint)newW, (uint)newH, 0, pixelFmt, PixelType.UnsignedByte, px);

            src = mip;
            srcW = newW;
            srcH = newH;
        }

        return tex;
    }

    /// <summary>
    /// Converts a diffuse image to a normal map and uploads it as a texture.
    /// </summary>
    public static unsafe uint UploadBumpTexture(GL gl, TgaImage? img)
    {
        if (img == null) return 0;

        byte[] normalMapData = ConvertDiffuseToNormalMap(img.Pixels, img.Width, img.Height, img.Channels);

        uint tex = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, tex);
        SetTexParam(gl, TextureParameterName.TextureWrapS, (int)GLEnum.Repeat);
        SetTexParam(gl, TextureParameterName.TextureWrapT, (int)GLEnum.Repeat);
        SetTexParam(gl, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        SetTexParam(gl, TextureParameterName.TextureMinFilter, (int)GLEnum.LinearMipmapLinear);

        fixed (byte* px = normalMapData)
            gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba,
                (uint)img.Width, (uint)img.Height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, px);

        gl.GenerateMipmap(TextureTarget.Texture2D);
        return tex;
    }

    /// <summary>
    /// Uploads a model part texture (TGA image) and returns the GL texture handle.
    /// </summary>
    public static unsafe uint UploadModelPartTexture(GL gl, TgaImage img)
    {
        uint tex = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, tex);
        SetTexParam(gl, TextureParameterName.TextureWrapS, (int)GLEnum.Repeat);
        SetTexParam(gl, TextureParameterName.TextureWrapT, (int)GLEnum.Repeat);
        SetTexParam(gl, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        SetTexParam(gl, TextureParameterName.TextureMinFilter, (int)GLEnum.LinearMipmapLinear);

        var (format, pixelFormat) = GetImageFormats(img.Channels);

        fixed (byte* px = img.Pixels)
        {
            gl.TexImage2D(TextureTarget.Texture2D, 0, format,
                (uint)img.Width, (uint)img.Height, 0,
                pixelFormat, PixelType.UnsignedByte, px);
        }

        gl.GenerateMipmap(TextureTarget.Texture2D);
        return tex;
    }

    private static void SetTexParam(GL gl, TextureParameterName pname, int value)
    {
        gl.TexParameterI(TextureTarget.Texture2D, pname, in value);
    }

    private static (InternalFormat format, PixelFormat pixelFormat) GetImageFormats(int channels)
    {
        var format = channels switch
        {
            1 => InternalFormat.R8,
            3 => InternalFormat.Rgb,
            4 => InternalFormat.Rgba,
            _ => InternalFormat.Rgb
        };
        var pixelFormat = channels switch
        {
            1 => PixelFormat.Red,
            3 => PixelFormat.Rgb,
            4 => PixelFormat.Rgba,
            _ => PixelFormat.Rgb
        };
        return (format, pixelFormat);
    }

    /// <summary>
    /// Converts a diffuse texture to a normal map:
    /// 1. RGB → height via screen-blend formula (colorspace)
    /// 2. Height → normal map via finite differences with scale factor
    /// </summary>
    private static byte[] ConvertDiffuseToNormalMap(byte[] pixels, int width, int height, int channels)
    {
        var heights = new byte[width * height];
        for (int j = 0; j < height; j++)
        {
            for (int i = 0; i < width; i++)
            {
                int idx = (j * width + i) * channels;
                float fr = pixels[idx] / 255.0f;
                float fg = pixels[idx + 1] / 255.0f;
                float fb = (channels >= 3) ? pixels[idx + 2] / 255.0f : 0f;

                float fa = 1.0f - (1.0f - fr) * (1.0f - fg) * (1.0f - fb);
                heights[j * width + i] = (byte)Math.Min(255, (int)(fa * 255));
            }
        }

        var result = new byte[width * height * 4];
        for (int j = 0; j < height; j++)
        {
            for (int i = 0; i < width; i++)
            {
                float h0 = heights[j * width + i];
                float h1 = heights[j * width + Math.Min(i + 1, width - 1)];
                float h2 = heights[Math.Min(j + 1, height - 1) * width + i];

                float nx = -NormalMapScale * (h1 - h0);
                float ny = 1.0f;
                float nz = -NormalMapScale * (h2 - h0);

                float len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                if (len > 0)
                {
                    nx /= len;
                    ny /= len;
                    nz /= len;
                }

                int outIdx = (j * width + i) * 4;
                result[outIdx] = (byte)Math.Min(255, (int)((nx + 1.0f) * 127.5f));
                result[outIdx + 1] = (byte)Math.Min(255, (int)((ny + 1.0f) * 127.5f));
                result[outIdx + 2] = (byte)Math.Min(255, (int)((nz + 1.0f) * 127.5f));
                result[outIdx + 3] = heights[j * width + i];
            }
        }

        return result;
    }

    /// <summary>
    /// Downscales an image by 2x using box filter, then applies a brightness multiplier to RGB channels.
    /// </summary>
    private static byte[] DownscaleWithBrightness(byte[] src, int srcW, int srcH, int ch, int dstW, int dstH, float brightness)
    {
        var dst = new byte[dstW * dstH * ch];
        for (int y = 0; y < dstH; y++)
        {
            for (int x = 0; x < dstW; x++)
            {
                int sx = x * 2, sy = y * 2;
                int sx1 = Math.Min(sx + 1, srcW - 1);
                int sy1 = Math.Min(sy + 1, srcH - 1);

                for (int c = 0; c < ch; c++)
                {
                    bool isAlpha = (ch == 4 && c == 3);
                    int sum = src[(sy * srcW + sx) * ch + c]
                            + src[(sy * srcW + sx1) * ch + c]
                            + src[(sy1 * srcW + sx) * ch + c]
                            + src[(sy1 * srcW + sx1) * ch + c];
                    float avg = sum / 4f;
                    if (!isAlpha) avg *= brightness;
                    dst[(y * dstW + x) * ch + c] = (byte)Math.Clamp((int)avg, 0, 255);
                }
            }
        }
        return dst;
    }
}
