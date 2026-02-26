namespace GiantsEdit.Core.Formats;

/// <summary>
/// Describes an entry in a GCK/GZP archive.
/// Ported from Delphi's gck.pas tgckindex.
/// </summary>
public class GckIndexEntry
{
    public int BlockPosition { get; set; }
    public int BlockLength { get; set; }
    public int OriginalLength { get; set; }
    public string Name { get; set; } = string.Empty;
    public GckContentKind Kind { get; set; }
}

/// <summary>
/// Content type identifiers for GCK archive entries.
/// </summary>
public enum GckContentKind
{
    GenericData = 0,
    Readme = 1,
    IniOrGmm = 2,
    Gti = 3,
    WorldBin = 4,
    Tga = 5,
    Jpg = 6,
    JpgWithAlpha = 7,
    JpgAlphaChannel = 8,
    TextureScript = 9
}
