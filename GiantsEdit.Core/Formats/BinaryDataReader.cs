namespace GiantsEdit.Core.Formats;

/// <summary>
/// Low-level binary reading helpers for Giants game file formats.
/// Wraps a byte array with a sequential read pointer, matching the
/// original Delphi data/dataptr pattern in bin_w_read.pas.
/// All multi-byte values are little-endian.
/// </summary>
public class BinaryDataReader
{
    private readonly byte[] _data;
    private int _pos;

    public BinaryDataReader(byte[] data)
    {
        _data = data;
        _pos = 0;
    }

    public int Position
    {
        get => _pos;
        set => _pos = value;
    }

    public int Length => _data.Length;
    public bool HasMore => _pos < _data.Length;

    private void EnsureAvailable(int count)
    {
        if (_pos + count > _data.Length)
            throw new InvalidDataException(
                $"Attempted to read {count} byte(s) at position {_pos}, but only {_data.Length - _pos} byte(s) remain (total length: {_data.Length}).");
    }

    public byte ReadByte()
    {
        EnsureAvailable(1);
        return _data[_pos++];
    }

    public int ReadInt32()
    {
        EnsureAvailable(4);
        int value = BitConverter.ToInt32(_data, _pos);
        _pos += 4;
        return value;
    }

    public float ReadSingle()
    {
        EnsureAvailable(4);
        float value = BitConverter.ToSingle(_data, _pos);
        _pos += 4;
        return value;
    }

    public ushort ReadWord()
    {
        EnsureAvailable(2);
        ushort value = BitConverter.ToUInt16(_data, _pos);
        _pos += 2;
        return value;
    }

    public uint ReadUInt32()
    {
        EnsureAvailable(4);
        uint value = BitConverter.ToUInt32(_data, _pos);
        _pos += 4;
        return value;
    }

    /// <summary>
    /// Reads a fixed-length string (null-padded). Advances by exactly <paramref name="length"/> bytes.
    /// </summary>
    public string ReadFixedString(int length)
    {
        EnsureAvailable(length);
        int end = _pos + length;
        int nullIdx = Array.IndexOf(_data, (byte)0, _pos, length);
        int strLen = nullIdx >= 0 ? nullIdx - _pos : length;
        string s = System.Text.Encoding.ASCII.GetString(_data, _pos, strLen);
        _pos = end;
        return s;
    }

    public string ReadString16() => ReadFixedString(16);
    public string ReadString32() => ReadFixedString(32);
    public string ReadString64() => ReadFixedString(64);

    /// <summary>
    /// Reads a null-terminated string (variable length). Advances past the null byte.
    /// Equivalent to Delphi's ReadPChar.
    /// </summary>
    public string ReadPChar()
    {
        int start = _pos;
        while (_pos < _data.Length && _data[_pos] != 0)
            _pos++;
        string s = System.Text.Encoding.ASCII.GetString(_data, start, _pos - start);
        if (_pos < _data.Length) _pos++; // skip null terminator
        return s;
    }

    /// <summary>
    /// Reads a BLString: skips 1 byte, then reads null-terminated string.
    /// Equivalent to Delphi's ReadBLString.
    /// </summary>
    public string ReadBLString()
    {
        EnsureAvailable(1);
        _pos++; // skip length byte
        return ReadPChar();
    }

    /// <summary>
    /// Reads 3 bytes as RGB.
    /// </summary>
    public (byte R, byte G, byte B) ReadRgb()
    {
        EnsureAvailable(3);
        byte r = _data[_pos++];
        byte g = _data[_pos++];
        byte b = _data[_pos++];
        return (r, g, b);
    }

    public byte[] ReadBytes(int count)
    {
        EnsureAvailable(count);
        var result = new byte[count];
        Array.Copy(_data, _pos, result, 0, count);
        _pos += count;
        return result;
    }

    public void Skip(int count)
    {
        EnsureAvailable(count);
        _pos += count;
    }
}
