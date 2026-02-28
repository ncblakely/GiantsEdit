using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using GiantsEdit.App.Dialogs;
using GiantsEdit.App.ViewModels;
using GiantsEdit.Core.DataModel;
using GiantsEdit.Core.Formats;
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
    private bool _drawRealObjects = true;
    private Point _lastMousePos;
    private bool _isClickDown; // true on pointer-down, false after first move
    private bool _objDragAllowed; // true when drag-to-move is permitted (requires separate press from selection)
    private long _objPressTimestamp; // ticks when pointer was pressed for drag delay

    // Height editing state (matches Delphi globals)
    private float _minimumHeight = -40f;
    private float _maximumHeight = 1000f;
    private float _currentHeight = -40f;
    private bool _heightPanelUpdating;

    // Light editing state
    private byte _paintR = 255, _paintG = 255, _paintB = 255;
    private bool _lightPanelUpdating;

    // Model loading
    private readonly ObjectCatalog _objectCatalog;
    private readonly ModelManager _modelManager;
    private readonly AppPreferences _prefs;

    // WASD fly camera state (Default control scheme)
    private readonly HashSet<Key> _keysDown = new();
    private Avalonia.Threading.DispatcherTimer? _flyTimer;
    private bool _rmbDown; // tracks RMB for Default scheme fly mode

    public MainWindow()
    {
        // Load object catalog from embedded resource
        _objectCatalog = LoadEmbeddedCatalog();
        _modelManager = new ModelManager(_objectCatalog);
        _prefs = AppPreferences.Load();

        if (!string.IsNullOrEmpty(_prefs.GamePath))
            _modelManager.SetGamePath(_prefs.GamePath);

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
        MenuDrawRealObjects.Click += async (_, _) => await ToggleDrawRealObjectsAsync();
        MenuModeCamera.Click += (_, _) => SetMode(EditMode.Camera, 0);
        MenuModeHeight.Click += (_, _) => SetMode(EditMode.HeightEdit, 1);
        MenuModeLight.Click += (_, _) => SetMode(EditMode.LightPaint, 2);
        MenuModeTriangle.Click += (_, _) => SetMode(EditMode.TriangleEdit, 3);
        MenuModeObject.Click += (_, _) => SetMode(EditMode.ObjectEdit, 4);
        MenuResetCamera.Click += (_, _) => { Viewport.Camera.Reset(); InvalidateViewport(); };
        MenuPreferences.Click += async (_, _) => await ShowPreferencesAsync();

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
        MenuMapObjTree.Click += (_, _) => ShowWorldObjectsTree();
        MenuMissions.Click += (_, _) =>
        {
            var dlg = new MissionsDialog(_vm.Document);
            dlg.Show(this);
        };
        MenuMissionObjTree.Click += (_, _) => ShowMissionObjectsTree();
        MenuMarkerReport.Click += async (_, _) => await ShowMarkerReportAsync();
        MenuPlaceOnSurface.Click += (_, _) => { PlaceAllObjectsOnSurface(); StatusText.Text = "All objects placed on surface"; };

        // === Special menu ===

        MenuShowConsole.Click += (_, _) =>
        {
            var console = new ConsoleWindow();
            console.Show(this);
        };

        // === Help menu ===
        MenuShowControls.Click += async (_, _) =>
        {
            var dlg = new EditorControlsDialog(_prefs.ControlScheme);
            await dlg.ShowDialog(this);
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

        // Height editor panel
        SliderHeight.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == "Value" && !_heightPanelUpdating)
            {
                _heightPanelUpdating = true;
                _currentHeight = _minimumHeight + (_maximumHeight - _minimumHeight) * (float)SliderHeight.Value / 1000f;
                TxtCurrentHeight.Text = _currentHeight.ToString("F2");
                _vm.Document.TargetHeight = _currentHeight;
                _heightPanelUpdating = false;
            }
        };
        TxtMinHeight.TextChanged += (_, _) =>
        {
            if (_heightPanelUpdating) return;
            if (!float.TryParse(TxtMinHeight.Text, out float mh)) return;
            _heightPanelUpdating = true;
            if (mh >= _maximumHeight) { _maximumHeight = mh; TxtMaxHeight.Text = _maximumHeight.ToString("F2"); }
            _minimumHeight = mh;
            ClampAndSyncHeightSlider();
            _heightPanelUpdating = false;
        };
        TxtMaxHeight.TextChanged += (_, _) =>
        {
            if (_heightPanelUpdating) return;
            if (!float.TryParse(TxtMaxHeight.Text, out float mh)) return;
            _heightPanelUpdating = true;
            if (mh <= _minimumHeight) { _minimumHeight = mh; TxtMinHeight.Text = _minimumHeight.ToString("F2"); }
            _maximumHeight = mh;
            ClampAndSyncHeightSlider();
            _heightPanelUpdating = false;
        };
        TxtCurrentHeight.TextChanged += (_, _) =>
        {
            if (_heightPanelUpdating) return;
            if (!float.TryParse(TxtCurrentHeight.Text, out float ch)) return;
            _heightPanelUpdating = true;
            if (ch >= _maximumHeight) { _maximumHeight = ch; TxtMaxHeight.Text = _maximumHeight.ToString("F2"); }
            if (ch <= _minimumHeight) { _minimumHeight = ch; TxtMinHeight.Text = _minimumHeight.ToString("F2"); }
            _currentHeight = ch;
            _vm.Document.TargetHeight = _currentHeight;
            SyncHeightSlider();
            _heightPanelUpdating = false;
        };

        // Light editor panel
        SliderR.PropertyChanged += (_, e) => { if (e.Property.Name == "Value") OnLightSliderChanged(); };
        SliderG.PropertyChanged += (_, e) => { if (e.Property.Name == "Value") OnLightSliderChanged(); };
        SliderB.PropertyChanged += (_, e) => { if (e.Property.Name == "Value") OnLightSliderChanged(); };
        TxtColorR.TextChanged += (_, _) => OnLightTextChanged();
        TxtColorG.TextChanged += (_, _) => OnLightTextChanged();
        TxtColorB.TextChanged += (_, _) => OnLightTextChanged();
        ColorSwatch.PointerPressed += (_, _) => UpdateLightColor(_paintR, _paintG, _paintB);

        // Triangle editor panel
        RbTriSet.IsCheckedChanged += (_, _) => { if (RbTriSet.IsChecked == true) _vm.Document.TriangleMode = TriangleSubMode.SetCorner; };
        RbTriDiag.IsCheckedChanged += (_, _) => { if (RbTriDiag.IsChecked == true) _vm.Document.TriangleMode = TriangleSubMode.DiagDirection; };
        RbTriDiagOpt.IsCheckedChanged += (_, _) => { if (RbTriDiagOpt.IsChecked == true) _vm.Document.TriangleMode = TriangleSubMode.DiagOptimize; };
        RbTriCornerOpt.IsCheckedChanged += (_, _) => { if (RbTriCornerOpt.IsChecked == true) _vm.Document.TriangleMode = TriangleSubMode.OptCorner; };

        // Property panel — object type dropdown
        var allObjNames = ObjectNames.GetAllDisplayNames();
        LstObjTypes.ItemsSource = allObjNames;
        BtnObjTypeDropdown.Click += (_, _) =>
        {
            TxtObjTypeSearch.Text = "";
            LstObjTypes.ItemsSource = allObjNames;
            ObjTypePopup.IsOpen = !ObjTypePopup.IsOpen;
            if (ObjTypePopup.IsOpen)
                TxtObjTypeSearch.Focus();
        };
        TxtObjTypeSearch.TextChanged += (_, _) =>
        {
            var filter = TxtObjTypeSearch.Text;
            if (string.IsNullOrEmpty(filter))
            {
                LstObjTypes.ItemsSource = allObjNames;
                return;
            }
            LstObjTypes.ItemsSource = ObjectNames.Search(filter);
        };
        LstObjTypes.SelectionChanged += (_, _) =>
        {
            if (LstObjTypes.SelectedItem is string selected)
            {
                PropObjType.Text = selected;
                ObjTypePopup.IsOpen = false;
            }
        };

        BtnApplyProps.Click += (_, _) => ApplyObjectProperties();
        BtnDeleteObj.Click += (_, _) =>
        {
            _vm.Document.RemoveSelectedObject();
            ObjPropsPanel.IsVisible = false;
            PropHeader.Text = "No selection";
            RefreshViewport();
        };

        // Optional property checkboxes: enable/disable text box and add/remove leaf
        ChkObjScale.IsCheckedChanged += (_, _) =>
        {
            PropObjScale.IsEnabled = ChkObjScale.IsChecked == true;
            ToggleOptionalLeaf("Scale", ChkObjScale.IsChecked == true, PropObjScale, 1.0f);
        };
        ChkObjAIMode.IsCheckedChanged += (_, _) =>
        {
            PropObjAIMode.IsEnabled = ChkObjAIMode.IsChecked == true;
            ToggleOptionalByteLeaf("AIMode", ChkObjAIMode.IsChecked == true, PropObjAIMode, 0);
        };
        ChkObjTeamID.IsCheckedChanged += (_, _) =>
        {
            PropObjTeamID.IsEnabled = ChkObjTeamID.IsChecked == true;
            ToggleOptionalInt32Leaf("TeamID", ChkObjTeamID.IsChecked == true, PropObjTeamID, 0);
        };
        ChkObjTilt.IsCheckedChanged += (_, _) => ToggleTilt();

        // Document events
        _vm.Document.WorldChanged += () =>
        {
            UploadTerrainToGpu();
            if (_drawRealObjects && _modelManager.HasGameData)
            {
                var objects = _vm.Document.GetObjectInstances();
                Viewport.QueueGlAction(renderer => _modelManager.PreloadModels(objects, renderer));
            }
            InvalidateViewport();
        };
        _vm.Document.TerrainChanged += () =>
        {
            UploadTerrainToGpu();
            InvalidateViewport();
        };
        _vm.Document.SelectionChanged += () =>
        {
            var obj = _vm.Document.SelectedObject;
            ObjPropsPanel.IsVisible = obj != null;
            if (obj != null)
            {
                PropHeader.Text = $"Object: {ObjectNames.GetDisplayName(obj.FindChildLeaf("Type")?.Int32Value ?? 0)}";
                PropObjType.Text = ObjectNames.GetDisplayName(obj.FindChildLeaf("Type")?.Int32Value ?? 0);
                PropObjX.Text = (obj.FindChildLeaf("X")?.SingleValue ?? 0).ToString("F2");
                PropObjY.Text = (obj.FindChildLeaf("Y")?.SingleValue ?? 0).ToString("F2");
                PropObjZ.Text = (obj.FindChildLeaf("Z")?.SingleValue ?? 0).ToString("F2");
                PropObjAngle.Text = (obj.FindChildLeaf("DirFacing")?.SingleValue ?? 0).ToString("F4");
                PropObjScale.Text = (obj.FindChildLeaf("Scale")?.SingleValue ?? 1f).ToString("F4");
            }
            else
            {
                PropHeader.Text = "No selection";
            }
        };

        Viewport.RenderStateNeeded += OnRenderStateNeeded;

        // Load map object shapes for editor rendering
        LoadMapObjectShapes();

        // Mouse input on viewport panel (Panel provides hit-test surface)
        ViewportPanel.PointerPressed += OnViewportPointerPressed;
        ViewportPanel.PointerMoved += OnViewportPointerMoved;
        ViewportPanel.PointerReleased += OnViewportPointerReleased;
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
        HeightPanel.IsVisible = mode == EditMode.HeightEdit;
        LightPanel.IsVisible = mode == EditMode.LightPaint;
        TrianglePanel.IsVisible = mode == EditMode.TriangleEdit;
        StatusText.Text = $"Mode: {mode}";
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;

        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.N: _ = NewWorldAsync(); e.Handled = true; return;
                case Key.O: _ = OpenWorldAsync(); e.Handled = true; return;
                case Key.S when shift: _ = SaveWorldAsAsync(); e.Handled = true; return;
                case Key.S: _vm.Document.SaveWorld(); StatusText.Text = "Map saved"; e.Handled = true; return;
                case Key.W: CloseMap(); e.Handled = true; return;
            }
        }

        switch (e.Key)
        {
            case Key.F1: SetMode(EditMode.Camera, 0); e.Handled = true; break;
            case Key.F2: SetMode(EditMode.HeightEdit, 1); e.Handled = true; break;
            case Key.F3: SetMode(EditMode.LightPaint, 2); e.Handled = true; break;
            case Key.F4: SetMode(EditMode.TriangleEdit, 3); e.Handled = true; break;
            case Key.F5: SetMode(EditMode.ObjectEdit, 4); e.Handled = true; break;
            case Key.F9: _viewTerrainMesh = !_viewTerrainMesh; InvalidateViewport(); e.Handled = true; break;
            case Key.F10: _ = ToggleDrawRealObjectsAsync(); e.Handled = true; break;
            case Key.F11: _viewObjThruTerrain = !_viewObjThruTerrain; InvalidateViewport(); e.Handled = true; break;
        }

        // WASD fly key tracking (Default scheme only, requires RMB held, not when typing in text fields)
        if (_prefs.ControlScheme == ControlScheme.Default && e.Key is Key.W or Key.A or Key.S or Key.D or Key.Q or Key.E)
        {
            bool isTextInput = FocusManager?.GetFocusedElement() is TextBox;
            if (!ctrl && !isTextInput)
            {
                _keysDown.Add(e.Key);
                StartFlyTimer();
                e.Handled = true;
            }
        }

        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        _keysDown.Remove(e.Key);
        if (_keysDown.Count == 0)
            StopFlyTimer();
        base.OnKeyUp(e);
    }

    private void StartFlyTimer()
    {
        if (_flyTimer != null) return;
        _flyTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _flyTimer.Tick += OnFlyTick;
        _flyTimer.Start();
    }

    private void StopFlyTimer()
    {
        _flyTimer?.Stop();
        _flyTimer = null;
    }

    private void OnFlyTick(object? sender, EventArgs e)
    {
        if (!_rmbDown || _keysDown.Count == 0)
        {
            StopFlyTimer();
            return;
        }

        float speed = Viewport.Camera.PanSpeed * 1.5f;
        if (_keysDown.Contains(Key.W)) Viewport.Camera.MoveForward(speed);
        if (_keysDown.Contains(Key.S)) Viewport.Camera.MoveForward(-speed);
        if (_keysDown.Contains(Key.D)) Viewport.Camera.MoveRight(speed);
        if (_keysDown.Contains(Key.A)) Viewport.Camera.MoveRight(-speed);
        if (_keysDown.Contains(Key.E)) Viewport.Camera.MoveUp(speed);
        if (_keysDown.Contains(Key.Q)) Viewport.Camera.MoveUp(-speed);
        InvalidateViewport();
    }

    private void ClampAndSyncHeightSlider()
    {
        if (_currentHeight < _minimumHeight) { _currentHeight = _minimumHeight; TxtCurrentHeight.Text = _currentHeight.ToString("F2"); }
        if (_currentHeight > _maximumHeight) { _currentHeight = _maximumHeight; TxtCurrentHeight.Text = _currentHeight.ToString("F2"); }
        _vm.Document.TargetHeight = _currentHeight;
        SyncHeightSlider();
    }

    private void SyncHeightSlider()
    {
        if (_maximumHeight > _minimumHeight)
            SliderHeight.Value = (_currentHeight - _minimumHeight) / (_maximumHeight - _minimumHeight) * 1000;
    }

    #endregion

    #region Light panel

    private void OnLightSliderChanged()
    {
        if (_lightPanelUpdating) return;
        _lightPanelUpdating = true;
        _paintR = (byte)Math.Clamp((int)SliderR.Value, 0, 255);
        _paintG = (byte)Math.Clamp((int)SliderG.Value, 0, 255);
        _paintB = (byte)Math.Clamp((int)SliderB.Value, 0, 255);
        TxtColorR.Text = _paintR.ToString();
        TxtColorG.Text = _paintG.ToString();
        TxtColorB.Text = _paintB.ToString();
        UpdateColorSwatch();
        _lightPanelUpdating = false;
    }

    private void OnLightTextChanged()
    {
        if (_lightPanelUpdating) return;
        if (!byte.TryParse(TxtColorR.Text, out byte r)) return;
        if (!byte.TryParse(TxtColorG.Text, out byte g)) return;
        if (!byte.TryParse(TxtColorB.Text, out byte b)) return;
        _lightPanelUpdating = true;
        _paintR = r; _paintG = g; _paintB = b;
        SliderR.Value = r; SliderG.Value = g; SliderB.Value = b;
        UpdateColorSwatch();
        _lightPanelUpdating = false;
    }

    private void UpdateLightColor(byte r, byte g, byte b)
    {
        _lightPanelUpdating = true;
        _paintR = r; _paintG = g; _paintB = b;
        SliderR.Value = r; SliderG.Value = g; SliderB.Value = b;
        TxtColorR.Text = r.ToString();
        TxtColorG.Text = g.ToString();
        TxtColorB.Text = b.ToString();
        UpdateColorSwatch();
        _lightPanelUpdating = false;
    }

    private void UpdateColorSwatch()
    {
        ColorSwatch.Background = new Avalonia.Media.SolidColorBrush(
            Avalonia.Media.Color.FromRgb(_paintR, _paintG, _paintB));
    }

    #endregion

    #region Viewport input

    private void OnViewportPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _lastMousePos = e.GetPosition(ViewportPanel);
        ViewportPanel.Focus();
        _isClickDown = true;
        _objDragAllowed = _vm.Document.SelectedObject != null
            && _vm.Document.CurrentMode == EditMode.ObjectEdit;
        _objPressTimestamp = Environment.TickCount64;

        var props = e.GetCurrentPoint(ViewportPanel).Properties;
        if (props.IsRightButtonPressed) _rmbDown = true;

        if (_prefs.ControlScheme == ControlScheme.Default)
            OnPointerPressed_Default(props, e.KeyModifiers);
        else
            OnPointerPressed_Classic(props, e.KeyModifiers);

        e.Handled = true;
    }

    private void OnPointerPressed_Default(PointerPointProperties props, KeyModifiers modifiers)
    {
        // Default scheme: LMB in edit modes starts editing, RMB starts camera
        if (_vm.Document.CurrentMode != EditMode.Camera && props.IsLeftButtonPressed && !props.IsRightButtonPressed)
            HandleEditModeInput((int)_lastMousePos.X, (int)_lastMousePos.Y, true, false, modifiers);
    }

    private void OnPointerPressed_Classic(PointerPointProperties props, KeyModifiers modifiers)
    {
        // Classic scheme: any button in edit modes starts editing
        if (_vm.Document.CurrentMode != EditMode.Camera)
        {
            if (props.IsLeftButtonPressed || props.IsRightButtonPressed)
                HandleEditModeInput((int)_lastMousePos.X, (int)_lastMousePos.Y, props.IsLeftButtonPressed, props.IsRightButtonPressed, modifiers);
        }
    }

    private void OnViewportPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var props = e.GetCurrentPoint(ViewportPanel).Properties;

        // Default scheme: quick RMB click (no drag) in Object mode → context menu
        if (_prefs.ControlScheme == ControlScheme.Default && _isClickDown
            && e.InitialPressMouseButton == Avalonia.Input.MouseButton.Right
            && _vm.Document.CurrentMode == EditMode.ObjectEdit)
        {
            var pos = e.GetPosition(ViewportPanel);
            var bounds = Viewport.Bounds;
            int vpW = (int)bounds.Width, vpH = (int)bounds.Height;
            if (vpW > 0 && vpH > 0)
            {
                var terrain = _vm.Document.Terrain;
                var objects = _vm.Document.GetObjectInstances();
                var picked = TerrainEditor.PickObject((int)pos.X, (int)pos.Y, vpW, vpH,
                    Viewport.Camera, terrain, objects);
                if (picked.HasValue && picked.Value.SourceNode != null)
                    SelectObject(picked.Value.SourceNode);
                if (terrain != null)
                    _newObjPosition = TerrainEditor.GetWorldHitPosition((int)pos.X, (int)pos.Y, vpW, vpH,
                        Viewport.Camera, terrain);
                ShowObjectContextMenu();
            }
        }

        if (!props.IsRightButtonPressed)
        {
            _rmbDown = false;
            _keysDown.Clear();
            StopFlyTimer();
        }
    }

    private void OnViewportPointerMoved(object? sender, PointerEventArgs e)
    {
        var pos = e.GetPosition(ViewportPanel);
        float dx = (float)(pos.X - _lastMousePos.X);
        float dy = (float)(pos.Y - _lastMousePos.Y);
        _lastMousePos = pos;
        _isClickDown = false;

        var props = e.GetCurrentPoint(ViewportPanel).Properties;
        bool left = props.IsLeftButtonPressed;
        bool right = props.IsRightButtonPressed;
        bool middle = props.IsMiddleButtonPressed;

        if (_prefs.ControlScheme == ControlScheme.Default)
            OnPointerMoved_Default(pos, dx, dy, left, right, middle, e.KeyModifiers);
        else
            OnPointerMoved_Classic(pos, dx, dy, left, right, e.KeyModifiers);
    }

    private void OnPointerMoved_Default(Point pos, float dx, float dy,
        bool left, bool right, bool middle, KeyModifiers modifiers)
    {
        // Default (UE5-style):
        //   RMB drag        = rotate (mouse look)
        //   MMB drag        = pan
        //   LMB+RMB drag    = pan (vertical = dolly, horizontal = strafe)
        //   LMB drag        = edit mode action or select-box (future)
        //   Scroll           = zoom

        bool isCameraAction = right || middle;
        if (isCameraAction)
        {
            if (left && right)
            {
                // LMB+RMB: vertical = dolly, horizontal = strafe
                Viewport.Camera.Zoom(dy);
                Viewport.Camera.Pan(dx, 0);
                InvalidateViewport();
            }
            else if (right)
            {
                Viewport.Camera.Rotate(dx, dy);
                InvalidateViewport();
            }
            else if (middle)
            {
                Viewport.Camera.Pan(dx, dy);
                InvalidateViewport();
            }
            return;
        }

        // LMB drag: edit mode action
        if (left && _vm.Document.CurrentMode != EditMode.Camera)
        {
            HandleEditModeInput((int)pos.X, (int)pos.Y, true, false, modifiers);
            return;
        }
    }

    private void OnPointerMoved_Classic(Point pos, float dx, float dy,
        bool left, bool right, KeyModifiers modifiers)
    {
        // Classic (original Delphi):
        //   LMB drag        = rotate
        //   RMB drag        = pan
        //   LMB+RMB drag    = zoom
        //   Ctrl + any      = camera in edit modes

        bool ctrlHeld = (modifiers & KeyModifiers.Control) != 0;
        if (_vm.Document.CurrentMode == EditMode.Camera || ctrlHeld)
        {
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
            return;
        }

        // In editing modes, dispatch to edit handler
        if (left || right)
            HandleEditModeInput((int)pos.X, (int)pos.Y, left, right, modifiers);
    }

    private void OnViewportPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        Viewport.Camera.ZoomWheel((float)e.Delta.Y);
        InvalidateViewport();
        e.Handled = true;
    }

    private void HandleEditModeInput(int screenX, int screenY, bool leftButton, bool rightButton, KeyModifiers modifiers)
    {
        var terrain = _vm.Document.Terrain;
        if (terrain == null) return;

        var bounds = Viewport.Bounds;
        int vpW = (int)bounds.Width;
        int vpH = (int)bounds.Height;
        if (vpW <= 0 || vpH <= 0) return;

        // In Default scheme, Shift+LMB acts as the secondary (right-click) action
        bool shiftHeld = (modifiers & KeyModifiers.Shift) != 0;
        if (_prefs.ControlScheme == ControlScheme.Default && leftButton && shiftHeld)
        {
            leftButton = false;
            rightButton = true;
        }

        // Object editing has its own hit detection (ray-sphere, not terrain-only)
        if (_vm.Document.CurrentMode == EditMode.ObjectEdit)
        {
            HandleObjectEditInput(screenX, screenY, vpW, vpH, leftButton, rightButton, modifiers);
            InvalidateViewport();
            return;
        }

        var hit = TerrainEditor.ScreenToTerrain(screenX, screenY, vpW, vpH, Viewport.Camera, terrain);
        if (!hit.Hit) return;

        switch (_vm.Document.CurrentMode)
        {
            case EditMode.HeightEdit:
                if (rightButton)
                {
                    // Right-click picks height (matches Delphi: sets currentheight + Edit3)
                    float picked = TerrainEditor.PickHeight(terrain, hit.GridX, hit.GridY,
                        _vm.Document.BrushRadius / terrain.Header.Stretch);
                    _currentHeight = picked;
                    _vm.Document.TargetHeight = picked;
                    _heightPanelUpdating = true;
                    TxtCurrentHeight.Text = picked.ToString("F2");
                    SyncHeightSlider();
                    _heightPanelUpdating = false;
                    StatusText.Text = $"Height: {picked:F2}";
                }
                else if (leftButton)
                {
                    // Left-click paints height
                    TerrainEditor.ApplyHeightBrush(terrain, hit.GridX, hit.GridY,
                        _currentHeight,
                        _vm.Document.BrushRadius / terrain.Header.Stretch,
                        _vm.Document.BrushStrength);
                    _vm.Document.NotifyTerrainChanged();
                }
                break;

            case EditMode.LightPaint:
                if (rightButton)
                {
                    // Right-click picks light color (matches Delphi: sets ColorPanel)
                    var (pr, pg, pb) = TerrainEditor.PickLight(terrain, hit.GridX, hit.GridY,
                        _vm.Document.BrushRadius / terrain.Header.Stretch);
                    UpdateLightColor(pr, pg, pb);
                    StatusText.Text = $"Picked color: R={pr} G={pg} B={pb}";
                }
                else if (leftButton)
                {
                    // Left-click paints light
                    TerrainEditor.ApplyLightBrush(terrain, hit.GridX, hit.GridY,
                        _paintR, _paintG, _paintB,
                        _vm.Document.BrushRadius / terrain.Header.Stretch,
                        _vm.Document.BrushStrength);
                    _vm.Document.NotifyTerrainChanged();
                }
                break;

            case EditMode.TriangleEdit:
                float triRadius = _vm.Document.BrushRadius / terrain.Header.Stretch;
                switch (_vm.Document.TriangleMode)
                {
                    case TriangleSubMode.SetCorner:
                        TerrainEditor.PaintTriangleSet(terrain, hit.GridX, hit.GridY, triRadius, rightButton);
                        break;
                    case TriangleSubMode.DiagDirection:
                        TerrainEditor.PaintTriangleDiagDirection(terrain, hit.GridX, hit.GridY, triRadius, rightButton);
                        break;
                    case TriangleSubMode.DiagOptimize:
                        TerrainEditor.PaintTriangleDiagOptimize(terrain, hit.GridX, hit.GridY, triRadius, rightButton);
                        break;
                    case TriangleSubMode.OptCorner:
                        TerrainEditor.PaintTriangleOptCorner(terrain, hit.GridX, hit.GridY, triRadius, rightButton);
                        break;
                }
                _vm.Document.NotifyTerrainChanged();
                break;
        }

        InvalidateViewport();
    }

    private void HandleObjectEditInput(int screenX, int screenY, int vpW, int vpH,
        bool leftButton, bool rightButton, KeyModifiers modifiers)
    {
        var terrain = _vm.Document.Terrain;
        var objects = _vm.Document.GetObjectInstances();

        if (_isClickDown && leftButton)
        {
            // Left-click: select object under cursor
            var picked = TerrainEditor.PickObject(screenX, screenY, vpW, vpH,
                Viewport.Camera, terrain, objects);

            if (picked.HasValue && picked.Value.SourceNode != null)
            {
                SelectObject(picked.Value.SourceNode);
                StatusText.Text = $"Selected object: {ObjectNames.GetDisplayName(picked.Value.ModelId)}";
            }
            else
            {
                DeselectObject();
                StatusText.Text = "No object under cursor";
            }
        }
        else if (_isClickDown && rightButton)
        {
            // Right-click: context menu (create/copy/delete)
            var picked = TerrainEditor.PickObject(screenX, screenY, vpW, vpH,
                Viewport.Camera, terrain, objects);

            if (picked.HasValue && picked.Value.SourceNode != null)
                SelectObject(picked.Value.SourceNode);

            // Get world position for object placement
            if (terrain != null)
                _newObjPosition = TerrainEditor.GetWorldHitPosition(screenX, screenY, vpW, vpH,
                    Viewport.Camera, terrain);

            ShowObjectContextMenu();
        }
        else if (!_isClickDown && leftButton && _objDragAllowed
                 && (Environment.TickCount64 - _objPressTimestamp) >= 100
                 && _vm.Document.SelectedObject != null)
        {
            // Left-drag with selected object: move to terrain position
            if (terrain == null) return;
            bool shiftHeld = (modifiers & KeyModifiers.Shift) != 0;
            if (shiftHeld)
            {
                // Shift+drag: adjust Z height along view ray
                AdjustObjectHeight(screenX, screenY, vpW, vpH);
            }
            else
            {
                // Normal drag: move on terrain surface
                var worldPos = TerrainEditor.GetWorldHitPosition(screenX, screenY, vpW, vpH,
                    Viewport.Camera, terrain);
                if (worldPos.HasValue)
                {
                    MoveSelectedObject(worldPos.Value.X, worldPos.Value.Y, worldPos.Value.Z);
                    StatusText.Text = $"Object pos: ({worldPos.Value.X:F1}, {worldPos.Value.Y:F1}, {worldPos.Value.Z:F1})";
                }
            }
        }
    }

    private Vector3? _newObjPosition;

    private bool _suppressOptionalLeafToggle;

    private void SelectObject(TreeNode objNode)
    {
        _suppressOptionalLeafToggle = true;
        _vm.Document.SelectedObject = objNode;
        ObjPropsPanel.IsVisible = true;

        int typeId = objNode.FindChildLeaf("Type")?.Int32Value ?? 0;
        PropObjType.Text = ObjectNames.GetDisplayName(typeId);
        PropObjX.Text = (objNode.FindChildLeaf("X")?.SingleValue ?? 0).ToString("F2");
        PropObjY.Text = (objNode.FindChildLeaf("Y")?.SingleValue ?? 0).ToString("F2");
        PropObjZ.Text = (objNode.FindChildLeaf("Z")?.SingleValue ?? 0).ToString("F2");
        PropObjAngle.Text = (objNode.FindChildLeaf("DirFacing")?.SingleValue ?? 0).ToString("F2");

        // Tilt (optional: TiltForward / TiltLeft from ObjectRef6)
        var tiltFwdLeaf = objNode.FindChildLeaf("TiltForward");
        bool hasTilt = tiltFwdLeaf != null;
        ChkObjTilt.IsChecked = hasTilt;
        PanelTilt.IsVisible = hasTilt;
        PropObjTiltFwd.IsEnabled = hasTilt;
        PropObjTiltLeft.IsEnabled = hasTilt;
        if (hasTilt)
        {
            PropObjTiltFwd.Text = (tiltFwdLeaf?.SingleValue ?? 0).ToString("F2");
            PropObjTiltLeft.Text = (objNode.FindChildLeaf("TiltLeft")?.SingleValue ?? 0).ToString("F2");
        }

        // Scale (optional)
        var scaleLeaf = objNode.FindChildLeaf("Scale");
        ChkObjScale.IsChecked = scaleLeaf != null;
        PropObjScale.IsEnabled = scaleLeaf != null;
        PropObjScale.Text = (scaleLeaf?.SingleValue ?? 1f).ToString("F2");

        // AIMode (optional)
        var aiLeaf = objNode.FindChildLeaf("AIMode");
        ChkObjAIMode.IsChecked = aiLeaf != null;
        PropObjAIMode.IsEnabled = aiLeaf != null;
        PropObjAIMode.Text = (aiLeaf?.ByteValue ?? 0).ToString();

        // TeamID (optional)
        var teamLeaf = objNode.FindChildLeaf("TeamID");
        ChkObjTeamID.IsChecked = teamLeaf != null;
        PropObjTeamID.IsEnabled = teamLeaf != null;
        PropObjTeamID.Text = (teamLeaf?.Int32Value ?? 0).ToString();

        PropHeader.Text = $"Object: {PropObjType.Text}";
        _suppressOptionalLeafToggle = false;
    }

    private void DeselectObject()
    {
        _vm.Document.SelectedObject = null;
        ObjPropsPanel.IsVisible = false;
        PropHeader.Text = "No selection";
    }

    private void MoveSelectedObject(float x, float y, float z)
    {
        var obj = _vm.Document.SelectedObject;
        if (obj == null) return;

        obj.FindChildLeaf("X")?.SetSingle(x);
        obj.FindChildLeaf("Y")?.SetSingle(y);
        obj.FindChildLeaf("Z")?.SetSingle(z);

        PropObjX.Text = x.ToString("F2");
        PropObjY.Text = y.ToString("F2");
        PropObjZ.Text = z.ToString("F2");

        _vm.Document.NotifyWorldChanged();
    }

    private void AdjustObjectHeight(int screenX, int screenY, int vpW, int vpH)
    {
        var obj = _vm.Document.SelectedObject;
        if (obj == null) return;

        // Compute new Z by projecting the ray onto the vertical axis through the object
        float fovRad = Viewport.Camera.FieldOfView / 360f * 2f * MathF.PI;
        float fovFactor = 2f * MathF.Tan(fovRad / 2f) / vpH;
        float x2 = -(screenX - vpW / 2f) * fovFactor;
        float y2 = -(screenY - vpH / 2f) * fovFactor;

        var ray = Viewport.Camera.Forward + x2 * Viewport.Camera.Right + y2 * Viewport.Camera.Up;
        var eye = Viewport.Camera.Position;

        float objX = obj.FindChildLeaf("X")?.SingleValue ?? 0;
        float objY = obj.FindChildLeaf("Y")?.SingleValue ?? 0;
        float objZ = obj.FindChildLeaf("Z")?.SingleValue ?? 0;

        // Project ray to find where it's closest to the object's vertical line
        float denom = ray.X * ray.X + ray.Y * ray.Y;
        if (denom < 1e-9f) return;

        float s = ((objX - eye.X) * (ray.Z * ray.X)
                 + (objY - eye.Y) * (ray.Z * ray.Y)
                 + (objZ - eye.Z) * (-denom))
                 / denom;

        float newZ = s + objZ;
        obj.FindChildLeaf("Z")?.SetSingle(newZ);
        PropObjZ.Text = newZ.ToString("F2");

        _vm.Document.NotifyWorldChanged();
    }

    private void ShowObjectContextMenu()
    {
        var menu = new Avalonia.Controls.ContextMenu();
        var hasSelection = _vm.Document.SelectedObject != null;

        if (hasSelection)
        {
            var deleteItem = new Avalonia.Controls.MenuItem { Header = "Delete Object" };
            deleteItem.Click += (_, _) =>
            {
                _vm.Document.RemoveSelectedObject();
                DeselectObject();
                RefreshViewport();
            };
            menu.Items.Add(deleteItem);

            var copyItem = new Avalonia.Controls.MenuItem { Header = "Copy Object Here" };
            copyItem.Click += (_, _) => CreateObjectCopy();
            menu.Items.Add(copyItem);
        }

        if (_newObjPosition.HasValue)
        {
            var createItem = new Avalonia.Controls.MenuItem { Header = "Create New Object..." };
            createItem.Click += async (_, _) => await CreateNewObjectAtPosition();
            menu.Items.Add(createItem);
        }

        if (menu.Items.Count > 0)
        {
            ViewportPanel.ContextMenu = menu;
            menu.Open(ViewportPanel);
            menu.Closed += (_, _) => ViewportPanel.ContextMenu = null;
        }
    }

    private void CreateObjectCopy()
    {
        var src = _vm.Document.SelectedObject;
        if (src == null || !_newObjPosition.HasValue) return;

        int typeId = src.FindChildLeaf("Type")?.Int32Value ?? 0;
        float angle = src.FindChildLeaf("DirFacing")?.SingleValue ?? 0;
        var pos = _newObjPosition.Value;

        var newObj = _vm.Document.AddObject(typeId, pos.X, pos.Y, pos.Z, angle);
        if (newObj != null)
        {
            // Copy scale if present
            var scaleSrc = src.FindChildLeaf("Scale");
            if (scaleSrc != null)
                newObj.AddSingle("Scale", scaleSrc.SingleValue);

            SelectObject(newObj);
        }
        RefreshViewport();
    }

    private async Task CreateNewObjectAtPosition()
    {
        if (!_newObjPosition.HasValue) return;

        var dialog = new Window
        {
            Title = "New Object",
            Width = 300,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };
        var panel = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "Object type (name or ID):" });
        var txtType = new TextBox { Text = ObjectNames.GetName(679) };
        panel.Children.Add(txtType);
        var btnOk = new Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        panel.Children.Add(btnOk);
        dialog.Content = panel;

        int? typeId = null;
        btnOk.Click += (_, _) =>
        {
            typeId = ObjectNames.ParseInput(txtType.Text ?? "");
            dialog.Close();
        };

        await dialog.ShowDialog(this);
        if (typeId.HasValue && typeId.Value > 0)
        {
            var pos = _newObjPosition.Value;
            var newObj = _vm.Document.AddObject(typeId.Value, pos.X, pos.Y, pos.Z);
            if (newObj != null)
                SelectObject(newObj);
            RefreshViewport();
        }
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
            DrawRealObjects = _drawRealObjects,
            ViewObjThruTerrain = _viewObjThruTerrain,
            SelectedObjectNode = _vm.Document.SelectedObject,
            Objects = _vm.Document.GetObjectInstances(),
            SplineLines = _vm.Document.GetSplineLines()
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
            LoadTerrainTexture("GroundTexture", v => { texInfo.GroundImage = v.img; texInfo.GroundWrap = v.wrap; });
            LoadTerrainTexture("SlopeTexture", v => { texInfo.SlopeImage = v.img; texInfo.SlopeWrap = v.wrap; });
            LoadTerrainTexture("WallTexture", v => { texInfo.WallImage = v.img; texInfo.WallWrap = v.wrap; });

            if (texInfo.GroundImage != null || texInfo.SlopeImage != null || texInfo.WallImage != null)
                terrainData.Textures = texInfo;
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

    #endregion

    #region File menu handlers

    private async Task NewWorldAsync()
    {
        var dlg = new NewMapDialog();
        await dlg.ShowDialog(this);

        if (dlg.Confirmed)
        {
            CloseOwnedWindows();
            _vm.Document.NewWorld(dlg.MapWidth, dlg.MapHeight, dlg.TextureName);
            StatusText.Text = $"New map: {dlg.MapWidth}x{dlg.MapHeight}";
        }
    }

    private async Task OpenWorldAsync()
    {
        IStorageFolder? startFolder = null;
        if (!string.IsNullOrEmpty(_prefs.LastOpenFolder) && Directory.Exists(_prefs.LastOpenFolder))
            startFolder = await StorageProvider.TryGetFolderFromPathAsync(_prefs.LastOpenFolder);

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Map",
            AllowMultiple = false,
            SuggestedStartLocation = startFolder,
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
                _prefs.LastOpenFolder = System.IO.Path.GetDirectoryName(path) ?? "";
                _prefs.Save();

                try
                {
                    CloseOwnedWindows();
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
            DefaultExtension = "gck",
            FileTypeChoices =
            [
                new FilePickerFileType("GCK Map Archive") { Patterns = ["*.gck"] }
            ]
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
                CloseOwnedWindows();
                _vm.Document.LoadWorld(path);
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
        win.Closed += (_, _) => UploadTerrainToGpu();
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

        // Apply object type (accepts name, ID, or "Name (ID)" format)
        var parsedType = ObjectNames.ParseInput(PropObjType.Text ?? "");
        bool typeChanged = false;
        if (parsedType.HasValue)
        {
            var typeLeaf = obj.FindChildLeaf("Type");
            if (typeLeaf != null && typeLeaf.Int32Value != parsedType.Value)
            {
                typeLeaf.Int32Value = parsedType.Value;
                typeChanged = true;
            }
            PropObjType.Text = ObjectNames.GetDisplayName(parsedType.Value);
            PropHeader.Text = $"Object: {PropObjType.Text}";
        }

        if (float.TryParse(PropObjX.Text, out float x))
            obj.FindChildLeaf("X")?.SetSingle(x);
        if (float.TryParse(PropObjY.Text, out float y))
            obj.FindChildLeaf("Y")?.SetSingle(y);
        if (float.TryParse(PropObjZ.Text, out float z))
            obj.FindChildLeaf("Z")?.SetSingle(z);
        if (float.TryParse(PropObjAngle.Text, out float angle))
            obj.FindChildLeaf("DirFacing")?.SetSingle(angle);
        if (ChkObjTilt.IsChecked == true)
        {
            if (float.TryParse(PropObjTiltFwd.Text, out float tf))
                obj.FindChildLeaf("TiltForward")?.SetSingle(tf);
            if (float.TryParse(PropObjTiltLeft.Text, out float tl))
                obj.FindChildLeaf("TiltLeft")?.SetSingle(tl);
        }

        // Apply optional fields only if their checkbox is checked
        if (ChkObjScale.IsChecked == true && float.TryParse(PropObjScale.Text, out float scale))
            obj.FindChildLeaf("Scale")?.SetSingle(scale);
        if (ChkObjAIMode.IsChecked == true && byte.TryParse(PropObjAIMode.Text, out byte aiMode))
        {
            var leaf = obj.FindChildLeaf("AIMode");
            if (leaf != null) leaf.ByteValue = aiMode;
        }
        if (ChkObjTeamID.IsChecked == true && int.TryParse(PropObjTeamID.Text, out int teamId))
        {
            var leaf = obj.FindChildLeaf("TeamID");
            if (leaf != null) leaf.Int32Value = teamId;
        }

        if (typeChanged && _drawRealObjects)
        {
            var objects = _vm.Document.GetObjectInstances();
            Viewport.QueueGlAction(renderer => _modelManager.PreloadModels(objects, renderer));
        }

        InvalidateViewport();
        StatusText.Text = "Object properties applied";
    }

    private void ToggleOptionalLeaf(string name, bool enabled, TextBox textBox, float defaultValue)
    {
        if (_suppressOptionalLeafToggle) return;
        var obj = _vm.Document.SelectedObject;
        if (obj == null) return;

        if (enabled)
        {
            obj.AddSingle(name, defaultValue);
            textBox.Text = defaultValue.ToString("F2");
        }
        else
        {
            var leaf = obj.FindChildLeaf(name);
            if (leaf != null) obj.RemoveLeaf(leaf);
        }
        InvalidateViewport();
    }

    private void ToggleOptionalByteLeaf(string name, bool enabled, TextBox textBox, byte defaultValue)
    {
        if (_suppressOptionalLeafToggle) return;
        var obj = _vm.Document.SelectedObject;
        if (obj == null) return;

        if (enabled)
        {
            obj.AddByte(name, defaultValue);
            textBox.Text = defaultValue.ToString();
        }
        else
        {
            var leaf = obj.FindChildLeaf(name);
            if (leaf != null) obj.RemoveLeaf(leaf);
        }
        InvalidateViewport();
    }

    private void ToggleOptionalInt32Leaf(string name, bool enabled, TextBox textBox, int defaultValue)
    {
        if (_suppressOptionalLeafToggle) return;
        var obj = _vm.Document.SelectedObject;
        if (obj == null) return;

        if (enabled)
        {
            obj.AddInt32(name, defaultValue);
            textBox.Text = defaultValue.ToString();
        }
        else
        {
            var leaf = obj.FindChildLeaf(name);
            if (leaf != null) obj.RemoveLeaf(leaf);
        }
        InvalidateViewport();
    }

    private void ToggleTilt()
    {
        if (_suppressOptionalLeafToggle) return;
        var obj = _vm.Document.SelectedObject;
        if (obj == null) return;

        bool enable = ChkObjTilt.IsChecked == true;
        PanelTilt.IsVisible = enable;
        PropObjTiltFwd.IsEnabled = enable;
        PropObjTiltLeft.IsEnabled = enable;

        if (enable)
        {
            obj.AddSingle("TiltForward", 0);
            obj.AddSingle("TiltLeft", 0);
            PropObjTiltFwd.Text = "0.00";
            PropObjTiltLeft.Text = "0.00";
        }
        else
        {
            var fwd = obj.FindChildLeaf("TiltForward");
            if (fwd != null) obj.RemoveLeaf(fwd);
            var left = obj.FindChildLeaf("TiltLeft");
            if (left != null) obj.RemoveLeaf(left);
        }

        InvalidateViewport();
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
        dlg.SetInitialValues(_prefs.GamePath, _prefs.ControlScheme);
        await dlg.ShowDialog(this);

        if (!dlg.Confirmed) return;

        _prefs.GamePath = dlg.GamePath;
        _prefs.ControlScheme = dlg.ControlScheme;
        _prefs.Save();
        _modelManager.SetGamePath(dlg.GamePath);

        StatusText.Text = _modelManager.HasGameData
            ? $"Game path set — {dlg.GamePath}"
            : "No .gzp files found in bin/ folder";
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

    #endregion
}