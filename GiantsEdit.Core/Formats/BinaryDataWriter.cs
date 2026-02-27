namespace GiantsEdit.Core.Formats;

/// <summary>
/// Low-level binary writing helpers for Giants game file formats.
/// Builds a byte array sequentially, matching the original Delphi write pattern.
/// All multi-byte values are little-endian.
/// </summary>
public class BinaryDataWriter
{
    private byte[] _data;
    private int _pos;

    public BinaryDataWriter(int initialCapacity = 65536)
    {
        _data = new byte[initialCapacity];
        _pos = 0;
    }

    public int Position
    {
        get => _pos;
        set => _pos = value;
    }

    public byte[] ToArray()
    {
        var result = new byte[_pos];
        Array.Copy(_data, result, _pos);
        return result;
    }

    private void EnsureCapacity(int additional)
    {
        int needed = _pos + additional;
        if (needed <= _data.Length) return;
        int newSize = Math.Max(_data.Length * 2, needed);
        Array.Resize(ref _data, newSize);
    }

    public void WriteByte(byte value)
    {
        EnsureCapacity(1);
        _data[_pos++] = value;
    }

    public void WriteInt32(int value)
    {
        EnsureCapacity(4);
        BitConverter.TryWriteBytes(_data.AsSpan(_pos), value);
        _pos += 4;
    }

    public void WriteSingle(float value)
    {
        EnsureCapacity(4);
        BitConverter.TryWriteBytes(_data.AsSpan(_pos), value);
        _pos += 4;
    }

    public void WriteWord(ushort value)
    {
        EnsureCapacity(2);
        BitConverter.TryWriteBytes(_data.AsSpan(_pos), value);
        _pos += 2;
    }

    /// <summary>
    /// Writes a null-terminated string.
    /// Equivalent to Delphi's WriteString0.
    /// </summary>
    public void WriteString0(string s)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(s);
        EnsureCapacity(bytes.Length + 1);
        Array.Copy(bytes, 0, _data, _pos, bytes.Length);
        _pos += bytes.Length;
        _data[_pos++] = 0;
    }

    /// <summary>
    /// Writes a length byte + string + null terminator.
    /// Equivalent to Delphi's WriteLString0.
    /// </summary>
    public void WriteLString0(string s)
    {
        WriteByte((byte)(s.Length + 1)); // length includes null terminator
        WriteString0(s);
    }

    /// <summary>
    /// Writes a fixed-length null-padded string.
    /// </summary>
    public void WriteFixedString(string s, int length)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(s);
        EnsureCapacity(length);
        int copyLen = Math.Min(bytes.Length, length);
        Array.Copy(bytes, 0, _data, _pos, copyLen);
        // Zero-fill remainder
        for (int i = copyLen; i < length; i++)
            _data[_pos + i] = 0;
        _pos += length;
    }

    public void WriteString16(string s) => WriteFixedString(s, 16);
    public void WriteString32(string s) => WriteFixedString(s, 32);
    public void WriteString64(string s) => WriteFixedString(s, 64);

    public void WriteRgb(byte r, byte g, byte b)
    {
        WriteByte(r);
        WriteByte(g);
        WriteByte(b);
    }

    public void WriteBytes(byte[] bytes)
    {
        EnsureCapacity(bytes.Length);
        Array.Copy(bytes, 0, _data, _pos, bytes.Length);
        _pos += bytes.Length;
    }

    /// <summary>
    /// Overwrites an int32 at a specific position without advancing the current position.
    /// Used for backpatching header pointers.
    /// </summary>
    public void PatchInt32(int position, int value)
    {
        BitConverter.TryWriteBytes(_data.AsSpan(position), value);
    }
}
