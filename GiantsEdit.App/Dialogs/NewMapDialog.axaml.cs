using Avalonia.Controls;
using GiantsEdit.Core.Formats;

namespace GiantsEdit.App.Dialogs;

public partial class NewMapDialog : Window
{
    public string MapName { get; private set; } = string.Empty;
    public int GameMapType { get; private set; }
    public int MapWidth { get; private set; } = 256;
    public int MapHeight { get; private set; } = 256;
    public MapFillType FillType { get; private set; } = MapFillType.Filled;
    public bool Confirmed { get; private set; }

    // Map type flag values matching the game engine
    private static readonly int[] MapTypeValues = [0, 0x31, 0x0D, 0x0B];

    public NewMapDialog()
    {
        InitializeComponent();

        BtnOk.Click += (_, _) =>
        {
            MapName = TxtMapName.Text?.Trim() ?? string.Empty;
            int typeIdx = CmbMapType.SelectedIndex >= 0 ? CmbMapType.SelectedIndex : 0;
            GameMapType = typeIdx < MapTypeValues.Length ? MapTypeValues[typeIdx] : 0;
            MapWidth = (int)(NumWidth.Value ?? 256);
            MapHeight = (int)(NumHeight.Value ?? 256);
            FillType = (MapFillType)(CmbFillType.SelectedIndex >= 0 ? CmbFillType.SelectedIndex : 1);
            Confirmed = true;
            Close();
        };

        BtnCancel.Click += (_, _) => Close();
    }
}
