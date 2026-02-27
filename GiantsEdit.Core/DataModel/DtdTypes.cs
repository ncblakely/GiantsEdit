namespace GiantsEdit.Core.DataModel;

/// <summary>
/// Cardinality constraint for DTD entries.
/// Ported from Delphi's tDTDCount.
/// </summary>
public enum DtdCount
{
    Any,
    Once,
    Optional,
    Multiple
}

/// <summary>
/// Basic leaf data types recognized by the DTD.
/// Ported from Delphi's tBasicType.
/// </summary>
public enum DtdBasicType
{
    Invalid,
    Byte,
    Int32,
    Single,
    String,
    Void
}

/// <summary>
/// A reference to a sub-node within a DTD node definition.
/// Ported from Delphi's tDTDSubNode.
/// </summary>
public class DtdSubNode
{
    /// <summary>The DTD node type name (used for linking).</summary>
    public string NodeTypeName { get; set; } = string.Empty;

    /// <summary>The display/context name for this sub-node.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Cardinality constraint.</summary>
    public DtdCount Count { get; set; }

    /// <summary>Resolved reference to the DTD node definition. Set during linking.</summary>
    public DtdNode? Node { get; set; }
}

/// <summary>
/// A reference to a sub-leaf within a DTD node definition.
/// Ported from Delphi's tDTDSubLeaf.
/// </summary>
public class DtdSubLeaf
{
    /// <summary>The DTD type name (e.g., "byte", "int32", "string").</summary>
    public string LeafTypeName { get; set; } = string.Empty;

    /// <summary>The display/context name for this sub-leaf.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Cardinality constraint.</summary>
    public DtdCount Count { get; set; }

    /// <summary>Resolved basic type. Set during linking.</summary>
    public DtdBasicType BasicType { get; set; } = DtdBasicType.Invalid;
}

/// <summary>
/// A node definition in the DTD schema.
/// Ported from Delphi's tDTDNode.
/// </summary>
public class DtdNode
{
    public string Name { get; set; } = string.Empty;
    public List<DtdSubNode> SubNodes { get; } = [];
    public List<DtdSubLeaf> SubLeaves { get; } = [];
}
