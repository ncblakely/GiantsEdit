using System.Globalization;
using Avalonia.Controls;
using GiantsEdit.Core.Formats;

namespace GiantsEdit.App.Dialogs;

public partial class HeightmapImportDialog : Window
{
    public float BlackHeight { get; private set; } = BmpHeightmap.DefaultBlackHeight;
    public float WhiteHeight { get; private set; } = BmpHeightmap.DefaultWhiteHeight;
    public bool Confirmed { get; private set; }

    public HeightmapImportDialog()
    {
        InitializeComponent();

        BtnOk.Click += (_, _) =>
        {
            if (float.TryParse(TxtBlackHeight.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float black) &&
                float.TryParse(TxtWhiteHeight.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float white) &&
                float.IsFinite(black) && float.IsFinite(white))
            {
                BlackHeight = black;
                WhiteHeight = white;
                Confirmed = true;
                Close();
            }
            else
            {
                ErrorText.IsVisible = true;
            }
        };

        BtnCancel.Click += (_, _) => Close();
    }
}
