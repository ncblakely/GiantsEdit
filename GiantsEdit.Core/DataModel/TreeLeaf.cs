using System.Runtime.InteropServices;

namespace GiantsEdit.Core.DataModel;

/// <summary>
/// A leaf in the tree hierarchy. Stores a named, typed value.
/// Ported from Delphi's tLeaf class.
/// </summary>
/// <remarks>
/// In the original Delphi code, Single values are stored as their raw int32 bit pattern
/// via 'absolute' overlays. Here we use BitConverter for the same effect.
/// </remarks>
public class TreeLeaf
{
    public string Name { get; set; } = string.Empty;
    public PropertyType PropertyType { get; set; }
    public string Help { get; set; } = string.Empty;
    public TreeState State { get; set; }
    public TreeNode? Parent { get; internal set; }

    // Storage: int32 holds Byte/Int32/Single (bit pattern), string holds String values.
    // This mirrors the original Delphi tLeaf which used pint:longint + pstr:string.
    private int _intValue;
    private string _strValue = string.Empty;
    private int _maxLength = -1; // -1 = unlimited

    public byte ByteValue
    {
        get
        {
            AssertType(PropertyType.Byte);
            return (byte)_intValue;
        }
        set
        {
            AssertType(PropertyType.Byte);
            _intValue = value;
        }
    }

    public int Int32Value
    {
        get
        {
            AssertType(PropertyType.Int32);
            return _intValue;
        }
        set
        {
            AssertType(PropertyType.Int32);
            _intValue = value;
        }
    }

    public float SingleValue
    {
        get
        {
            AssertType(PropertyType.Single);
            return BitConverter.Int32BitsToSingle(_intValue);
        }
        set
        {
            AssertType(PropertyType.Single);
            _intValue = BitConverter.SingleToInt32Bits(value);
        }
    }

    public string StringValue
    {
        get
        {
            AssertType(PropertyType.String);
            return _strValue;
        }
        set
        {
            AssertType(PropertyType.String);
            if (_maxLength >= 0 && value.Length > _maxLength)
                throw new InvalidOperationException(
                    $"String length {value.Length} exceeds maximum {_maxLength}.");
            _strValue = value;
        }
    }

    /// <summary>
    /// Maximum string length, or -1 for unlimited.
    /// Corresponds to Delphi's pint field for string leaves.
    /// </summary>
    public int MaxLength
    {
        get => _maxLength;
        set => _maxLength = value;
    }

    /// <summary>
    /// Gets or sets the raw int32 storage directly.
    /// Useful for serialization where the bit pattern matters.
    /// </summary>
    public int RawInt32
    {
        get => _intValue;
        set => _intValue = value;
    }

    /// <summary>
    /// Sets the value regardless of type, for convenience.
    /// </summary>
    public void SetSingle(float value)
    {
        SingleValue = value;
    }

    private void AssertType(PropertyType expected)
    {
        if (PropertyType != expected)
            throw new InvalidOperationException(
                $"Expected property type {expected}, but leaf '{Name}' is {PropertyType}.");
    }
}
