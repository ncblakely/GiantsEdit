namespace GiantsEdit.Core.DataModel;

/// <summary>
/// Bit flags for tree node/leaf state.
/// </summary>
[Flags]
public enum TreeState
{
    None = 0,
    Visible = 1,
    SubNodesSortNumeric = 2,
    SubNodesSortAlpha = 4,
    AllowEditName = 8,
}
