using Avalonia.Controls;

namespace GiantsEdit.App.Dialogs;

public partial class InputDialog : Window
{
    public InputDialog()
    {
        InitializeComponent();
    }

    public InputDialog(string title, string prompt) : this()
    {
        Title = title;
        PromptText.Text = prompt;

        BtnOk.Click += (_, _) =>
        {
            Close(InputBox.Text?.Trim());
        };

        BtnCancel.Click += (_, _) =>
        {
            Close(null);
        };
    }
}
