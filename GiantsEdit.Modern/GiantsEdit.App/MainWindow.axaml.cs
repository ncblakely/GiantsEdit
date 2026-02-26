using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using GiantsEdit.App.ViewModels;
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
    private Point _lastMousePos;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        // Menu events
        MenuNewWorld.Click += async (_, _) => await NewWorldAsync();
        MenuOpenWorld.Click += async (_, _) => await OpenWorldAsync();
        MenuSaveWorld.Click += (_, _) => _vm.Document.SaveWorld();
        MenuSaveWorldAs.Click += async (_, _) => await SaveWorldAsAsync();
        MenuExit.Click += (_, _) => Close();
        MenuResetCamera.Click += (_, _) => { Viewport.Camera.Reset(); Viewport.CurrentRenderState = null; Viewport.Invalidate(); };
        MenuShowTerrain.Click += (_, _) => { _showTerrain = !_showTerrain; Viewport.Invalidate(); };
        MenuShowDome.Click += (_, _) => { _showDome = !_showDome; Viewport.Invalidate(); };
        MenuShowSea.Click += (_, _) => { _showSea = !_showSea; Viewport.Invalidate(); };
        MenuShowObjects.Click += (_, _) => { _showObjects = !_showObjects; Viewport.Invalidate(); };

        // Toolbar mode buttons (radio behavior)
        var modeButtons = new[] { BtnCamera, BtnHeight, BtnLight, BtnTriangle, BtnObject };
        var modes = new[] { EditMode.Camera, EditMode.HeightEdit, EditMode.LightPaint, EditMode.TriangleEdit, EditMode.ObjectEdit };
        for (int i = 0; i < modeButtons.Length; i++)
        {
            int idx = i;
            modeButtons[i].Click += (_, _) =>
            {
                for (int j = 0; j < modeButtons.Length; j++)
                    modeButtons[j].IsChecked = j == idx;
                _vm.Document.CurrentMode = modes[idx];
                _vm.CurrentMode = modes[idx];
                StatusText.Text = $"Mode: {modes[idx]}";
            };
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
            Viewport.CurrentRenderState = null;
            Viewport.Invalidate();
        }
        else if (left)
        {
            Viewport.Camera.Rotate(dx, dy);
            Viewport.CurrentRenderState = null;
            Viewport.Invalidate();
        }
        else if (right)
        {
            Viewport.Camera.Pan(dx, dy);
            Viewport.CurrentRenderState = null;
            Viewport.Invalidate();
        }
    }

    private void OnViewportPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        Viewport.Camera.ZoomWheel((float)e.Delta.Y);
        Viewport.CurrentRenderState = null;
        Viewport.Invalidate();
        e.Handled = true;
    }

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
            Objects = _vm.Document.GetObjectInstances()
        };
    }

    private void UploadTerrainToGpu()
    {
        var terrainData = _vm.Document.BuildTerrainRenderData();
        Debug.WriteLine($"[UploadTerrainToGpu] terrainData={terrainData != null}, renderer={Viewport.Renderer != null}");
        if (terrainData != null)
            Viewport.QueueTerrainUpload(terrainData);
    }

    private void RefreshViewport()
    {
        Viewport.Invalidate();
    }

    private void RebuildTreeView()
    {
        WorldTree.ItemsSource = _vm.TreeRoots;
    }

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
        {
            var leaf = obj.FindChildLeaf("Angle");
            leaf?.SetSingle(angle);
        }

        RefreshViewport();
        StatusText.Text = "Object properties applied";
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
            Title = "Save World File",
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

    private async Task NewWorldAsync()
    {
        var dlg = new Dialogs.NewMapDialog();
        await dlg.ShowDialog(this);

        if (dlg.Confirmed)
        {
            _vm.Document.NewWorld(dlg.MapWidth, dlg.MapHeight, dlg.TextureName);
            StatusText.Text = $"New map: {dlg.MapWidth}x{dlg.MapHeight}";
        }
    }
}