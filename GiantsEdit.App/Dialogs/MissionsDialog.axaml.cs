using Avalonia.Controls;
using GiantsEdit.Core.Services;

namespace GiantsEdit.App.Dialogs;

public partial class MissionsDialog : Window
{
    private readonly WorldDocument _doc;

    public MissionsDialog(WorldDocument doc)
    {
        InitializeComponent();
        _doc = doc;

        BtnNew.Click += (_, _) => AddMission();
        BtnDelete.Click += (_, _) => DeleteMission();
        BtnClose.Click += (_, _) => Close();

        RefreshList();
    }

    private void RefreshList()
    {
        var items = new List<string> { "- none -" };
        foreach (var m in _doc.Missions)
            items.Add(m.Name);
        MissionList.ItemsSource = items;
        MissionList.SelectedIndex = 0;
    }

    private void AddMission()
    {
        _doc.AddMission();
        RefreshList();
        MissionList.SelectedIndex = MissionList.ItemCount - 1;
    }

    private void DeleteMission()
    {
        int idx = MissionList.SelectedIndex;
        if (idx < 1) return; // Can't delete "- none -"
        _doc.RemoveMission(idx - 1);
        RefreshList();
    }
}
