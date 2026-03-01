using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using GiantsEdit.App.Dialogs;
using GiantsEdit.App.ViewModels;
using GiantsEdit.Core.DataModel;
using GiantsEdit.Core.Services;

namespace GiantsEdit.App;

public partial class MainWindow : Window
{
    private const float HeightSliderScale = 1000f;

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
        ApplyTheme(_prefs.Theme);
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
        MenuSubdivide.Click += async (_, _) => await SubdivideTerrainAsync();
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
                _currentHeight = _minimumHeight + (_maximumHeight - _minimumHeight) * (float)SliderHeight.Value / HeightSliderScale;
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

        // Property panel — herd type dropdown (reuses same ObjectNames list)
        LstHerdTypes.ItemsSource = allObjNames;
        BtnHerdTypeDropdown.Click += (_, _) =>
        {
            TxtHerdTypeSearch.Text = "";
            LstHerdTypes.ItemsSource = allObjNames;
            HerdTypePopup.IsOpen = !HerdTypePopup.IsOpen;
            if (HerdTypePopup.IsOpen)
                TxtHerdTypeSearch.Focus();
        };
        TxtHerdTypeSearch.TextChanged += (_, _) =>
        {
            var filter = TxtHerdTypeSearch.Text;
            if (string.IsNullOrEmpty(filter))
            {
                LstHerdTypes.ItemsSource = allObjNames;
                return;
            }
            LstHerdTypes.ItemsSource = ObjectNames.Search(filter);
        };
        LstHerdTypes.SelectionChanged += (_, _) =>
        {
            if (LstHerdTypes.SelectedItem is string selected)
            {
                PropHerdType.Text = selected;
                HerdTypePopup.IsOpen = false;
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
        ChkObjLightColor.IsCheckedChanged += (_, _) => ToggleLightColor();
        ChkObjOData.IsCheckedChanged += (_, _) => ToggleOData();
        ChkObjHerdMarkers.IsCheckedChanged += (_, _) => ToggleHerdMarkers();
        ChkObjHerdType.IsCheckedChanged += (_, _) =>
        {
            PropHerdType.IsEnabled = ChkObjHerdType.IsChecked == true;
            if (_suppressOptionalLeafToggle) return;
            var o = _vm.Document.SelectedObject;
            if (o == null) return;
            if (ChkObjHerdType.IsChecked == true)
            {
                o.AddInt32("HerdType", 0);
                PropHerdType.Text = ObjectNames.GetDisplayName(0);
            }
            else
            {
                var leaf = o.FindChildLeaf("HerdType");
                if (leaf != null) o.RemoveLeaf(leaf);
            }
            InvalidateViewport();
        };
        ChkObjHerdCount.IsCheckedChanged += (_, _) => ToggleHerdCount();
        ChkObjSplineKeyTime.IsCheckedChanged += (_, _) =>
        {
            PropSplineKeyTime.IsEnabled = ChkObjSplineKeyTime.IsChecked == true;
            ToggleOptionalInt32Leaf("KeyTime", ChkObjSplineKeyTime.IsChecked == true, PropSplineKeyTime, 0);
        };
        ChkObjSplinePath3D.IsCheckedChanged += (_, _) => ToggleSplinePath3D();
        ChkObjSplineScale.IsCheckedChanged += (_, _) => ToggleSplineScale();
        ChkObjSplineStartId.IsCheckedChanged += (_, _) =>
        {
            PropSplineStartId.IsEnabled = ChkObjSplineStartId.IsChecked == true;
            ToggleOptionalByteLeaf("StartId", ChkObjSplineStartId.IsChecked == true, PropSplineStartId, 0);
        };
        ChkObjSplineTangents.IsCheckedChanged += (_, _) => ToggleSplineTangents();
        ChkObjSplineJet.IsCheckedChanged += (_, _) => ToggleSplineJet();
        ChkObjAnimType.IsCheckedChanged += (_, _) =>
        {
            PropAnimType.IsEnabled = ChkObjAnimType.IsChecked == true;
            ToggleOptionalByteLeaf("AnimType", ChkObjAnimType.IsChecked == true, PropAnimType, 0);
        };
        ChkObjAnimTime.IsCheckedChanged += (_, _) =>
        {
            PropAnimTime.IsEnabled = ChkObjAnimTime.IsChecked == true;
            ToggleOptionalLeaf("AnimTime", ChkObjAnimTime.IsChecked == true, PropAnimTime, 0);
        };
        ChkObjPath.IsCheckedChanged += (_, _) => TogglePath();
        ChkObjWind.IsCheckedChanged += (_, _) => ToggleWind();
        ChkObjFlickUsed.IsCheckedChanged += (_, _) => ToggleFlickUsed();
        ChkObjAIData.IsCheckedChanged += (_, _) => ToggleAIData();
        ChkObjMinishopR.IsCheckedChanged += (_, _) => ToggleMinishopR();
        ChkObjMinishopM.IsCheckedChanged += (_, _) => ToggleMinishopM();
        ChkObjHerdPoint.IsCheckedChanged += (_, _) => ToggleHerdPoint();

        // Icon picker buttons
        BtnEditMinishopR.Click += (_, _) => OpenIconPicker("Reaper Icons", "RIcon", TxtMinishopR, IconNames.ReaperIcons);
        BtnEditMinishopM.Click += (_, _) => OpenIconPicker("Mecc Icons", "MIcon", TxtMinishopM, IconNames.MeccIcons);

        // Document events
        _vm.Document.WorldChanged += () =>
        {
            UploadTerrainToGpu();
            if (_modelManager.HasGameData)
            {
                LoadDomeFromGameData();
                if (_drawRealObjects)
                {
                    var objects = _vm.Document.GetObjectInstances();
                    Viewport.QueueGlAction(renderer => _modelManager.PreloadModels(objects, renderer));
                }
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
            SliderHeight.Value = (_currentHeight - _minimumHeight) / (_maximumHeight - _minimumHeight) * HeightSliderScale;
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

}