using GiantsEdit.App.Dialogs;
using GiantsEdit.Core.Formats;
using GiantsEdit.Core.Services;

namespace GiantsEdit.App;

public partial class MainWindow
{
    #region Map menu handlers

    private async Task ShowMapNamesAsync()
    {
        var dlg = new MapNamesDialog();
        dlg.SetValues(
            _vm.Document.MapBinName,
            _vm.Document.UserMessage,
            _vm.Document.MapType);
        await dlg.ShowDialog(this);

        _vm.Document.MapBinName = dlg.BinFileName;
        _vm.Document.UserMessage = dlg.Message;
        _vm.Document.MapType = dlg.MapType;
        StatusText.Text = $"Map name set to: {dlg.BinFileName}";
    }

    private async Task ShowMarkerReportAsync()
    {
        var markers = _vm.Document.GetMarkers();
        var dlg = new MarkerReportDialog();
        dlg.SetMarkers(markers);
        await dlg.ShowDialog(this);
    }

    private void ShowWorldObjectsTree()
    {
        if (_vm.Document.WorldRoot == null)
        {
            StatusText.Text = "No map loaded";
            return;
        }

        var win = new DataTreeWindow();
        win.LoadTree(_vm.Document.WorldRoot, "Map Objects Tree View");
        win.NodeSelected += node =>
        {
            // If in object editing mode and an Object node is selected, select it in the viewport
            if (_vm.Document.CurrentMode == EditMode.ObjectEdit && node.Name == BinFormatConstants.NodeObject)
            {
                SelectObject(node);
                InvalidateViewport();
            }
        };
        win.Closed += (_, _) =>
        {
            UploadTerrainToGpu();
            LoadDomeFromGameData();
        };
        win.Show(this);
    }

    private void ShowMissionObjectsTree()
    {
        if (_vm.Document.Missions.Count == 0)
        {
            StatusText.Text = "No missions loaded — go to Missions and select one first";
            return;
        }

        // Show the first (active) mission tree
        var win = new DataTreeWindow();
        win.LoadTree(_vm.Document.Missions[0], "Mission Objects Tree View");
        win.Show(this);
    }

    private void CloseOwnedWindows()
    {
        foreach (var w in OwnedWindows.ToList())
            w.Close();
    }

    private void PlaceAllObjectsOnSurface()
    {
        var terrain = _vm.Document.Terrain;
        var root = _vm.Document.WorldRoot;
        if (terrain == null || root == null) return;

        foreach (var obj in root.EnumerateNodes())
        {
            if (obj.Name != BinFormatConstants.NodeObject) continue;

            var xLeaf = obj.FindChildLeaf("X");
            var yLeaf = obj.FindChildLeaf("Y");
            var zLeaf = obj.FindChildLeaf("Z");
            if (xLeaf == null || yLeaf == null || zLeaf == null) continue;

            float wx = xLeaf.SingleValue;
            float wy = yLeaf.SingleValue;

            // Find terrain height at this position
            float tx = (wx - terrain.Header.XOffset) / terrain.Header.Stretch;
            float ty = (wy - terrain.Header.YOffset) / terrain.Header.Stretch;
            int ix = Math.Clamp((int)tx, 0, terrain.Width - 1);
            int iy = Math.Clamp((int)ty, 0, terrain.Height - 1);
            float height = terrain.Heights[iy * terrain.Width + ix];

            zLeaf.SetSingle(height);
        }

        InvalidateViewport();
    }

    #endregion
}
