using System.Diagnostics;
using System.Reflection;
using Avalonia.Styling;
using GiantsEdit.App.Dialogs;
using GiantsEdit.Core.DataModel;
using GiantsEdit.Core.Formats;
using GiantsEdit.Core.Rendering;

namespace GiantsEdit.App;

public partial class MainWindow
{
    #region Render state

    private void OnRenderStateNeeded()
    {
        var bounds = Viewport.Bounds;
        float aspect = (float)(bounds.Width / Math.Max(1, bounds.Height));

        Viewport.CurrentRenderState = new RenderState
        {
            ViewMatrix = Viewport.Camera.GetViewMatrix(),
            ProjectionMatrix = Viewport.Camera.GetProjectionMatrix(aspect),
            CameraPosition = Viewport.Camera.Position,
            ShowTerrain = _showTerrain,
            ShowDome = _showDome,
            ShowSea = _showSea,
            ShowObjects = _showObjects,
            ShowTerrainMesh = _viewTerrainMesh,
            DrawRealObjects = _drawRealObjects,
            ViewObjThruTerrain = _viewObjThruTerrain,
            SelectedObjectNode = _vm.Document.SelectedObject,
            Objects = _vm.Document.GetObjectInstances(),
            SplineLines = _vm.Document.GetSplineLines(),
            Lights = _vm.Document.GetDirectionalLights(),
            WorldAmbientColor = _vm.Document.GetWorldAmbientColor()
        };
    }

    private void InvalidateViewport()
    {
        Viewport.CurrentRenderState = null;
        Viewport.Invalidate();
    }

    private void RefreshViewport() => Viewport.Invalidate();

    private void UploadTerrainToGpu()
    {
        var terrainData = _vm.Document.BuildTerrainRenderData();
        if (terrainData == null) return;

        if (_modelManager.HasGameData)
        {
            var texInfo = new TerrainTextureInfo();
            LoadTerrainTexture(BinFormatConstants.NodeGroundTexture, v => { texInfo.GroundImage = v.img; texInfo.GroundWrap = v.wrap; });
            LoadTerrainTexture(BinFormatConstants.NodeSlopeTexture, v => { texInfo.SlopeImage = v.img; texInfo.SlopeWrap = v.wrap; });
            LoadTerrainTexture(BinFormatConstants.NodeWallTexture, v => { texInfo.WallImage = v.img; texInfo.WallWrap = v.wrap; });

            // Normal maps (try normal textures first, fall back to bump textures)
            LoadTerrainTexture(BinFormatConstants.NodeGroundNormalTexture, v => { texInfo.GroundNormalImage = v.img; texInfo.GroundNormalWrap = v.wrap; });
            if (texInfo.GroundNormalImage == null)
                LoadTerrainTexture(BinFormatConstants.NodeGroundBumpTexture, v => { texInfo.GroundNormalImage = v.img; texInfo.GroundNormalWrap = v.wrap; });
            LoadTerrainTexture(BinFormatConstants.NodeSlopeNormalTexture, v => { texInfo.SlopeNormalImage = v.img; texInfo.SlopeNormalWrap = v.wrap; });
            if (texInfo.SlopeNormalImage == null)
                LoadTerrainTexture(BinFormatConstants.NodeSlopeBumpTexture, v => { texInfo.SlopeNormalImage = v.img; texInfo.SlopeNormalWrap = v.wrap; });
            LoadTerrainTexture(BinFormatConstants.NodeWallNormalTexture, v => { texInfo.WallNormalImage = v.img; texInfo.WallNormalWrap = v.wrap; });
            if (texInfo.WallNormalImage == null)
                LoadTerrainTexture(BinFormatConstants.NodeWallBumpTexture, v => { texInfo.WallNormalImage = v.img; texInfo.WallNormalWrap = v.wrap; });

            if (texInfo.GroundImage != null || texInfo.SlopeImage != null || texInfo.WallImage != null)
            {
                var (c0, c1, c2) = _vm.Document.GetTerrainMipFalloff();
                texInfo.MipFalloff0 = c0;
                texInfo.MipFalloff1 = c1;
                texInfo.MipFalloff2 = c2;
                terrainData.Textures = texInfo;
            }
        }

        Viewport.QueueTerrainUpload(terrainData);
    }

    private void LoadTerrainTexture(string kind, Action<(TgaImage? img, float wrap)> setter)
    {
        var (name, wrap) = _vm.Document.GetTerrainTexture(kind);
        if (string.IsNullOrEmpty(name)) return;

        byte[]? data = _modelManager.LoadGameFile(name + ".tga");
        if (data == null || data.Length < 18) return;

        try
        {
            setter((TgaLoader.Load(data), wrap));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LoadTerrainTexture] Failed to load {name}: {ex.Message}");
        }
    }

    private void LoadMapObjectShapes()
    {
        try
        {
            var asm = typeof(MainWindow).Assembly;
            var resourceName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("Mapobj.txt", StringComparison.OrdinalIgnoreCase));
            if (resourceName == null) return;

            using var stream = asm.GetManifestResourceStream(resourceName);
            if (stream == null) return;
            using var reader = new StreamReader(stream);
            var lines = new List<string>();
            while (reader.ReadLine() is { } line)
                lines.Add(line);

            var mapObjReader = new GiantsEdit.Core.Formats.MapObjectReader();
            mapObjReader.Load(lines);
            Viewport.QueueMapObjectsUpload(mapObjReader);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LoadMapObjects] Error: {ex.Message}");
        }
    }

    private void LoadDomeFromGameData()
    {
        if (!_modelManager.HasGameData) return;

        try
        {
            byte[]? gb2Data = _modelManager.LoadGameFile("world.gb2");
            if (gb2Data == null || gb2Data.Length == 0) return;

            var skyObj = Gb2ModelLoader.Load(gb2Data, "sky");
            if (skyObj == null) return;

            TgaImage? domeTex = null;
            string? domeTexName = _vm.Document.GetDomeTextureName();
            if (!string.IsNullOrEmpty(domeTexName))
            {
                byte[]? texData = _modelManager.LoadGameFile(domeTexName + ".tga");
                if (texData != null && texData.Length > 18)
                {
                    try { domeTex = TgaLoader.Load(texData); }
                    catch { /* non-fatal */ }
                }
            }

            Viewport.QueueGlAction(renderer => renderer.UploadDome(skyObj, domeTex));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LoadDome] Error: {ex.Message}");
        }
    }

    #endregion

    #region Model loading

    private static ObjectCatalog LoadEmbeddedCatalog()
    {
        var asm = Assembly.GetExecutingAssembly();
        var resName = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("inclist.tsv"));
        if (resName == null) return new ObjectCatalog();

        using var stream = asm.GetManifestResourceStream(resName)!;
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
            lines.Add(line);
        return ObjectCatalog.LoadFromTsv(lines);
    }

    private async Task ShowPreferencesAsync()
    {
        var dlg = new PreferencesDialog();
        dlg.SetInitialValues(_prefs.GamePath, _prefs.ControlScheme, _prefs.Theme);
        await dlg.ShowDialog(this);

        if (!dlg.Confirmed) return;

        _prefs.GamePath = dlg.GamePath;
        _prefs.ControlScheme = dlg.ControlScheme;
        _prefs.Theme = dlg.ThemeName;
        _prefs.Save();
        _modelManager.SetGamePath(dlg.GamePath);
        ApplyTheme(_prefs.Theme);

        StatusText.Text = _modelManager.HasGameData
            ? $"Game path set — {dlg.GamePath}"
            : "No .gzp files found in bin/ folder";

        // Reload dome with game textures if a world is loaded
        if (_modelManager.HasGameData && _vm.Document.WorldRoot != null)
            LoadDomeFromGameData();
    }

    private void ApplyTheme(string theme)
    {
        if (Avalonia.Application.Current is { } app)
            app.RequestedThemeVariant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    private async Task ToggleDrawRealObjectsAsync()
    {
        _drawRealObjects = !_drawRealObjects;

        if (_drawRealObjects && !_modelManager.HasGameData)
        {
            // Prompt user to set game path first
            await ShowPreferencesAsync();
            if (!_modelManager.HasGameData)
            {
                _drawRealObjects = false;
                StatusText.Text = "Draw real objects requires game path to be set";
                return;
            }
        }

        if (_drawRealObjects)
        {
            StatusText.Text = "Loading models...";
            var objects = _vm.Document.GetObjectInstances();

            // Preload models on the GL thread
            Viewport.QueueGlAction(renderer =>
            {
                _modelManager.PreloadModels(objects, renderer);
            });
        }

        InvalidateViewport();
    }

    /// <summary>
    /// Gets object instances with bounding radii populated from loaded models when in real objects mode.
    /// </summary>
    private List<ObjectInstance> GetObjectInstancesForPicking()
    {
        var objects = _vm.Document.GetObjectInstances();
        if (_drawRealObjects && _modelManager.HasGameData)
        {
            for (int i = 0; i < objects.Count; i++)
            {
                var obj = objects[i];
                float r = _modelManager.GetBoundingRadius(obj.ModelId);
                if (r > 0)
                {
                    obj.HitRadius = r;
                    objects[i] = obj;
                }
            }
        }
        return objects;
    }

    #endregion
}
