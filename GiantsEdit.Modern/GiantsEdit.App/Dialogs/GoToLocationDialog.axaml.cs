using System.Numerics;
using Avalonia.Controls;

namespace GiantsEdit.App.Dialogs;

public partial class GoToLocationDialog : Window
{
    public Vector3 Target { get; private set; }
    public bool Confirmed { get; private set; }

    public GoToLocationDialog()
    {
        InitializeComponent();

        BtnOk.Click += (_, _) =>
        {
            if (float.TryParse(TxtX.Text, out float x) &&
                float.TryParse(TxtY.Text, out float y) &&
                float.TryParse(TxtZ.Text, out float z))
            {
                Target = new Vector3(x, y, z);
                Confirmed = true;
                Close();
            }
        };

        BtnCancel.Click += (_, _) => Close();
    }
}
