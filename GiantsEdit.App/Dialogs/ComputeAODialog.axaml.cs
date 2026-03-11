using Avalonia.Controls;

namespace GiantsEdit.App.Dialogs;

public record AmbientOcclusionSettings(int Directions, int Radius);

public partial class ComputeAODialog : Window
{
    public ComputeAODialog()
    {
        InitializeComponent();
    }

    public ComputeAODialog(bool hasExistingAO) : this()
    {
        if (hasExistingAO)
            StatusText.Text = "Existing AO data will be replaced.";

        BtnCompute.Click += (_, _) =>
        {
            int directions = (int)(DirectionsInput.Value ?? 64);
            int radius = (int)(RadiusInput.Value ?? 100);
            Close(new AmbientOcclusionSettings(directions, radius));
        };
        BtnCancel.Click += (_, _) => Close(null);
    }
}
