using System.Text;

namespace GiantsEdit.Core.Formats;

/// <summary>
/// Little-endian binary reader with game-specific string helpers.
/// Extends <see cref="BinaryReader"/> over a <see cref="MemoryStream"/>.
/// </summary>
public class BinaryDataReader : BinaryReader
{
    public BinaryDataReader(byte[] data)
        : base(new MemoryStream(data, writable: false), Encoding.ASCII, leaveOpen: false) { }

    public int Position
    {
        get => (int)BaseStream.Position;
        set => BaseStream.Position = value;
    }

    public int Length => (int)BaseStream.Length;
    public bool HasMore => BaseStream.Position < BaseStream.Length;

    public ushort ReadWord() => ReadUInt16();

    /// <summary>
    /// Reads a fixed-length null-padded string. Advances by exactly <paramref name="length"/> bytes.
    /// </summary>
    public string ReadFixedString(int length)
    {
        byte[] bytes = ReadBytes(length);
        int nullIdx = Array.IndexOf(bytes, (byte)0);
        int strLen = nullIdx >= 0 ? nullIdx : length;
        return Encoding.ASCII.GetString(bytes, 0, strLen);
    }

    public string ReadString16() => ReadFixedString(16);
    public string ReadString32() => ReadFixedString(32);
    public string ReadString64() => ReadFixedString(64);

    /// <summary>
    /// Reads a null-terminated string (variable length). Advances past the null byte.
    /// </summary>
    public string ReadPChar()
    {
        var sb = new StringBuilder();
        while (HasMore)
        {
            byte b = ReadByte();
            if (b == 0) break;
            sb.Append((char)b);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Reads a BLString: skips 1 byte, then reads null-terminated string.
    /// </summary>
    public string ReadBLString()
    {
        ReadByte(); // skip length byte
        return ReadPChar();
    }

    /// <summary>
    /// Reads 3 bytes as RGB.
    /// </summary>
    public (byte R, byte G, byte B) ReadRgb()
    {
        byte r = ReadByte();
        byte g = ReadByte();
        byte b = ReadByte();
        return (r, g, b);
    }

    public void Skip(int count) => BaseStream.Position += count;
}
