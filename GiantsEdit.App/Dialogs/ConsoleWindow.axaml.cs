using Avalonia.Controls;

namespace GiantsEdit.App.Dialogs;

public partial class ConsoleWindow : Window
{
    public ConsoleWindow()
    {
        InitializeComponent();
        BtnClear.Click += (_, _) => LogText.Text = string.Empty;
    }

    public void AppendLine(string message)
    {
        LogText.Text += message + Environment.NewLine;
        LogText.CaretIndex = LogText.Text?.Length ?? 0;
    }
}
