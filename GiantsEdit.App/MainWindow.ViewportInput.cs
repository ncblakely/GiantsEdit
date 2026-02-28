using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using GiantsEdit.Core.DataModel;
using GiantsEdit.Core.Rendering;
using GiantsEdit.Core.Services;

namespace GiantsEdit.App;

public partial class MainWindow
{
    // Timer intervals
    private const int FlyTimerIntervalMs = 16;
    private const int ObjDragDelayMs = 100;

    // Default object type for "Create New Object" dialog
    private const int DefaultNewObjectType = 679;

    // Object type: directional light
    private const int ObjectTypeLightDirectional = 1004;

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
                var objects = GetObjectInstancesForPicking();
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
        var objects = GetObjectInstancesForPicking();

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
                 && (Environment.TickCount64 - _objPressTimestamp) >= ObjDragDelayMs
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

        // Light Color (optional)
        var lightR = objNode.FindChildLeaf("LightColorR");
        ChkObjLightColor.IsChecked = lightR != null;
        PanelLightColor.IsVisible = lightR != null;
        if (lightR != null)
        {
            PropLightR.Text = (lightR.SingleValue).ToString("F4");
            PropLightG.Text = (objNode.FindChildLeaf("LightColorG")?.SingleValue ?? 0).ToString("F4");
            PropLightB.Text = (objNode.FindChildLeaf("LightColorB")?.SingleValue ?? 0).ToString("F4");
        }

        // OData (optional)
        var odata1 = objNode.FindChildLeaf("OData1");
        ChkObjOData.IsChecked = odata1 != null;
        PanelOData.IsVisible = odata1 != null;
        if (odata1 != null)
        {
            PropOData1.Text = (odata1.SingleValue).ToString("F4");
            PropOData2.Text = (objNode.FindChildLeaf("OData2")?.SingleValue ?? 0).ToString("F4");
            PropOData3.Text = (objNode.FindChildLeaf("OData3")?.SingleValue ?? 0).ToString("F4");
        }

        // Herd markers (optional)
        var numMarkers = objNode.FindChildLeaf("NumMarkers");
        ChkObjHerdMarkers.IsChecked = numMarkers != null;
        PanelHerdMarkers.IsVisible = numMarkers != null;
        if (numMarkers != null)
        {
            PropHerdNum.Text = numMarkers.Int32Value.ToString();
            PropHerdMarkerType.Text = (objNode.FindChildLeaf("MarkerType")?.Int32Value ?? 0).ToString();
            PropHerdShowRadius.Text = (objNode.FindChildLeaf("ShowRadius")?.Int32Value ?? 0).ToString();
        }

        // Herd type (optional — object type ID)
        var herdType = objNode.FindChildLeaf("HerdType");
        ChkObjHerdType.IsChecked = herdType != null;
        PropHerdType.IsEnabled = herdType != null;
        PropHerdType.Text = herdType != null ? ObjectNames.GetDisplayName(herdType.Int32Value) : "";

        // Herd count (optional)
        var teamCount = objNode.FindChildLeaf("TeamCount");
        ChkObjHerdCount.IsChecked = teamCount != null;
        PanelHerdCount.IsVisible = teamCount != null;
        if (teamCount != null)
        {
            PropTeamCount.Text = teamCount.ByteValue.ToString();
        }

        // Spline key time (optional)
        var keyTime = objNode.FindChildLeaf("KeyTime");
        ChkObjSplineKeyTime.IsChecked = keyTime != null;
        PropSplineKeyTime.IsEnabled = keyTime != null;
        PropSplineKeyTime.Text = (keyTime?.Int32Value ?? 0).ToString();

        // Spline path 3D (optional, marker node)
        var path3D = objNode.FindChildNode("SplinePath3D");
        ChkObjSplinePath3D.IsChecked = path3D != null;

        // Spline scale (optional)
        var inScale = objNode.FindChildLeaf("InScale");
        ChkObjSplineScale.IsChecked = inScale != null;
        PanelSplineScale.IsVisible = inScale != null;
        if (inScale != null)
        {
            PropSplineScaleIn.Text = inScale.SingleValue.ToString("F4");
            PropSplineScaleOut.Text = (objNode.FindChildLeaf("OutScale")?.SingleValue ?? 0).ToString("F4");
        }

        // Spline start ID (optional)
        var startId = objNode.FindChildLeaf("StartId");
        ChkObjSplineStartId.IsChecked = startId != null;
        PropSplineStartId.IsEnabled = startId != null;
        PropSplineStartId.Text = (startId?.ByteValue ?? 0).ToString();

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
        var txtType = new TextBox { Text = ObjectNames.GetName(DefaultNewObjectType) };
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

    #region Fly camera (WASD)

    private void StartFlyTimer()
    {
        if (_flyTimer != null) return;
        _flyTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FlyTimerIntervalMs) };
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

    #endregion
}
