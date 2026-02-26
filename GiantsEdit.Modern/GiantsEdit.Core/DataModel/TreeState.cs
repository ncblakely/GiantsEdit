namespace GiantsEdit.Core.DataModel;

/// <summary>
/// Constants for tree node/leaf state flags.
/// Ported from Delphi's ST_* constants.
/// </summary>
public static class TreeState
{
    public const int Visible = 1;
    public const int SubNodesSortNumeric = 2;
    public const int SubNodesSortAlpha = 4;
    public const int AllowEditName = 8;
}
