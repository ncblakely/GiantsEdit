namespace GiantsEdit.Core.Formats;

/// <summary>
/// Parses uncompressed TGA image files (8/24/32 bit).
/// Converts BGR→RGB and flips vertically to match OpenGL bottom-up layout.
/// </summary>
public static class TgaLoader
{
    public static TgaImage Load(byte[] data)
    {
        if (data.Length < 18)
            throw new InvalidDataException("TGA file too small");

        // TGA header: 18 bytes
        // Byte 2: image type (2=uncompressed true-color, 3=uncompressed grayscale)
        // Words at offset 12,14: width, height
        // Byte 16: bits per pixel
        int width = BitConverter.ToUInt16(data, 12);
        int height = BitConverter.ToUInt16(data, 14);
        int bpp = data[16];
        int channels = bpp / 8;

        if (channels is not (1 or 3 or 4))
            throw new InvalidDataException($"Unsupported TGA bpp: {bpp}");

        int pixelDataStart = 18;
        int pixelCount = width * height;
        var pixels = new byte[pixelCount * channels];

        // Flip vertically (TGA is top-down by default, OpenGL expects bottom-up)
        // and swap BGR → RGB for 24/32 bit
        for (int y = 0; y < height; y++)
        {
            int srcRow = y;
            int dstRow = height - 1 - y;

            for (int x = 0; x < width; x++)
            {
                int srcIdx = pixelDataStart + (srcRow * width + x) * channels;
                int dstIdx = (dstRow * width + x) * channels;

                switch (channels)
                {
                    case 1:
                        pixels[dstIdx] = data[srcIdx];
                        break;
                    case 3:
                        pixels[dstIdx + 0] = data[srcIdx + 2]; // R
                        pixels[dstIdx + 1] = data[srcIdx + 1]; // G
                        pixels[dstIdx + 2] = data[srcIdx + 0]; // B
                        break;
                    case 4:
                        pixels[dstIdx + 0] = data[srcIdx + 2]; // R
                        pixels[dstIdx + 1] = data[srcIdx + 1]; // G
                        pixels[dstIdx + 2] = data[srcIdx + 0]; // B
                        pixels[dstIdx + 3] = data[srcIdx + 3]; // A
                        break;
                }
            }
        }

        return new TgaImage
        {
            Width = width,
            Height = height,
            Channels = channels,
            Pixels = pixels,
            HasAlpha = channels == 4
        };
    }
}

public class TgaImage
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int Channels { get; init; }
    public required byte[] Pixels { get; init; }
    public bool HasAlpha { get; init; }
}
