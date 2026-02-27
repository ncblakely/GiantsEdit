using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using GiantsEdit.App.Rendering;
using GiantsEdit.Core.Formats;
using GiantsEdit.Core.Rendering;
using Silk.NET.OpenGL;

namespace GiantsEdit.App.Controls;

/// <summary>
/// Avalonia control that hosts an OpenGL viewport for 3D rendering.
/// Wraps Avalonia's OpenGlControlBase with Silk.NET bindings and the EditorCamera.
/// </summary>
public class OpenGlViewport : OpenGlControlBase
{
    private GL? _gl;
    private OpenGlRenderer? _renderer;
    private bool _initialized;
    private Point _lastMousePos;
    private MouseButton _activeButton;
    private TerrainRenderData? _pendingTerrain;
    private MapObjectReader? _pendingMapObjects;
    private Action<IRenderer>? _pendingGlAction;

    public EditorCamera Camera { get; } = new();

    public RenderState? CurrentRenderState { get; set; }

    /// <summary>Raised when the viewport needs to rebuild the render state.</summary>
    public event Action? RenderStateNeeded;

    /// <summary>
    /// Queues terrain data for upload on the next GL render frame.
    /// Safe to call from any thread.
    /// </summary>
    public void QueueTerrainUpload(TerrainRenderData terrain)
    {
        _pendingTerrain = terrain;
        Invalidate();
    }

    public void QueueMapObjectsUpload(MapObjectReader mapObjects)
    {
        _pendingMapObjects = mapObjects;
        Invalidate();
    }

    /// <summary>
    /// Queues an action to run on the GL thread during the next render frame.
    /// </summary>
    public void QueueGlAction(Action<IRenderer> action)
    {
        _pendingGlAction = action;
        Invalidate();
    }

    public OpenGlViewport()
    {
        Focusable = true;
    }

    protected override void OnOpenGlInit(GlInterface glInterface)
    {
        base.OnOpenGlInit(glInterface);

        _gl = GL.GetApi(glInterface.GetProcAddress);
        _renderer = new OpenGlRenderer();
        _renderer.SetGlContext(_gl);

        var scaling = VisualRoot?.RenderScaling ?? 1.0;
        var bounds = Bounds;
        int w = Math.Max(1, (int)(bounds.Width * scaling));
        int h = Math.Max(1, (int)(bounds.Height * scaling));
        _renderer.Init(w, h);
        _initialized = true;
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        _renderer?.Cleanup();
        _renderer = null;
        _gl?.Dispose();
        _gl = null;
        _initialized = false;

        base.OnOpenGlDeinit(gl);
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (!_initialized || _renderer == null || _gl == null) return;

        // Bind Avalonia's framebuffer — required for correct rendering
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)fb);

        // Use physical pixel dimensions for GL viewport (framebuffer is at physical size)
        var scaling = VisualRoot?.RenderScaling ?? 1.0;
        var bounds = Bounds;
        int w = Math.Max(1, (int)(bounds.Width * scaling));
        int h = Math.Max(1, (int)(bounds.Height * scaling));
        _renderer.Resize(w, h);

        // Process pending terrain upload on the GL thread
        var pending = _pendingTerrain;
        if (pending != null)
        {
            _pendingTerrain = null;
            _renderer.UploadTerrain(pending);
        }

        var pendingMapObj = _pendingMapObjects;
        if (pendingMapObj != null)
        {
            _pendingMapObjects = null;
            _renderer.UploadMapObjects(pendingMapObj);
        }

        var pendingGl = _pendingGlAction;
        if (pendingGl != null)
        {
            _pendingGlAction = null;
            pendingGl(_renderer);
        }

        // Ask the host to provide render state if we don't have one
        if (CurrentRenderState == null)
            RenderStateNeeded?.Invoke();

        // Build a default render state from camera if none provided
        var state = CurrentRenderState ?? new RenderState
        {
            ViewMatrix = Camera.GetViewMatrix(),
            ProjectionMatrix = Camera.GetProjectionMatrix((float)bounds.Width / (float)Math.Max(1, bounds.Height)),
            CameraPosition = Camera.Position
        };

        _renderer.Render(state);
    }

    /// <summary>
    /// Provides access to the renderer for uploading terrain/models.
    /// </summary>
    public IRenderer? Renderer => _renderer;

    /// <summary>
    /// Requests a repaint of the viewport.
    /// </summary>
    public void Invalidate()
    {
        Dispatcher.UIThread.Post(RequestNextFrameRendering, DispatcherPriority.Render);
    }

    #region Mouse input

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _lastMousePos = e.GetPosition(this);
        _activeButton = GetButton(e);
        e.Handled = true;
        Focus();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _activeButton = MouseButton.None;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var pos = e.GetPosition(this);
        float dx = (float)(pos.X - _lastMousePos.X);
        float dy = (float)(pos.Y - _lastMousePos.Y);
        _lastMousePos = pos;

        var props = e.GetCurrentPoint(this).Properties;
        bool left = props.IsLeftButtonPressed;
        bool right = props.IsRightButtonPressed;

        if (left && right)
        {
            Camera.Zoom(dy);
            CurrentRenderState = null;
            Invalidate();
        }
        else if (left)
        {
            Camera.Rotate(dx, dy);
            CurrentRenderState = null;
            Invalidate();
        }
        else if (right)
        {
            Camera.Pan(dx, dy);
            CurrentRenderState = null;
            Invalidate();
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        Camera.ZoomWheel((float)e.Delta.Y);
        CurrentRenderState = null;
        Invalidate();
        e.Handled = true;
    }

    private static MouseButton GetButton(PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(null).Properties;
        if (props.IsLeftButtonPressed) return MouseButton.Left;
        if (props.IsRightButtonPressed) return MouseButton.Right;
        if (props.IsMiddleButtonPressed) return MouseButton.Middle;
        return MouseButton.None;
    }

    private enum MouseButton { None, Left, Right, Middle }

    #endregion
}
