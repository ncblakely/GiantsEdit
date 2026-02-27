using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace GiantsEdit.App.Dialogs;

public partial class PreferencesDialog : Window
{
    public string GamePath { get; private set; } = "";
    public bool Confirmed { get; private set; }

    public PreferencesDialog()
    {
        InitializeComponent();

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
            Confirmed = true;
            Close();
        };

        BtnCancel.Click += (_, _) => Close();
    }

    public void SetInitialGamePath(string path)
    {
        TxtGamePath.Text = path;
    }
}
