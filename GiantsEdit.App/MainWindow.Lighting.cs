using GiantsEdit.App.Dialogs;
using GiantsEdit.Core.Formats;
using GiantsEdit.Core.Rendering;

namespace GiantsEdit.App;

public partial class MainWindow
{
    private HeightBasedLightingSettings _heightLightingSettings =
        HeightBasedLightingSettings.CreateDefault();

    private async Task ShowAutomaticLightingAsync()
    {
        if (_vm.Document.Terrain == null)
        {
            StatusText.Text = "No terrain loaded";
            return;
        }

        var dialog = new HeightBasedLightingDialog(
            _heightLightingSettings,
            settings => _heightLightingSettings = settings,
            ApplyAutomaticLighting);
        await dialog.ShowDialog(this);
        _heightLightingSettings = dialog.Settings;
    }

    private void ApplyAutomaticLighting(HeightBasedLightingSettings settings)
    {
        var terrain = _vm.Document.Terrain;
        if (terrain == null)
        {
            StatusText.Text = "No terrain loaded";
            return;
        }

        string? error = settings.Validate();
        if (error != null)
        {
            StatusText.Text = error;
            return;
        }

        HeightBasedLighting.Apply(terrain, settings);
        _vm.Document.NotifyTerrainChanged();
        StatusText.Text = "Automatic lighting applied";
    }

    private void ApplyAutomaticLightingAround(TerrainData terrain, float gridX, float gridY)
    {
        if (!_heightLightingSettings.ApplyAutomatically ||
            _heightLightingSettings.Validate() != null)
            return;

        float radius = _vm.Document.BrushRadius / terrain.Header.Stretch;
        int range = Math.Max(1, (int)MathF.Round(radius * 3f));
        int centerX = (int)gridX;
        int centerY = (int)gridY;
        HeightBasedLighting.Apply(
            terrain,
            _heightLightingSettings,
            centerX - range,
            centerY - range,
            centerX + range,
            centerY + range);
    }
}
