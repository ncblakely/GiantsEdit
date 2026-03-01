using Avalonia.Controls;
using Avalonia.Platform.Storage;
using GiantsEdit.Core.Services;

namespace GiantsEdit.App.Dialogs;

public partial class PreferencesDialog : Window
{
    public string GamePath { get; private set; } = "";
    public ControlScheme ControlScheme { get; private set; } = ControlScheme.Default;
    public string ThemeName { get; private set; } = "Light";
    public bool Confirmed { get; private set; }

    public PreferencesDialog()
    {
        InitializeComponent();

        CmbControlScheme.ItemsSource = new[] { "Default (UE5-style)", "Classic (original)" };
        CmbControlScheme.SelectedIndex = 0;

        CmbTheme.ItemsSource = new[] { "Light", "Dark" };
        CmbTheme.SelectedIndex = 0;

        BtnBrowse.Click += async (_, _) =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Giants: Citizen Kabuto installation folder",
                AllowMultiple = false
            });

            if (folders.Count > 0)
                TxtGamePath.Text = folders[0].Path.LocalPath;
        };

        BtnOk.Click += (_, _) =>
        {
            GamePath = TxtGamePath.Text ?? "";
            ControlScheme = CmbControlScheme.SelectedIndex == 1 ? ControlScheme.Classic : ControlScheme.Default;
            ThemeName = CmbTheme.SelectedIndex == 1 ? "Dark" : "Light";
            Confirmed = true;
            Close();
        };

        BtnCancel.Click += (_, _) => Close();
    }

    public void SetInitialValues(string gamePath, ControlScheme scheme, string theme)
    {
        TxtGamePath.Text = gamePath;
        CmbControlScheme.SelectedIndex = scheme == ControlScheme.Classic ? 1 : 0;
        CmbTheme.SelectedIndex = theme == "Dark" ? 1 : 0;
    }
}
