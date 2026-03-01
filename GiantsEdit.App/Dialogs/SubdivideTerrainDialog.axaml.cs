using Avalonia.Controls;

namespace GiantsEdit.App.Dialogs;

public partial class SubdivideTerrainDialog : Window
{
    private readonly int _sourceWidth;
    private readonly int _sourceHeight;

    public SubdivideTerrainDialog()
    {
        InitializeComponent();
    }

    public SubdivideTerrainDialog(int sourceWidth, int sourceHeight) : this()
    {
        _sourceWidth = sourceWidth;
        _sourceHeight = sourceHeight;

        FactorInput.ValueChanged += (_, _) => UpdatePreview();
        UpdatePreview();

        BtnOk.Click += (_, _) => Close((int)(FactorInput.Value ?? 2));
        BtnCancel.Click += (_, _) => Close(null);
    }

    private void UpdatePreview()
    {
        int factor = (int)(FactorInput.Value ?? 2);
        int newW = (_sourceWidth - 1) * factor + 1;
        int newH = (_sourceHeight - 1) * factor + 1;
        ResultPreview.Text = $"{newW} × {newH} ({newW * newH:N0} cells)";

        const int warnThreshold = 2_000_000;
        if (newW * newH > warnThreshold)
        {
            WarningText.Text = "Large terrain — subdivision may take a moment and use significant memory.";
            WarningText.IsVisible = true;
        }
        else
        {
            WarningText.IsVisible = false;
        }
    }
}
