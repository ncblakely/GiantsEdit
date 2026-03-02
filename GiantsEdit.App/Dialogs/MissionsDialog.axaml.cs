using Avalonia.Controls;
using Avalonia.Platform.Storage;
using GiantsEdit.Core.DataModel;
using GiantsEdit.Core.Formats;
using GiantsEdit.Core.Services;

namespace GiantsEdit.App.Dialogs;

/// <summary>
/// Result returned when the dialog closes via "Edit Properties".
/// </summary>
public record MissionEditRequest(int MissionIndex);

public partial class MissionsDialog : Window
{
    private readonly WorldDocument? _doc;

    public MissionsDialog() => InitializeComponent();

    public MissionsDialog(WorldDocument doc)
    {
        InitializeComponent();
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));

        BtnNew.Click += (_, _) => AddMission();
        MenuDelete.Click += (_, _) => DeleteMission();
        BtnClose.Click += (_, _) => Close();
        BtnImport.Click += async (_, _) => await ImportMission();
        BtnExport.Click += async (_, _) => await ExportMission();
        MenuRename.Click += async (_, _) => await RenameMission();
        MenuEditProps.Click += (_, _) => EditProperties();
        MissionList.SelectionChanged += (_, _) => UpdateDetails();
        ChkShowInViewport.IsCheckedChanged += (_, _) => ToggleViewport();

        RefreshList();
    }

    private void RefreshList()
    {
        int prevIdx = MissionList.SelectedIndex;
        var items = new List<string>();
        foreach (var m in _doc!.Missions)
            items.Add(m.Name);
        MissionList.ItemsSource = items;
        MissionList.SelectedIndex = items.Count > 0 ? Math.Clamp(prevIdx, 0, items.Count - 1) : -1;
    }

    private void UpdateDetails()
    {
        int idx = MissionList.SelectedIndex;
        if (idx < 0 || idx >= _doc!.Missions.Count)
        {
            TxtMissionName.Text = "(no selection)";
            TxtObjectCount.Text = "";
            TxtOptions.Text = "";
            ChkShowInViewport.IsEnabled = false;
            ChkShowInViewport.IsChecked = false;
            BtnExport.IsEnabled = false;
            return;
        }

        var mission = _doc.Missions[idx];
        TxtMissionName.Text = mission.Name;
        BtnExport.IsEnabled = true;

        var objects = mission.FindChildNode(BinFormatConstants.GroupObjects);
        int objCount = objects?.EnumerateNodes().Count() ?? 0;
        TxtObjectCount.Text = $"Objects: {objCount}";

        var options = mission.FindChildNode("<Options>");
        if (options != null)
        {
            var hidden = new HashSet<string> { "IconCount", "Icons" };
            var parts = new List<string>();

            foreach (var leaf in options.EnumerateLeaves())
            {
                if (hidden.Contains(leaf.Name)) continue;

                string display = leaf.Name == "Character"
                    ? $"Character: {ObjectNames.GetDisplayName(leaf.Int32Value)}"
                    : leaf.PropertyType switch
                    {
                        PropertyType.Void => leaf.Name,
                        PropertyType.String => $"{leaf.Name}: {leaf.StringValue}",
                        PropertyType.Int32 => $"{leaf.Name}: {leaf.Int32Value}",
                        PropertyType.Single => $"{leaf.Name}: {leaf.SingleValue:F2}",
                        PropertyType.Byte => $"{leaf.Name}: {leaf.ByteValue}",
                        _ => leaf.Name
                    };
                parts.Add(display);
            }

            int iconCount = options.EnumerateLeaves().Count(l => l.Name == "Icons");
            if (iconCount > 0)
                parts.Add($"Icons: {iconCount}");

            TxtOptions.Text = parts.Count > 0 ? string.Join("\n", parts) : "(no options)";
        }
        else
        {
            TxtOptions.Text = "(no options)";
        }

        ChkShowInViewport.IsEnabled = true;
        ChkShowInViewport.IsChecked = _doc.ActiveMissionIndex == idx;
    }

    private void AddMission()
    {
        _doc!.AddMission();
        RefreshList();
        MissionList.SelectedIndex = MissionList.ItemCount - 1;
    }

    private void DeleteMission()
    {
        int idx = MissionList.SelectedIndex;
        if (idx < 0) return;

        if (_doc!.ActiveMissionIndex == idx)
            _doc.ActiveMissionIndex = null;

        _doc.RemoveMission(idx);
        RefreshList();
    }

    private void EditProperties()
    {
        int idx = MissionList.SelectedIndex;
        if (idx < 0 || idx >= _doc!.Missions.Count) return;

        Close(new MissionEditRequest(idx));
    }

    private async Task RenameMission()
    {
        int idx = MissionList.SelectedIndex;
        if (idx < 0 || idx >= _doc!.Missions.Count) return;

        var mission = _doc.Missions[idx];
        var dlg = new InputDialog("Rename Mission", "New mission name:");
        var newName = await dlg.ShowDialog<string?>(this);
        if (string.IsNullOrWhiteSpace(newName)) return;

        string oldName = mission.Name;
        mission.Name = newName;

        _doc.RenameMissionInScenerios(oldName, newName);
        RefreshList();
    }

    private async Task ImportMission()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Mission .bin",
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("Mission files") { Patterns = ["*.bin"] }]
        });

        foreach (var file in files)
        {
            try
            {
                string path = file.Path.LocalPath;
                byte[] data = await File.ReadAllBytesAsync(path);
                var reader = new BinMissionReader();
                var mission = reader.Load(data);
                if (mission == null)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load mission: {path}");
                    continue;
                }

                // Name from filename: wm_mission1.bin → mission1
                string name = Path.GetFileNameWithoutExtension(path);
                if (name.StartsWith("wm_", StringComparison.OrdinalIgnoreCase))
                    name = name[3..];
                mission.Name = name;

                _doc!.ImportMission(mission);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error importing mission: {ex.Message}");
            }
        }

        RefreshList();
        MissionList.SelectedIndex = MissionList.ItemCount - 1;
    }

    private async Task ExportMission()
    {
        int idx = MissionList.SelectedIndex;
        if (idx < 0 || idx >= _doc!.Missions.Count) return;

        var mission = _doc.Missions[idx];
        string defaultName = $"wm_{mission.Name}.bin";

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Mission .bin",
            SuggestedFileName = defaultName,
            FileTypeChoices = [new FilePickerFileType("Mission files") { Patterns = ["*.bin"] }]
        });

        if (file == null) return;

        try
        {
            var writer = new BinMissionWriter();
            byte[] data = writer.Save(mission);
            await File.WriteAllBytesAsync(file.Path.LocalPath, data);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error exporting mission: {ex.Message}");
        }
    }

    private void ToggleViewport()
    {
        int idx = MissionList.SelectedIndex;
        if (idx < 0) return;

        if (ChkShowInViewport.IsChecked == true)
            _doc!.ActiveMissionIndex = idx;
        else if (_doc!.ActiveMissionIndex == idx)
            _doc.ActiveMissionIndex = null;
    }
}
