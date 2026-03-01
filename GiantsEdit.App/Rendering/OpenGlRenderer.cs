using GiantsEdit.Core.Formats;
using GiantsEdit.Core.Rendering;
using Silk.NET.OpenGL;

namespace GiantsEdit.App.Rendering;

/// <summary>
/// OpenGL ES 3.0 renderer implementing IRenderer.
/// Uses Silk.NET for GL bindings, runs under Avalonia's ANGLE context.
/// Orchestrates specialised sub-renderers for each visual layer.
/// </summary>
public sealed class OpenGlRenderer : IRenderer
{
    private GL _gl = null!;
    private int _viewportWidth;
    private int _viewportHeight;

    // MSAA framebuffer
    private uint _msaaFbo;
    private uint _msaaColorRbo;
    private uint _msaaDepthRbo;
    private int _msaaSamples;
    private int _msaaWidth;
    private int _msaaHeight;
    private uint _avaloniaFbo;

    // Shaders
    private uint _terrainShader;
    private uint _solidShader;
    private uint _modelShader;
    private uint _domeShader;

    // Sub-renderers
    private TerrainRenderer _terrain = null!;
    private SeaRenderer _sea = null!;
    private DomeRenderer _dome = null!;
    private ModelRenderer _models = null!;
    private SplineRenderer _splines = null!;

    /// <summary>
    /// Sets the GL context. Must be called before Init().
    /// </summary>
    public void SetGlContext(GL gl)
    {
        _gl = gl;
    }

    public unsafe void Init(int viewportWidth, int viewportHeight)
    {
        _viewportWidth = viewportWidth;
        _viewportHeight = viewportHeight;

        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.Enable(EnableCap.CullFace);
        _gl.CullFace(TriangleFace.Back);
        _gl.ClearColor(0.05f, 0.05f, 0.1f, 1.0f);

        // Query max MSAA samples supported (GL_MAX_SAMPLES = 0x8D57)
        int maxSamples = 0;
        _gl.GetInteger((GetPName)0x8D57, &maxSamples);
        _msaaSamples = Math.Clamp(maxSamples, 1, 16);

        // Compile shaders
        _terrainShader = ShaderCompiler.CreateShader(_gl, ShaderSources.TerrainVert, ShaderSources.TerrainFrag);
        _solidShader = ShaderCompiler.CreateShader(_gl, ShaderSources.SolidVert, ShaderSources.SolidFrag);
        _modelShader = ShaderCompiler.CreateShader(_gl, ShaderSources.ModelVert, ShaderSources.ModelFrag);
        _domeShader = ShaderCompiler.CreateShader(_gl, ShaderSources.DomeVert, ShaderSources.DomeFrag);

        // Solid shader uniform locations (shared by sea, splines, and model selection box)
        int solidMvpLoc = _gl.GetUniformLocation(_solidShader, "uMVP");
        int solidColorLoc = _gl.GetUniformLocation(_solidShader, "uColor");

        // Initialise sub-renderers
        _terrain = new TerrainRenderer(_gl, _terrainShader);
        _sea = new SeaRenderer(_gl, _solidShader, solidMvpLoc, solidColorLoc);
        _dome = new DomeRenderer(_gl, _domeShader);
        _models = new ModelRenderer(_gl, _modelShader, _solidShader, solidMvpLoc, solidColorLoc);
        _splines = new SplineRenderer(_gl, _solidShader, solidMvpLoc, solidColorLoc);

        _sea.Build(10240f);
        _splines.Init();

        // Share the line VAO/VBO with model renderer (for selection box drawing)
        _models.SetLineBuffers(_splines.LineVao, _splines.LineVbo);

        if (_msaaSamples > 1)
            CreateMsaaFbo(viewportWidth, viewportHeight);
    }

    public void Resize(int width, int height)
    {
        _viewportWidth = width;
        _viewportHeight = height;
        _gl.Viewport(0, 0, (uint)width, (uint)height);

        if (_msaaSamples > 1 && (width != _msaaWidth || height != _msaaHeight))
            CreateMsaaFbo(width, height);
    }

    /// <summary>
    /// Stores Avalonia's framebuffer and binds the MSAA FBO if available.
    /// </summary>
    public void BeginRender(uint avaloniaFb)
    {
        _avaloniaFbo = avaloniaFb;
        if (_msaaSamples > 1 && _msaaFbo != 0)
        {
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _msaaFbo);
        }
    }

    /// <summary>
    /// Blits the MSAA FBO to Avalonia's framebuffer if MSAA is active.
    /// </summary>
    public void EndRender()
    {
        if (_msaaSamples > 1 && _msaaFbo != 0)
        {
            _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _msaaFbo);
            _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _avaloniaFbo);
            _gl.BlitFramebuffer(0, 0, _msaaWidth, _msaaHeight,
                                0, 0, _msaaWidth, _msaaHeight,
                                ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _avaloniaFbo);
        }
    }

    public void Render(RenderState state)
    {
        _gl.ClearColor(0.05f, 0.05f, 0.1f, 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        var vp = state.ViewMatrix * state.ProjectionMatrix;

        if (state.ShowDome && _dome.HasData)
            _dome.Draw(vp);

        if (state.ShowSea && _sea.HasData)
            _sea.Draw(vp, state.SeaColor);

        if (state.ShowTerrain && _terrain.HasData)
            _terrain.Draw(vp, state.ShowTerrainMesh);

        if (state.ShowObjects)
            _models.Draw(state, vp);

        if (state.ShowObjects && state.SplineLines.Count > 0)
            _splines.Draw(state, vp);

        if (state.ShowObjects && state.SelectedObjectNode != null)
            _models.DrawSelectionBox(state, vp);

        _gl.BindVertexArray(0);
        _gl.UseProgram(0);
    }

    public void UploadTerrain(TerrainRenderData terrain)
    {
        _terrain.Upload(terrain);
    }

    public int UploadModel(ModelRenderData model, int modelId = -1) => _models.UploadModel(model, modelId);

    public void UploadMapObjects(MapObjectReader mapObjects) => _models.UploadMapObjects(mapObjects);

    public void UploadDome(Gb2Object dome, TgaImage? texture) => _dome.Upload(dome, texture);

    private void CreateMsaaFbo(int width, int height)
    {
        // Delete old MSAA resources
        if (_msaaFbo != 0) _gl.DeleteFramebuffer(_msaaFbo);
        if (_msaaColorRbo != 0) _gl.DeleteRenderbuffer(_msaaColorRbo);
        if (_msaaDepthRbo != 0) _gl.DeleteRenderbuffer(_msaaDepthRbo);

        _msaaWidth = width;
        _msaaHeight = height;

        _msaaFbo = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _msaaFbo);

        _msaaColorRbo = _gl.GenRenderbuffer();
        _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _msaaColorRbo);
        _gl.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer,
            (uint)_msaaSamples, InternalFormat.Rgba8, (uint)width, (uint)height);
        _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, _msaaColorRbo);

        _msaaDepthRbo = _gl.GenRenderbuffer();
        _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _msaaDepthRbo);
        _gl.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer,
            (uint)_msaaSamples, InternalFormat.Depth24Stencil8, (uint)width, (uint)height);
        _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,
            FramebufferAttachment.DepthStencilAttachment, RenderbufferTarget.Renderbuffer, _msaaDepthRbo);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public void Cleanup()
    {
        if (_msaaFbo != 0) _gl.DeleteFramebuffer(_msaaFbo);
        if (_msaaColorRbo != 0) _gl.DeleteRenderbuffer(_msaaColorRbo);
        if (_msaaDepthRbo != 0) _gl.DeleteRenderbuffer(_msaaDepthRbo);

        _terrain?.Cleanup();
        _dome?.Cleanup();
        _sea?.Cleanup();
        _splines?.Cleanup();
        _models?.Cleanup();

        if (_terrainShader != 0) _gl.DeleteProgram(_terrainShader);
        if (_solidShader != 0) _gl.DeleteProgram(_solidShader);
        if (_modelShader != 0) _gl.DeleteProgram(_modelShader);
        if (_domeShader != 0) _gl.DeleteProgram(_domeShader);
    }

    public void Dispose() => Cleanup();
}
