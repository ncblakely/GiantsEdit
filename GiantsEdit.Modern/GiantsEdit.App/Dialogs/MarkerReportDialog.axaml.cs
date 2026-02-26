using System.Text;
using Avalonia.Controls;

namespace GiantsEdit.App.Dialogs;

public partial class MarkerReportDialog : Window
{
    public MarkerReportDialog()
    {
        InitializeComponent();
        BtnClose.Click += (_, _) => Close();
    }

    /// <summary>
    /// Populates the report with marker data.
    /// Each marker has AIMode and TeamID (-1 means not present).
    /// </summary>
    public void SetMarkers(List<(int AIMode, int TeamID)> markers)
    {
        if (markers.Count == 0)
        {
            TxtReport.Text = "No markers found.";
            return;
        }

        var sb = new StringBuilder();
        for (int i = 0; i < markers.Count; i++)
        {
            var (aiMode, teamId) = markers[i];
            var line = new StringBuilder();
            if (aiMode >= 0)
                line.Append($"AIMode={aiMode}  ");
            if (teamId >= 0)
                line.Append($"TeamID={teamId}  ");
            if (line.Length == 0)
                line.Append("(no properties)");
            sb.AppendLine(line.ToString().TrimEnd());
        }
        TxtReport.Text = sb.ToString();
    }
}
