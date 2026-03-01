using System.Text;

namespace GiantsEdit.Core.Formats;

/// <summary>
/// Little-endian binary writer with game-specific string helpers.
/// Extends <see cref="BinaryWriter"/> over a <see cref="MemoryStream"/>.
/// </summary>
public class BinaryDataWriter : BinaryWriter
{
    public BinaryDataWriter(int initialCapacity = 65536)
        : base(new MemoryStream(initialCapacity), Encoding.ASCII, leaveOpen: false) { }

    public int Position
    {
        get => (int)BaseStream.Position;
        set => BaseStream.Position = value;
    }

    public byte[] ToArray() => ((MemoryStream)BaseStream).ToArray();

    public void WriteByte(byte value) => Write(value);

    public void WriteInt32(int value) => Write(value);

    public void WriteSingle(float value) => Write(value);

    public void WriteWord(ushort value) => Write(value);

    /// <summary>
    /// Writes a null-terminated string.
    /// </summary>
    public void WriteString0(string s)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(s);
        Write(bytes);
        Write((byte)0);
    }

    /// <summary>
    /// Writes a length byte + string + null terminator.
    /// </summary>
    public void WriteLString0(string s)
    {
        WriteByte((byte)(s.Length + 1));
        WriteString0(s);
    }

    /// <summary>
    /// Writes a fixed-length null-padded string.
    /// </summary>
    public void WriteFixedString(string s, int length)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(s);
        int copyLen = Math.Min(bytes.Length, length);
        Write(bytes, 0, copyLen);
        for (int i = copyLen; i < length; i++)
            Write((byte)0);
    }

    public void WriteString16(string s) => WriteFixedString(s, 16);
    public void WriteString32(string s) => WriteFixedString(s, 32);
    public void WriteString64(string s) => WriteFixedString(s, 64);

    public void WriteRgb(byte r, byte g, byte b)
    {
        Write(r);
        Write(g);
        Write(b);
    }

    public void WriteBytes(byte[] bytes) => Write(bytes);

    /// <summary>
    /// Overwrites an int32 at a specific position without advancing the current position.
    /// Used for backpatching header pointers.
    /// </summary>
    public void PatchInt32(int position, int value)
    {
        long saved = BaseStream.Position;
        BaseStream.Position = position;
        Write(value);
        BaseStream.Position = saved;
    }
}
