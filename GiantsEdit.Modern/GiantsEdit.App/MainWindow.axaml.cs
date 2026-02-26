using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using GiantsEdit.App.Dialogs;
using GiantsEdit.App.ViewModels;
using GiantsEdit.Core.DataModel;
using GiantsEdit.Core.Rendering;
using GiantsEdit.Core.Services;

namespace GiantsEdit.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private bool _showTerrain = true;
    private bool _showDome = true;
    private bool _showSea = true;
    private bool _showObjects = true;
    private bool _viewTerrainMesh;
    private bool _viewObjThruTerrain;
    private Point _lastMousePos;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        // === File menu ===
        MenuNewWorld.Click += async (_, _) => await NewWorldAsync();
        MenuOpenWorld.Click += async (_, _) => await OpenWorldAsync();
        MenuSaveWorld.Click += (_, _) => { _vm.Document.SaveWorld(); StatusText.Text = "Map saved"; };
        MenuSaveWorldAs.Click += async (_, _) => await SaveWorldAsAsync();
        MenuCloseMap.Click += (_, _) => CloseMap();
        MenuClearTerrain.Click += (_, _) => ClearTerrain();
        MenuClearObjects.Click += (_, _) => ClearObjects();
        MenuImportTerrain.Click += async (_, _) => await ImportTerrainAsync();
        MenuImportObjects.Click += async (_, _) => await ImportObjectsAsync();
        MenuExportLightmap.Click += async (_, _) => await ExportBitmapAsync("lightmap");
        MenuExportHeightmap.Click += async (_, _) => await ExportBitmapAsync("heightmap");
        MenuExportTrimap.Click += async (_, _) => await ExportBitmapAsync("trianglemap");
        MenuExportTerrain.Click += async (_, _) => await ExportTerrainAsync();
        MenuExportObjects.Click += async (_, _) => await ExportObjectsAsync();
        MenuExit.Click += (_, _) => Close();

        // === Editor menu ===
        MenuDrawDome.Click += (_, _) => { _showDome = !_showDome; InvalidateViewport(); };
        MenuDomeColor.Click += async (_, _) => await PickColorAsync("Dome"); // TODO: apply to dome
        MenuSeaColor.Click += async (_, _) => await PickColorAsync("Sea");
        MenuSeaGroundColor.Click += async (_, _) => await PickColorAsync("Sea ground");
        MenuGoToLocation.Click += async (_, _) => await GoToLocationAsync();
        MenuViewTerrainMesh.Click += (_, _) => { _viewTerrainMesh = !_viewTerrainMesh; InvalidateViewport(); };
        MenuViewObjThruTerrain.Click += (_, _) => { _viewObjThruTerrain = !_viewObjThruTerrain; InvalidateViewport(); };
        MenuModeCamera.Click += (_, _) => SetMode(EditMode.Camera, 0);
        MenuModeHeight.Click += (_, _) => SetMode(EditMode.HeightEdit, 1);
        MenuModeLight.Click += (_, _) => SetMode(EditMode.LightPaint, 2);
        MenuModeTriangle.Click += (_, _) => SetMode(EditMode.TriangleEdit, 3);
        MenuModeObject.Click += (_, _) => SetMode(EditMode.ObjectEdit, 4);
        MenuResetCamera.Click += (_, _) => { Viewport.Camera.Reset(); InvalidateViewport(); };

        // === Terrain menu ===
        MenuStretchMove.Click += (_, _) => StatusText.Text = "Stretch/Move: not yet implemented";
        MenuCrop.Click += (_, _) => StatusText.Text = "Crop: not yet implemented";
        MenuAddBorders.Click += (_, _) => StatusText.Text = "Add borders: not yet implemented";
        MenuFixEdges.Click += (_, _) => StatusText.Text = "Fix edges: not yet implemented";
        MenuSoftenNoEdges.Click += (_, _) => { SoftenTerrain(false); StatusText.Text = "Terrain softened (no edges)"; };
        MenuSoftenWithEdges.Click += (_, _) => { SoftenTerrain(true); StatusText.Text = "Terrain softened (with edges)"; };
        MenuAutoLighting.Click += (_, _) => StatusText.Text = "Auto lighting: not yet implemented";

        // === Map menu ===
        MenuMapNames.Click += async (_, _) => await ShowMapNamesAsync();
        MenuMapObjTree.Click += (_, _) => StatusText.Text = "Map objects tree: not yet implemented";
        MenuMissions.Click += (_, _) =>
        {
            var dlg = new MissionsDialog(_vm.Document);
            dlg.Show(this);
        };
        MenuMissionObjTree.Click += (_, _) => StatusText.Text = "Mission objects tree: not yet implemented";
        MenuMarkerReport.Click += async (_, _) => await ShowMarkerReportAsync();
        MenuPlaceOnSurface.Click += (_, _) => { PlaceAllObjectsOnSurface(); StatusText.Text = "All objects placed on surface"; };

        // === Special menu ===
        MenuWeirdMatrix.Click += (_, _) => StatusText.Text = "Weird matrix: not yet implemented";
        MenuParabolic.Click += (_, _) => StatusText.Text = "Parabolic shape: not yet implemented";
        MenuShowConsole.Click += (_, _) =>
        {
            var console = new ConsoleWindow();
            console.Show(this);
        };

        // === Help menu ===
        MenuAbout.Click += (_, _) =>
        {
            StatusText.Text = "GiantsEdit — Modern C# port of the Giants: Citizen Kabuto level editor";
        };

        // === Toolbar mode buttons (radio behavior) ===
        var modeButtons = new[] { BtnCamera, BtnHeight, BtnLight, BtnTriangle, BtnObject };
        var modes = new[] { EditMode.Camera, EditMode.HeightEdit, EditMode.LightPaint, EditMode.TriangleEdit, EditMode.ObjectEdit };
        for (int i = 0; i < modeButtons.Length; i++)
        {
            int idx = i;
            modeButtons[i].Click += (_, _) => SetMode(modes[idx], idx);
        }

        SliderRadius.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == "Value")
                _vm.BrushRadius = (float)SliderRadius.Value;
        };
        SliderStrength.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == "Value")
                _vm.BrushStrength = (float)SliderStrength.Value;
        };

        // Property panel
        BtnApplyProps.Click += (_, _) => ApplyObjectProperties();
        BtnDeleteObj.Click += (_, _) =>
        {
            _vm.Document.RemoveSelectedObject();
            ObjPropsPanel.IsVisible = false;
            PropHeader.Text = "No selection";
            RefreshViewport();
        };

        // Document events
        _vm.Document.WorldChanged += () =>
        {
            RebuildTreeView();
            UploadTerrainToGpu();
            RefreshViewport();
        };
        _vm.Document.TerrainChanged += () =>
        {
            UploadTerrainToGpu();
            RefreshViewport();
        };
        _vm.Document.SelectionChanged += () =>
        {
            var obj = _vm.Document.SelectedObject;
            ObjPropsPanel.IsVisible = obj != null;
            if (obj != null)
            {
                PropHeader.Text = $"Object #{obj.FindChildLeaf("Type")?.Int32Value ?? 0}";
                PropObjType.Text = (obj.FindChildLeaf("Type")?.Int32Value ?? 0).ToString();
                PropObjX.Text = (obj.FindChildLeaf("X")?.SingleValue ?? 0).ToString("F2");
                PropObjY.Text = (obj.FindChildLeaf("Y")?.SingleValue ?? 0).ToString("F2");
                PropObjZ.Text = (obj.FindChildLeaf("Z")?.SingleValue ?? 0).ToString("F2");
                PropObjAngle.Text = (obj.FindChildLeaf("Angle")?.SingleValue ?? 0).ToString("F4");
                PropObjScale.Text = (obj.FindChildLeaf("Scale")?.SingleValue ?? 1f).ToString("F4");
            }
            else
            {
                PropHeader.Text = "No selection";
            }
        };

        Viewport.RenderStateNeeded += OnRenderStateNeeded;

        // Mouse input on viewport panel (Panel provides hit-test surface)
        ViewportPanel.PointerPressed += OnViewportPointerPressed;
        ViewportPanel.PointerMoved += OnViewportPointerMoved;
        ViewportPanel.PointerWheelChanged += OnViewportPointerWheel;
    }

    #region Toolbar / Mode helpers

    private void SetMode(EditMode mode, int buttonIndex)
    {
        var modeButtons = new[] { BtnCamera, BtnHeight, BtnLight, BtnTriangle, BtnObject };
        for (int j = 0; j < modeButtons.Length; j++)
            modeButtons[j].IsChecked = j == buttonIndex;
        _vm.Document.CurrentMode = mode;
        _vm.CurrentMode = mode;
        StatusText.Text = $"Mode: {mode}";
    }

    #endregion

    #region Viewport input

    private void OnViewportPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _lastMousePos = e.GetPosition(ViewportPanel);
        ViewportPanel.Focus();
        e.Handled = true;
    }

    private void OnViewportPointerMoved(object? sender, PointerEventArgs e)
    {
        var pos = e.GetPosition(ViewportPanel);
        float dx = (float)(pos.X - _lastMousePos.X);
        float dy = (float)(pos.Y - _lastMousePos.Y);
        _lastMousePos = pos;

        var props = e.GetCurrentPoint(ViewportPanel).Properties;
        bool left = props.IsLeftButtonPressed;
        bool right = props.IsRightButtonPressed;

        if (left && right)
        {
            Viewport.Camera.Zoom(dy);
            InvalidateViewport();
        }
        else if (left)
        {
            Viewport.Camera.Rotate(dx, dy);
            InvalidateViewport();
        }
        else if (right)
        {
            Viewport.Camera.Pan(dx, dy);
            InvalidateViewport();
        }
    }

    private void OnViewportPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        Viewport.Camera.ZoomWheel((float)e.Delta.Y);
        InvalidateViewport();
        e.Handled = true;
    }

    #endregion

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
            Objects = _vm.Document.GetObjectInstances()
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
        if (terrainData != null)
            Viewport.QueueTerrainUpload(terrainData);
    }

    private void RebuildTreeView()
    {
        WorldTree.ItemsSource = _vm.TreeRoots;
    }

    #endregion

    #region File menu handlers

    private async Task NewWorldAsync()
    {
        var dlg = new NewMapDialog();
        await dlg.ShowDialog(this);

        if (dlg.Confirmed)
        {
            _vm.Document.NewWorld(dlg.MapWidth, dlg.MapHeight, dlg.TextureName);
            StatusText.Text = $"New map: {dlg.MapWidth}x{dlg.MapHeight}";
        }
    }

    private async Task OpenWorldAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Map",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Map Files") { Patterns = ["*.gck", "*.bin"] },
                new FilePickerFileType("GCK Archives") { Patterns = ["*.gck"] },
                new FilePickerFileType("BIN Files") { Patterns = ["*.bin"] }
            ]
        });

        if (files.Count > 0)
        {
            var path = files[0].TryGetLocalPath();
            if (path != null)
            {
                try
                {
                    if (path.EndsWith(".gck", StringComparison.OrdinalIgnoreCase))
                        _vm.Document.LoadGck(path);
                    else
                        _vm.Document.LoadWorld(path);
                    StatusText.Text = $"Loaded: {System.IO.Path.GetFileName(path)}";
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[OpenWorld] Error: {ex}");
                    StatusText.Text = $"Error: {ex.Message}";
                }
            }
        }
    }

    private async Task SaveWorldAsAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Map As",
            DefaultExtension = "bin",
            FileTypeChoices = [new FilePickerFileType("World Files") { Patterns = ["*.bin"] }]
        });

        if (file != null)
        {
            var path = file.TryGetLocalPath();
            if (path != null)
            {
                _vm.Document.SaveWorld(path);
                StatusText.Text = $"Saved: {System.IO.Path.GetFileName(path)}";
            }
        }
    }

    private void CloseMap()
    {
        _vm.Document.NewWorld(2, 2);
        _vm.TreeRoots.Clear();
        WorldTree.ItemsSource = null;
        InvalidateViewport();
        StatusText.Text = "Map closed";
    }

    private void ClearTerrain()
    {
        if (_vm.Document.Terrain == null) return;
        int w = _vm.Document.Terrain.Width;
        int h = _vm.Document.Terrain.Height;
        _vm.Document.NewWorld(w, h);
        StatusText.Text = "Terrain cleared";
    }

    private void ClearObjects()
    {
        var root = _vm.Document.WorldRoot;
        if (root == null) return;

        // Find the "Object" slot and clear it
        for (int i = 0; i < root.NodeSlots.Count; i++)
        {
            if (root.NodeSlots[i].Count > 0 && root.NodeSlots[i][0].Name == "Object")
            {
                root.ClearSlot(i);
                break;
            }
        }
        InvalidateViewport();
        StatusText.Text = "Objects cleared";
    }

    private async Task ImportTerrainAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Terrain (GTI)",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("GTI Files") { Patterns = ["*.gti"] }]
        });

        if (files.Count > 0)
        {
            var path = files[0].TryGetLocalPath();
            if (path != null)
            {
                _vm.Document.LoadTerrain(path);
                StatusText.Text = $"Imported terrain: {System.IO.Path.GetFileName(path)}";
            }
        }
    }

    private async Task ImportObjectsAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Objects (BIN)",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("BIN Files") { Patterns = ["*.bin"] }]
        });

        if (files.Count > 0)
        {
            var path = files[0].TryGetLocalPath();
            if (path != null)
            {
                _vm.Document.LoadWorld(path);
                StatusText.Text = $"Imported objects from: {System.IO.Path.GetFileName(path)}";
            }
        }
    }

    private async Task ExportTerrainAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Terrain",
            DefaultExtension = "gti",
            FileTypeChoices = [new FilePickerFileType("GTI Files") { Patterns = ["*.gti"] }]
        });

        if (file != null)
        {
            var path = file.TryGetLocalPath();
            if (path != null)
            {
                _vm.Document.SaveTerrain(path);
                StatusText.Text = $"Terrain exported: {System.IO.Path.GetFileName(path)}";
            }
        }
    }

    private async Task ExportObjectsAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Objects",
            DefaultExtension = "bin",
            FileTypeChoices = [new FilePickerFileType("BIN Files") { Patterns = ["*.bin"] }]
        });

        if (file != null)
        {
            var path = file.TryGetLocalPath();
            if (path != null)
            {
                _vm.Document.SaveWorld(path);

                // Show export report in console (matches Delphi SaveBinW → ScanDone behavior)
                var console = new ConsoleWindow();
                var report = _vm.Document.GetExportReport();
                if (!string.IsNullOrEmpty(report))
                {
                    console.AppendLine(report);
                    console.AppendLine("These items were not included in the saved map file; all other items should be alright.");
                }
                else
                {
                    console.AppendLine($"Objects exported successfully to {System.IO.Path.GetFileName(path)}");
                }
                console.Show(this);
                StatusText.Text = $"Objects exported: {System.IO.Path.GetFileName(path)}";
            }
        }
    }

    private async Task ExportBitmapAsync(string kind)
    {
        var terrain = _vm.Document.Terrain;
        if (terrain == null) { StatusText.Text = "No terrain loaded"; return; }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Export {kind}",
            DefaultExtension = "bmp",
            FileTypeChoices = [new FilePickerFileType("Bitmap") { Patterns = ["*.bmp"] }]
        });

        if (file == null) return;
        var path = file.TryGetLocalPath();
        if (path == null) return;

        int w = terrain.Width;
        int h = terrain.Height;

        // Build raw RGBA pixel data
        var pixels = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int srcIdx = y * w + x;
                int dstIdx = ((h - 1 - y) * w + x) * 4; // flip Y for image

                byte r, g, b;
                switch (kind)
                {
                    case "lightmap":
                        r = terrain.LightMap[srcIdx * 3 + 0];
                        g = terrain.LightMap[srcIdx * 3 + 1];
                        b = terrain.LightMap[srcIdx * 3 + 2];
                        break;
                    case "heightmap":
                        float ht = terrain.Heights[srcIdx];
                        byte v = (byte)Math.Clamp((int)(ht + 128), 0, 255);
                        r = g = b = v;
                        break;
                    case "trianglemap":
                        byte tri = terrain.Triangles[srcIdx];
                        r = (byte)((tri & 1) != 0 ? 255 : 0);
                        g = (byte)((tri & 2) != 0 ? 255 : 0);
                        b = (byte)((tri & 4) != 0 ? 255 : 0);
                        break;
                    default:
                        r = g = b = 0;
                        break;
                }
                pixels[dstIdx + 0] = r;
                pixels[dstIdx + 1] = g;
                pixels[dstIdx + 2] = b;
                pixels[dstIdx + 3] = 255;
            }
        }

        // Write as simple 32-bit BMP
        WriteBmp32(path, w, h, pixels);
        StatusText.Text = $"Exported {kind}: {System.IO.Path.GetFileName(path)}";
    }

    private static void WriteBmp32(string path, int w, int h, byte[] rgba)
    {
        int rowBytes = w * 4;
        int imageSize = rowBytes * h;
        int fileSize = 54 + imageSize;

        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);

        // BMP header
        bw.Write((byte)'B'); bw.Write((byte)'M');
        bw.Write(fileSize);
        bw.Write(0); // reserved
        bw.Write(54); // pixel data offset
        // DIB header (BITMAPINFOHEADER)
        bw.Write(40); // header size
        bw.Write(w);
        bw.Write(h);
        bw.Write((short)1); // planes
        bw.Write((short)32); // bpp
        bw.Write(0); // compression
        bw.Write(imageSize);
        bw.Write(2835); bw.Write(2835); // 72 DPI
        bw.Write(0); bw.Write(0);

        // Pixel data (BMP stores bottom-up, BGRA)
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                bw.Write(rgba[i + 2]); // B
                bw.Write(rgba[i + 1]); // G
                bw.Write(rgba[i + 0]); // R
                bw.Write(rgba[i + 3]); // A
            }
        }
    }

    #endregion

    #region Editor menu handlers

    private async Task GoToLocationAsync()
    {
        var dlg = new GoToLocationDialog();
        await dlg.ShowDialog(this);
        if (dlg.Confirmed)
        {
            Viewport.Camera.LookAt(dlg.Target);
            InvalidateViewport();
            StatusText.Text = $"Camera moved to ({dlg.Target.X:F0}, {dlg.Target.Y:F0}, {dlg.Target.Z:F0})";
        }
    }

    private async Task PickColorAsync(string target)
    {
        // Simple placeholder — Avalonia doesn't have a built-in color picker dialog
        StatusText.Text = $"{target} color picker: not yet implemented";
        await Task.CompletedTask;
    }

    #endregion

    #region Terrain menu handlers

    private void SoftenTerrain(bool includeEdges)
    {
        var terrain = _vm.Document.Terrain;
        if (terrain == null) return;

        int w = terrain.Width;
        int h = terrain.Height;
        var src = (float[])terrain.Heights.Clone();
        int startX = includeEdges ? 0 : 1;
        int startY = includeEdges ? 0 : 1;
        int endX = includeEdges ? w : w - 1;
        int endY = includeEdges ? h : h - 1;

        for (int y = startY; y < endY; y++)
        {
            for (int x = startX; x < endX; x++)
            {
                float sum = 0;
                int count = 0;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx >= 0 && nx < w && ny >= 0 && ny < h)
                        {
                            sum += src[ny * w + nx];
                            count++;
                        }
                    }
                }
                terrain.Heights[y * w + x] = sum / count;
            }
        }

        _vm.Document.NotifyTerrainChanged();
    }

    #endregion

    #region Map menu handlers

    private async Task ShowMapNamesAsync()
    {
        var dlg = new MapNamesDialog();
        dlg.SetValues(
            _vm.Document.MapBinName,
            _vm.Document.UserMessage,
            _vm.Document.MapType,
            _vm.Document.Shareable);
        await dlg.ShowDialog(this);

        if (dlg.Confirmed)
        {
            _vm.Document.MapBinName = dlg.BinFileName;
            _vm.Document.UserMessage = dlg.Message;
            _vm.Document.MapType = dlg.MapType;
            _vm.Document.Shareable = dlg.Shareable;
            StatusText.Text = $"Map name set to: {dlg.BinFileName}";
        }
    }

    private async Task ShowMarkerReportAsync()
    {
        var markers = _vm.Document.GetMarkers();
        var dlg = new MarkerReportDialog();
        dlg.SetMarkers(markers);
        await dlg.ShowDialog(this);
    }

    private void PlaceAllObjectsOnSurface()
    {
        var terrain = _vm.Document.Terrain;
        var root = _vm.Document.WorldRoot;
        if (terrain == null || root == null) return;

        foreach (var obj in root.EnumerateNodes())
        {
            if (obj.Name != "Object") continue;

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

    #region Property panel

    private void ApplyObjectProperties()
    {
        var obj = _vm.Document.SelectedObject;
        if (obj == null) return;

        if (float.TryParse(PropObjX.Text, out float x))
            obj.FindChildLeaf("X")?.SetSingle(x);
        if (float.TryParse(PropObjY.Text, out float y))
            obj.FindChildLeaf("Y")?.SetSingle(y);
        if (float.TryParse(PropObjZ.Text, out float z))
            obj.FindChildLeaf("Z")?.SetSingle(z);
        if (float.TryParse(PropObjAngle.Text, out float angle))
            obj.FindChildLeaf("Angle")?.SetSingle(angle);

        RefreshViewport();
        StatusText.Text = "Object properties applied";
    }

    #endregion
}