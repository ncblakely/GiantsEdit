using Avalonia.Controls;

namespace GiantsEdit.App.Dialogs;

public partial class NewMapDialog : Window
{
    public int MapWidth { get; private set; } = 256;
    public int MapHeight { get; private set; } = 256;
    public string TextureName { get; private set; } = string.Empty;
    public bool Confirmed { get; private set; }

    public NewMapDialog()
    {
        InitializeComponent();

        BtnOk.Click += (_, _) =>
        {
            MapWidth = (int)(NumWidth.Value ?? 256);
            MapHeight = (int)(NumHeight.Value ?? 256);
            TextureName = TxtTexture.Text ?? string.Empty;
            Confirmed = true;
            Close();
        };

        BtnCancel.Click += (_, _) => Close();
    }
}
