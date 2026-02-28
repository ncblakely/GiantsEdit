using System.Numerics;
using GiantsEdit.Core.Formats;
using GiantsEdit.Core.Rendering;
using Silk.NET.OpenGL;

namespace GiantsEdit.App.Rendering;

/// <summary>
/// OpenGL ES 3.0 renderer implementing IRenderer.
/// Uses Silk.NET for GL bindings, runs under Avalonia's ANGLE context.
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
    private uint _avaloniaFbo; // Avalonia's framebuffer to blit to

    // Terrain
    private uint _terrainVao;
    private uint _terrainVboPos;
    private uint _terrainVboColor;
    private uint _terrainEbo;
    private int _terrainIndexCount;
    private uint _terrainLineEbo;
    private int _terrainLineIndexCount;
    private uint _terrainTexGround;
    private uint _terrainTexSlope;
    private uint _terrainTexWall;
    private uint _terrainNormGround;
    private uint _terrainNormSlope;
    private uint _terrainNormWall;
    private bool _terrainHasTextures;
    private bool _terrainHasNormals;

    // Dome
    private uint _domeVao;
    private uint _domeVbo;
    private uint _domeEbo;
    private int _domeIndexCount;

    // Sea
    private uint _seaVao;
    private uint _seaVbo;
    private int _seaVertexCount;

    // Shaders
    private uint _terrainShader;
    private uint _solidShader;
    private uint _modelShader;

    // Uniform locations
    private int _terrainMvpLoc;
    private int _terrainHasTexLoc;
    private int _terrainGroundTexLoc;
    private int _terrainSlopeTexLoc;
    private int _terrainWallTexLoc;
    private int _terrainGroundWrapLoc;
    private int _terrainSlopeWrapLoc;
    private int _terrainWallWrapLoc;
    private int _terrainGroundNormTexLoc;
    private int _terrainSlopeNormTexLoc;
    private int _terrainWallNormTexLoc;
    private int _terrainGroundNormWrapLoc;
    private int _terrainSlopeNormWrapLoc;
    private int _terrainWallNormWrapLoc;
    private int _terrainHasNormLoc;
    private int _terrainSunDirLoc;
    private int _solidMvpLoc;
    private int _solidColorLoc;
    private int _modelMvpLoc;
    private int _modelModelLoc;
    private int _modelHasTexLoc;
    private int _modelTexLoc;
    private int _modelHasNormLoc;
    private int _modelLightCountLoc;
    private int[] _modelLightDirLocs = new int[4];
    private int[] _modelLightColorLocs = new int[4];
    private int _modelSceneAmbientLoc;
    private int _modelMatAmbientLoc;
    private int _modelMatDiffuseLoc;
    private int _modelMatEmissiveLoc;
    private int _modelMatSpecularLoc;
    private int _modelMatPowerLoc;
    private int _modelCameraPosLoc;
    private int _modelColorScaleLoc;

    private readonly Dictionary<int, ModelGpuData> _models = new();
    private int _nextModelId;

    // Map object shapes: shapeIndex → GPU data, typeId → shapeIndex
    private readonly Dictionary<int, ModelGpuData> _mapObjShapes = new();
    private readonly Dictionary<int, int> _mapObjWrap = new();
    private bool _mapObjsLoaded;
    private bool _debugDumped1050;

    // Spline lines (dynamic, rebuilt each frame)
    private uint _lineVao;
    private uint _lineVbo;

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

        _terrainShader = CreateShader(TerrainVertSrc, TerrainFragSrc);
        _terrainMvpLoc = _gl.GetUniformLocation(_terrainShader, "uMVP");
        _terrainHasTexLoc = _gl.GetUniformLocation(_terrainShader, "uHasTex");
        _terrainGroundTexLoc = _gl.GetUniformLocation(_terrainShader, "uGroundTex");
        _terrainSlopeTexLoc = _gl.GetUniformLocation(_terrainShader, "uSlopeTex");
        _terrainWallTexLoc = _gl.GetUniformLocation(_terrainShader, "uWallTex");
        _terrainGroundWrapLoc = _gl.GetUniformLocation(_terrainShader, "uGroundWrap");
        _terrainSlopeWrapLoc = _gl.GetUniformLocation(_terrainShader, "uSlopeWrap");
        _terrainWallWrapLoc = _gl.GetUniformLocation(_terrainShader, "uWallWrap");
        _terrainGroundNormTexLoc = _gl.GetUniformLocation(_terrainShader, "uGroundNorm");
        _terrainSlopeNormTexLoc = _gl.GetUniformLocation(_terrainShader, "uSlopeNorm");
        _terrainWallNormTexLoc = _gl.GetUniformLocation(_terrainShader, "uWallNorm");
        _terrainGroundNormWrapLoc = _gl.GetUniformLocation(_terrainShader, "uGroundNormWrap");
        _terrainSlopeNormWrapLoc = _gl.GetUniformLocation(_terrainShader, "uSlopeNormWrap");
        _terrainWallNormWrapLoc = _gl.GetUniformLocation(_terrainShader, "uWallNormWrap");
        _terrainHasNormLoc = _gl.GetUniformLocation(_terrainShader, "uHasNorm");
        _terrainSunDirLoc = _gl.GetUniformLocation(_terrainShader, "uSunDir");

        _solidShader = CreateShader(SolidVertSrc, SolidFragSrc);
        _solidMvpLoc = _gl.GetUniformLocation(_solidShader, "uMVP");
        _solidColorLoc = _gl.GetUniformLocation(_solidShader, "uColor");

        _modelShader = CreateShader(ModelVertSrc, ModelFragSrc);
        _modelMvpLoc = _gl.GetUniformLocation(_modelShader, "uMVP");
        _modelModelLoc = _gl.GetUniformLocation(_modelShader, "uModel");
        _modelHasTexLoc = _gl.GetUniformLocation(_modelShader, "uHasTex");
        _modelTexLoc = _gl.GetUniformLocation(_modelShader, "uTex");
        _modelHasNormLoc = _gl.GetUniformLocation(_modelShader, "uHasNormals");
        _modelLightCountLoc = _gl.GetUniformLocation(_modelShader, "uLightCount");
        _modelSceneAmbientLoc = _gl.GetUniformLocation(_modelShader, "uSceneAmbient");
        _modelMatAmbientLoc = _gl.GetUniformLocation(_modelShader, "uMatAmbient");
        _modelMatDiffuseLoc = _gl.GetUniformLocation(_modelShader, "uMatDiffuse");
        _modelMatEmissiveLoc = _gl.GetUniformLocation(_modelShader, "uMatEmissive");
        _modelMatSpecularLoc = _gl.GetUniformLocation(_modelShader, "uMatSpecular");
        _modelMatPowerLoc = _gl.GetUniformLocation(_modelShader, "uMatPower");
        _modelCameraPosLoc = _gl.GetUniformLocation(_modelShader, "uCameraPos");
        _modelColorScaleLoc = _gl.GetUniformLocation(_modelShader, "uColorScale");
        for (int i = 0; i < 4; i++)
        {
            _modelLightDirLocs[i] = _gl.GetUniformLocation(_modelShader, $"uLightDir[{i}]");
            _modelLightColorLocs[i] = _gl.GetUniformLocation(_modelShader, $"uLightColor[{i}]");
        }

        BuildDome(10240f);
        BuildSea(10240f);

        // Create reusable line VAO/VBO for splines
        _lineVao = _gl.GenVertexArray();
        _lineVbo = _gl.GenBuffer();
        _gl.BindVertexArray(_lineVao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _lineVbo);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), (void*)0);
        _gl.EnableVertexAttribArray(0);
        _gl.BindVertexArray(0);

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

    public unsafe void Render(RenderState state)
    {
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        var vp = state.ViewMatrix * state.ProjectionMatrix;

        // Draw dome (behind everything, no depth write)
        // Camera is inside the dome, so disable back-face culling
        if (state.ShowDome && _domeIndexCount > 0)
        {
            _gl.DepthMask(false);
            _gl.Disable(EnableCap.CullFace);
            _gl.UseProgram(_terrainShader);
            SetUniformMatrix(_terrainMvpLoc, vp);
            _gl.Uniform1(_terrainHasTexLoc, 0); // Dome uses vertex colors only
            _gl.BindVertexArray(_domeVao);
            _gl.DrawElements(PrimitiveType.Triangles, (uint)_domeIndexCount, DrawElementsType.UnsignedInt, null);
            _gl.Enable(EnableCap.CullFace);
            _gl.DepthMask(true);
        }

        // Draw sea ground (below terrain)
        if (state.ShowSea && _seaVertexCount > 0)
        {
            _gl.Disable(EnableCap.CullFace);
            _gl.UseProgram(_solidShader);
            SetUniformMatrix(_solidMvpLoc, vp);
            _gl.Uniform4(_solidColorLoc, 0.05f, 0.15f, 0.1f, 1.0f);
            _gl.BindVertexArray(_seaVao);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_seaVertexCount);

            // Draw sea surface with blending
            _gl.Enable(EnableCap.Blend);
            _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            _gl.Uniform4(_solidColorLoc, state.SeaColor.X, state.SeaColor.Y, state.SeaColor.Z, 0.5f);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_seaVertexCount);
            _gl.Disable(EnableCap.Blend);
            _gl.Enable(EnableCap.CullFace);
        }

        // Draw terrain (disable culling — original uses mixed winding order)
        if (state.ShowTerrain && _terrainIndexCount > 0)
        {
            _gl.Disable(EnableCap.CullFace);
            _gl.UseProgram(_terrainShader);
            SetUniformMatrix(_terrainMvpLoc, vp);

            // Bind terrain textures if available
            _gl.Uniform1(_terrainHasTexLoc, _terrainHasTextures ? 1 : 0);
            if (_terrainHasTextures)
            {
                _gl.ActiveTexture(TextureUnit.Texture0);
                _gl.BindTexture(TextureTarget.Texture2D, _terrainTexGround);
                _gl.Uniform1(_terrainGroundTexLoc, 0);

                _gl.ActiveTexture(TextureUnit.Texture1);
                _gl.BindTexture(TextureTarget.Texture2D, _terrainTexSlope);
                _gl.Uniform1(_terrainSlopeTexLoc, 1);

                _gl.ActiveTexture(TextureUnit.Texture2);
                _gl.BindTexture(TextureTarget.Texture2D, _terrainTexWall);
                _gl.Uniform1(_terrainWallTexLoc, 2);

                // Bind normal map textures on units 3-5
                _gl.Uniform1(_terrainHasNormLoc, _terrainHasNormals ? 1 : 0);
                if (_terrainHasNormals)
                {
                    _gl.ActiveTexture(TextureUnit.Texture3);
                    _gl.BindTexture(TextureTarget.Texture2D, _terrainNormGround);
                    _gl.Uniform1(_terrainGroundNormTexLoc, 3);

                    _gl.ActiveTexture(TextureUnit.Texture4);
                    _gl.BindTexture(TextureTarget.Texture2D, _terrainNormSlope);
                    _gl.Uniform1(_terrainSlopeNormTexLoc, 4);

                    _gl.ActiveTexture(TextureUnit.Texture5);
                    _gl.BindTexture(TextureTarget.Texture2D, _terrainNormWall);
                    _gl.Uniform1(_terrainWallNormTexLoc, 5);
                }

                _gl.ActiveTexture(TextureUnit.Texture0);

                // Update sun direction for terrain bump mapping from actual map lights
                if (_terrainHasNormals)
                {
                    foreach (var light in state.Lights)
                    {
                        if (light.IsSun)
                        {
                            var d = light.Direction;
                            _gl.Uniform3(_terrainSunDirLoc, d.X, d.Y, d.Z);
                            break;
                        }
                    }
                }
            }

            _gl.BindVertexArray(_terrainVao);

            if (state.ShowTerrainMesh)
            {
                // Wireframe: disable textures, use vertex colors only
                _gl.Uniform1(_terrainHasTexLoc, 0);
                _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _terrainLineEbo);
                _gl.DrawElements(PrimitiveType.Lines, (uint)_terrainLineIndexCount, DrawElementsType.UnsignedInt, null);
                _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _terrainEbo);
            }
            else
            {
                _gl.DrawElements(PrimitiveType.Triangles, (uint)_terrainIndexCount, DrawElementsType.UnsignedInt, null);
            }

            _gl.Enable(EnableCap.CullFace);
        }

        // Draw objects
        if (state.ShowObjects)
        {
            if (state.ViewObjThruTerrain)
                _gl.Disable(EnableCap.DepthTest);

            _gl.UseProgram(_modelShader);
            SetUniformMatrix(_modelMvpLoc, vp);
            _gl.Disable(EnableCap.CullFace);

            // Set up directional lights and scene ambient
            // Game: GFC_DirectionalLights.Ambient = worldAmbientColor + sum(light->Ambient)
            int lightCount = Math.Min(state.Lights.Count, 4);
            _gl.Uniform1(_modelLightCountLoc, lightCount);
            Vector3 sceneAmbient = state.WorldAmbientColor;
            for (int i = 0; i < lightCount; i++)
            {
                var light = state.Lights[i];
                _gl.Uniform3(_modelLightDirLocs[i], light.Direction.X, light.Direction.Y, light.Direction.Z);
                _gl.Uniform3(_modelLightColorLocs[i], light.Color.X, light.Color.Y, light.Color.Z);
                sceneAmbient += light.Color;
            }
            sceneAmbient = Vector3.Min(sceneAmbient, Vector3.One);
            _gl.Uniform3(_modelSceneAmbientLoc, sceneAmbient.X, sceneAmbient.Y, sceneAmbient.Z);
            _gl.Uniform3(_modelCameraPosLoc, state.CameraPosition.X, state.CameraPosition.Y, state.CameraPosition.Z);

            foreach (var obj in state.Objects)
            {
                // Try real model when DrawRealObjects is on, otherwise use mapobj shapes
                ModelGpuData gpuData;
                bool isRealModel = false;
                if (state.DrawRealObjects && _models.TryGetValue(obj.ModelId, out gpuData))
                {
                    isRealModel = true;
                }
                else if (_mapObjsLoaded && _mapObjWrap.TryGetValue(obj.ModelId, out int shapeIdx)
                         && _mapObjShapes.TryGetValue(shapeIdx, out gpuData))
                {
                    // Map object shape from Mapobj.txt
                }
                else
                {
                    continue;
                }

                // Build model matrix
                // For mapobj shapes without DrawRealObjects, only translate + Z-rotate (no scale/tilt).
                Matrix4x4 model;
                if (isRealModel || state.DrawRealObjects)
                {
                    model = BuildObjectMatrix(in obj);
                }
                else
                {
                    float rad = obj.DirFacing * MathF.PI / 180f;
                    model = Matrix4x4.CreateRotationZ(rad)
                        * Matrix4x4.CreateTranslation(obj.Position);
                }

                SetUniformMatrix(_modelModelLoc, model);
                _gl.Uniform1(_modelHasNormLoc, gpuData.HasNormals ? 1 : 0);
                _gl.BindVertexArray(gpuData.Vao);

                if (gpuData.Parts != null && gpuData.Parts.Length > 0)
                {
                    // Draw per-part with texture binding
                    _gl.ActiveTexture(TextureUnit.Texture0);
                    _gl.Uniform1(_modelTexLoc, 0);

                    foreach (var part in gpuData.Parts)
                    {
                        // Set per-part material properties for lighting
                        _gl.Uniform3(_modelMatAmbientLoc, part.MaterialAmbient.X, part.MaterialAmbient.Y, part.MaterialAmbient.Z);
                        _gl.Uniform3(_modelMatDiffuseLoc, part.MaterialDiffuse.X, part.MaterialDiffuse.Y, part.MaterialDiffuse.Z);
                        _gl.Uniform3(_modelMatEmissiveLoc, part.MaterialEmissive.X, part.MaterialEmissive.Y, part.MaterialEmissive.Z);
                        _gl.Uniform3(_modelMatSpecularLoc, part.MaterialSpecular.X, part.MaterialSpecular.Y, part.MaterialSpecular.Z);
                        _gl.Uniform1(_modelMatPowerLoc, part.SpecularPower);
                        // If blend is disabled, apply white constant (texture only)
                        _gl.Uniform1(_modelColorScaleLoc, part.Blend > 0f ? 4.0f : 0.0f);

                        if (part.TextureId != 0)
                        {
                            _gl.BindTexture(TextureTarget.Texture2D, part.TextureId);
                            _gl.Uniform1(_modelHasTexLoc, 1);

                            if (part.HasAlpha)
                            {
                                _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                                _gl.Enable(EnableCap.Blend);
                            }
                            else
                            {
                                _gl.Disable(EnableCap.Blend);
                            }
                        }
                        else
                        {
                            _gl.Uniform1(_modelHasTexLoc, 0);
                            _gl.Disable(EnableCap.Blend);
                        }

                        _gl.DrawElements(PrimitiveType.Triangles, (uint)part.IndexCount,
                            DrawElementsType.UnsignedInt,
                            (void*)(part.IndexOffset * sizeof(uint)));
                    }

                    _gl.Disable(EnableCap.Blend);
                    _gl.BindTexture(TextureTarget.Texture2D, 0);
                }
                else
                {
                    // No parts — draw whole mesh without texture (mapobj shapes)
                    _gl.Uniform1(_modelHasTexLoc, 0);
                    _gl.DrawElements(PrimitiveType.Triangles, (uint)gpuData.IndexCount,
                        DrawElementsType.UnsignedInt, null);
                }
            }
            _gl.Enable(EnableCap.CullFace);

            if (state.ViewObjThruTerrain)
                _gl.Enable(EnableCap.DepthTest);
        }

        // Draw spline lines
        if (state.ShowObjects && state.SplineLines.Count > 0)
        {
            _gl.UseProgram(_solidShader);
            SetUniformMatrix(_solidMvpLoc, vp);
            if (!state.ViewObjThruTerrain)
                _gl.Disable(EnableCap.DepthTest);
            _gl.BindVertexArray(_lineVao);

            foreach (var spline in state.SplineLines)
            {
                if (spline.PointCount < 2) continue;

                // Upload line vertices dynamically
                _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _lineVbo);
                fixed (float* p = spline.Vertices)
                    _gl.BufferData(BufferTargetARB.ArrayBuffer,
                        (nuint)(spline.Vertices.Length * sizeof(float)),
                        p, BufferUsageARB.DynamicDraw);

                _gl.Uniform4(_solidColorLoc, spline.Color.X, spline.Color.Y, spline.Color.Z, 1.0f);
                _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)spline.PointCount);
            }

            _gl.Enable(EnableCap.DepthTest);
        }

        // Draw selection bounding box
        if (state.ShowObjects && state.SelectedObjectNode != null)
        {
            DrawSelectionBox(state, vp);
        }

        _gl.BindVertexArray(0);
        _gl.UseProgram(0);
    }

    private unsafe void DrawSelectionBox(RenderState state, Matrix4x4 vp)
    {
        // Find the selected object instance
        ObjectInstance? selected = null;
        foreach (var obj in state.Objects)
        {
            if (obj.SourceNode == state.SelectedObjectNode)
            {
                selected = obj;
                break;
            }
        }
        if (selected == null) return;

        var sel = selected.Value;

        // Get bounds from GPU data
        Vector3 bMin, bMax;
        bool found = false;
        if (state.DrawRealObjects && _models.TryGetValue(sel.ModelId, out var modelGpu))
        {
            bMin = modelGpu.BoundsMin;
            bMax = modelGpu.BoundsMax;
            found = true;
        }
        else if (_mapObjsLoaded && _mapObjWrap.TryGetValue(sel.ModelId, out int shapeIdx)
                 && _mapObjShapes.TryGetValue(shapeIdx, out var shapeGpu))
        {
            bMin = shapeGpu.BoundsMin;
            bMax = shapeGpu.BoundsMax;
            found = true;
        }
        else
        {
            // Default cube ±16
            bMin = new Vector3(-16);
            bMax = new Vector3(16);
            found = true;
        }

        if (!found) return;

        // Build model matrix matching the object transform
        Matrix4x4 model;
        if (state.DrawRealObjects)
        {
            model = BuildObjectMatrix(in sel);
        }
        else
        {
            float rad = sel.DirFacing * MathF.PI / 180f;
            model = Matrix4x4.CreateRotationZ(rad)
                * Matrix4x4.CreateTranslation(sel.Position);
        }

        // 12 edges of a box = 24 line vertices (3 floats each)
        float x0 = bMin.X, y0 = bMin.Y, z0 = bMin.Z;
        float x1 = bMax.X, y1 = bMax.Y, z1 = bMax.Z;
        float[] lines =
        [
            // Bottom face edges
            x0,y0,z0, x1,y0,z0,
            x1,y0,z0, x1,y1,z0,
            x1,y1,z0, x0,y1,z0,
            x0,y1,z0, x0,y0,z0,
            // Top face edges
            x0,y0,z1, x1,y0,z1,
            x1,y0,z1, x1,y1,z1,
            x1,y1,z1, x0,y1,z1,
            x0,y1,z1, x0,y0,z1,
            // Vertical edges
            x0,y0,z0, x0,y0,z1,
            x1,y0,z0, x1,y0,z1,
            x1,y1,z0, x1,y1,z1,
            x0,y1,z0, x0,y1,z1,
        ];

        var mvp = model * vp;

        _gl.UseProgram(_solidShader);
        SetUniformMatrix(_solidMvpLoc, mvp);
        _gl.Uniform4(_solidColorLoc, 1.0f, 1.0f, 1.0f, 0.6f);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.Disable(EnableCap.DepthTest);

        _gl.BindVertexArray(_lineVao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _lineVbo);
        fixed (float* p = lines)
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(lines.Length * sizeof(float)),
                p, BufferUsageARB.DynamicDraw);

        _gl.DrawArrays(PrimitiveType.Lines, 0, 24);

        _gl.Disable(EnableCap.Blend);
        _gl.Enable(EnableCap.DepthTest);
    }

    public unsafe void UploadTerrain(TerrainRenderData terrain)
    {
        // Clean up previous terrain buffers
        if (_terrainVao != 0)
        {
            _gl.DeleteVertexArray(_terrainVao);
            _gl.DeleteBuffer(_terrainVboPos);
            _gl.DeleteBuffer(_terrainVboColor);
            _gl.DeleteBuffer(_terrainEbo);
            if (_terrainLineEbo != 0) _gl.DeleteBuffer(_terrainLineEbo);
        }

        _terrainVao = _gl.GenVertexArray();
        _gl.BindVertexArray(_terrainVao);

        // Position buffer (location 0)
        _terrainVboPos = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _terrainVboPos);
        fixed (float* p = terrain.Positions)
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(terrain.Positions.Length * sizeof(float)),
                p, BufferUsageARB.StaticDraw);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), null);
        _gl.EnableVertexAttribArray(0);

        // Color buffer (location 1) — packed RGBA uint
        _terrainVboColor = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _terrainVboColor);
        fixed (uint* p = terrain.Colors)
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(terrain.Colors.Length * sizeof(uint)),
                p, BufferUsageARB.StaticDraw);
        _gl.VertexAttribPointer(1, 4, VertexAttribPointerType.UnsignedByte, true, sizeof(uint), null);
        _gl.EnableVertexAttribArray(1);

        // Index buffer
        _terrainEbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _terrainEbo);
        fixed (uint* p = terrain.Indices)
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(terrain.Indices.Length * sizeof(uint)),
                p, BufferUsageARB.StaticDraw);

        _terrainIndexCount = terrain.IndexCount;

        // Build wireframe line indices from triangles (each triangle → 3 edges)
        if (_terrainLineEbo != 0)
            _gl.DeleteBuffer(_terrainLineEbo);

        int triCount = terrain.IndexCount / 3;
        var lineIndices = new uint[triCount * 6]; // 3 edges × 2 vertices each
        for (int i = 0; i < triCount; i++)
        {
            uint a = terrain.Indices[i * 3];
            uint b = terrain.Indices[i * 3 + 1];
            uint c = terrain.Indices[i * 3 + 2];
            lineIndices[i * 6 + 0] = a; lineIndices[i * 6 + 1] = b;
            lineIndices[i * 6 + 2] = b; lineIndices[i * 6 + 3] = c;
            lineIndices[i * 6 + 4] = c; lineIndices[i * 6 + 5] = a;
        }

        _terrainLineEbo = _gl.GenBuffer();
        _gl.BindVertexArray(_terrainVao);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _terrainLineEbo);
        fixed (uint* p = lineIndices)
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(lineIndices.Length * sizeof(uint)),
                p, BufferUsageARB.StaticDraw);
        _terrainLineIndexCount = lineIndices.Length;

        // Re-bind the triangle EBO as default
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _terrainEbo);

        _gl.BindVertexArray(0);

        // Upload terrain textures
        DeleteTerrainTextures();
        _terrainHasTextures = false;
        _terrainHasNormals = false;

        if (terrain.Textures is { } tex)
        {
            _terrainTexGround = UploadTerrainTex(tex.GroundImage);
            _terrainTexSlope = UploadTerrainTex(tex.SlopeImage ?? tex.GroundImage);
            _terrainTexWall = UploadTerrainTex(tex.WallImage ?? tex.SlopeImage ?? tex.GroundImage);

            if (_terrainTexGround != 0)
            {
                _terrainHasTextures = true;

                _gl.UseProgram(_terrainShader);
                _gl.Uniform1(_terrainGroundWrapLoc, tex.GroundWrap);
                _gl.Uniform1(_terrainSlopeWrapLoc, tex.SlopeWrap);
                _gl.Uniform1(_terrainWallWrapLoc, tex.WallWrap);
            }

            // Upload normal map textures
            _terrainNormGround = UploadTerrainTex(tex.GroundNormalImage);
            _terrainNormSlope = UploadTerrainTex(tex.SlopeNormalImage ?? tex.GroundNormalImage);
            _terrainNormWall = UploadTerrainTex(tex.WallNormalImage ?? tex.SlopeNormalImage ?? tex.GroundNormalImage);

            if (_terrainNormGround != 0)
            {
                _terrainHasNormals = true;

                _gl.UseProgram(_terrainShader);
                _gl.Uniform1(_terrainGroundNormWrapLoc, tex.GroundNormalWrap);
                _gl.Uniform1(_terrainSlopeNormWrapLoc, tex.SlopeNormalWrap);
                _gl.Uniform1(_terrainWallNormWrapLoc, tex.WallNormalWrap);

                // Hardcoded sun direction (normalized)
                float sx = 0.5f, sy = 0.3f, sz = 0.8f;
                float len = MathF.Sqrt(sx * sx + sy * sy + sz * sz);
                _gl.Uniform3(_terrainSunDirLoc, sx / len, sy / len, sz / len);
            }
        }
    }

    private unsafe uint UploadTerrainTex(TgaImage? img)
    {
        if (img == null) return 0;

        uint tex = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, tex);
        _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.Repeat);
        _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.Repeat);
        _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.LinearMipmapLinear);

        var format = img.Channels switch
        {
            1 => InternalFormat.R8,
            3 => InternalFormat.Rgb,
            4 => InternalFormat.Rgba,
            _ => InternalFormat.Rgb
        };
        var pixelFormat = img.Channels switch
        {
            1 => PixelFormat.Red,
            3 => PixelFormat.Rgb,
            4 => PixelFormat.Rgba,
            _ => PixelFormat.Rgb
        };

        fixed (byte* px = img.Pixels)
            _gl.TexImage2D(TextureTarget.Texture2D, 0, format,
                (uint)img.Width, (uint)img.Height, 0,
                pixelFormat, PixelType.UnsignedByte, px);

        _gl.GenerateMipmap(TextureTarget.Texture2D);
        return tex;
    }

    private void DeleteTerrainTextures()
    {
        if (_terrainTexGround != 0) { _gl.DeleteTexture(_terrainTexGround); _terrainTexGround = 0; }
        if (_terrainTexSlope != 0) { _gl.DeleteTexture(_terrainTexSlope); _terrainTexSlope = 0; }
        if (_terrainTexWall != 0) { _gl.DeleteTexture(_terrainTexWall); _terrainTexWall = 0; }
        if (_terrainNormGround != 0) { _gl.DeleteTexture(_terrainNormGround); _terrainNormGround = 0; }
        if (_terrainNormSlope != 0) { _gl.DeleteTexture(_terrainNormSlope); _terrainNormSlope = 0; }
        if (_terrainNormWall != 0) { _gl.DeleteTexture(_terrainNormWall); _terrainNormWall = 0; }
    }

    public unsafe int UploadModel(ModelRenderData model, int modelId = -1)
    {
        int id = modelId >= 0 ? modelId : _nextModelId++;

        var gpuData = new ModelGpuData();
        gpuData.Vao = _gl.GenVertexArray();
        _gl.BindVertexArray(gpuData.Vao);

        // Interleaved VBO
        gpuData.Vbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, gpuData.Vbo);
        fixed (float* p = model.Vertices)
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(model.Vertices.Length * sizeof(float)),
                p, BufferUsageARB.StaticDraw);

        uint stride = (uint)(model.VertexStride * sizeof(float));
        // Position (location 0): offset 0
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
        _gl.EnableVertexAttribArray(0);
        // Normal (location 1): offset 12
        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));
        _gl.EnableVertexAttribArray(1);
        // UV (location 2): offset 24
        _gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, (void*)(6 * sizeof(float)));
        _gl.EnableVertexAttribArray(2);
        // Color (location 3): offset 32
        _gl.VertexAttribPointer(3, 3, VertexAttribPointerType.Float, false, stride, (void*)(8 * sizeof(float)));
        _gl.EnableVertexAttribArray(3);

        // EBO
        gpuData.Ebo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, gpuData.Ebo);
        fixed (uint* p = model.Indices)
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(model.Indices.Length * sizeof(uint)),
                p, BufferUsageARB.StaticDraw);

        gpuData.IndexCount = model.IndexCount;
        gpuData.BoundsMin = model.BoundsMin;
        gpuData.BoundsMax = model.BoundsMax;
        gpuData.HasNormals = model.HasNormals;

        // Upload textures for each part
        if (model.Parts.Count > 0)
        {
            gpuData.Parts = new ModelPartGpu[model.Parts.Count];
            for (int i = 0; i < model.Parts.Count; i++)
            {
                var part = model.Parts[i];
                gpuData.Parts[i] = new ModelPartGpu
                {
                    IndexOffset = part.IndexOffset,
                    IndexCount = part.IndexCount,
                    HasAlpha = false,
                    MaterialAmbient = part.MaterialAmbient,
                    MaterialDiffuse = part.MaterialDiffuse,
                    MaterialEmissive = part.MaterialEmissive,
                    MaterialSpecular = part.MaterialSpecular,
                    SpecularPower = part.SpecularPower,
                    Blend = part.Blend
                };

                if (part.TextureImage is { } img)
                {
                    uint tex = _gl.GenTexture();
                    _gl.BindTexture(TextureTarget.Texture2D, tex);
                    _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
                        (int)GLEnum.Repeat);
                    _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,
                        (int)GLEnum.Repeat);
                    _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                        (int)GLEnum.Linear);
                    _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                        (int)GLEnum.LinearMipmapLinear);

                    var format = img.Channels switch
                    {
                        1 => InternalFormat.R8,
                        3 => InternalFormat.Rgb,
                        4 => InternalFormat.Rgba,
                        _ => InternalFormat.Rgb
                    };
                    var pixelFormat = img.Channels switch
                    {
                        1 => PixelFormat.Red,
                        3 => PixelFormat.Rgb,
                        4 => PixelFormat.Rgba,
                        _ => PixelFormat.Rgb
                    };

                    fixed (byte* px = img.Pixels)
                    {
                        _gl.TexImage2D(TextureTarget.Texture2D, 0, format,
                            (uint)img.Width, (uint)img.Height, 0,
                            pixelFormat, PixelType.UnsignedByte, px);
                    }

                    _gl.GenerateMipmap(TextureTarget.Texture2D);

                    gpuData.Parts[i].TextureId = tex;
                    gpuData.Parts[i].HasAlpha = img.HasAlpha;
                }
            }
        }

        _gl.BindVertexArray(0);

        _models[id] = gpuData;
        return id;
    }

    public unsafe void UploadMapObjects(MapObjectReader mapObjects)
    {
        // Clean up previous
        foreach (var s in _mapObjShapes.Values)
        {
            _gl.DeleteVertexArray(s.Vao);
            _gl.DeleteBuffer(s.Vbo);
            _gl.DeleteBuffer(s.Ebo);
        }
        _mapObjShapes.Clear();
        _mapObjWrap.Clear();

        // Copy the type → shape mapping
        foreach (var (typeId, shapeIdx) in mapObjects.ObjectWrap)
            _mapObjWrap[typeId] = shapeIdx;

        // Upload each shape: pack as interleaved pos(3) + normal(3) + uv(2) + color(3) = 11 floats
        for (int si = 0; si < mapObjects.Objects.Count; si++)
        {
            var shape = mapObjects.Objects[si];
            if (shape.Triangles.Count == 0) continue;

            int vertCount = shape.Triangles.Count * 3;
            var verts = new float[vertCount * 11]; // 11 floats per vertex
            var indices = new uint[vertCount];

            for (int ti = 0; ti < shape.Triangles.Count; ti++)
            {
                var tri = shape.Triangles[ti];
                WriteVertex(verts, ti * 3 + 0, tri.V0);
                WriteVertex(verts, ti * 3 + 1, tri.V1);
                WriteVertex(verts, ti * 3 + 2, tri.V2);
                indices[ti * 3 + 0] = (uint)(ti * 3 + 0);
                indices[ti * 3 + 1] = (uint)(ti * 3 + 1);
                indices[ti * 3 + 2] = (uint)(ti * 3 + 2);
            }

            var gpuData = new ModelGpuData();
            gpuData.Vao = _gl.GenVertexArray();
            _gl.BindVertexArray(gpuData.Vao);

            gpuData.Vbo = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, gpuData.Vbo);
            fixed (float* p = verts)
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(verts.Length * sizeof(float)),
                    p, BufferUsageARB.StaticDraw);

            uint stride = 11 * sizeof(float);
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, (void*)(6 * sizeof(float)));
            _gl.EnableVertexAttribArray(2);
            _gl.VertexAttribPointer(3, 3, VertexAttribPointerType.Float, false, stride, (void*)(8 * sizeof(float)));
            _gl.EnableVertexAttribArray(3);

            gpuData.Ebo = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, gpuData.Ebo);
            fixed (uint* p = indices)
                _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indices.Length * sizeof(uint)),
                    p, BufferUsageARB.StaticDraw);

            gpuData.IndexCount = indices.Length;

            // Compute bounds from vertices
            var bMin = new Vector3(float.MaxValue);
            var bMax = new Vector3(float.MinValue);
            for (int vi = 0; vi < vertCount; vi++)
            {
                int off = vi * 11;
                var p = new Vector3(verts[off], verts[off + 1], verts[off + 2]);
                bMin = Vector3.Min(bMin, p);
                bMax = Vector3.Max(bMax, p);
            }
            gpuData.BoundsMin = bMin;
            gpuData.BoundsMax = bMax;

            _gl.BindVertexArray(0);

            _mapObjShapes[si] = gpuData;
        }

        _mapObjsLoaded = true;

        static void WriteVertex(float[] buf, int vi, MapObjVertex v)
        {
            int off = vi * 11;
            buf[off + 0] = v.X;
            buf[off + 1] = v.Y;
            buf[off + 2] = v.Z;
            // Normal (dummy up vector)
            buf[off + 3] = 0; buf[off + 4] = 0; buf[off + 5] = 1;
            // UV (unused)
            buf[off + 6] = 0; buf[off + 7] = 0;
            // Color (0-1 range)
            buf[off + 8] = v.R / 255f;
            buf[off + 9] = v.G / 255f;
            buf[off + 10] = v.B / 255f;
        }
    }

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

        if (_terrainVao != 0) _gl.DeleteVertexArray(_terrainVao);
        if (_terrainVboPos != 0) _gl.DeleteBuffer(_terrainVboPos);
        if (_terrainVboColor != 0) _gl.DeleteBuffer(_terrainVboColor);
        if (_terrainEbo != 0) _gl.DeleteBuffer(_terrainEbo);
        DeleteTerrainTextures();

        if (_domeVao != 0) _gl.DeleteVertexArray(_domeVao);
        if (_domeVbo != 0) _gl.DeleteBuffer(_domeVbo);
        if (_domeEbo != 0) _gl.DeleteBuffer(_domeEbo);

        if (_seaVao != 0) _gl.DeleteVertexArray(_seaVao);
        if (_seaVbo != 0) _gl.DeleteBuffer(_seaVbo);

        if (_lineVao != 0) _gl.DeleteVertexArray(_lineVao);
        if (_lineVbo != 0) _gl.DeleteBuffer(_lineVbo);

        foreach (var m in _models.Values)
        {
            _gl.DeleteVertexArray(m.Vao);
            _gl.DeleteBuffer(m.Vbo);
            _gl.DeleteBuffer(m.Ebo);
            if (m.Parts != null)
                foreach (var p in m.Parts)
                    if (p.TextureId != 0) _gl.DeleteTexture(p.TextureId);
        }
        _models.Clear();

        foreach (var s in _mapObjShapes.Values)
        {
            _gl.DeleteVertexArray(s.Vao);
            _gl.DeleteBuffer(s.Vbo);
            _gl.DeleteBuffer(s.Ebo);
        }
        _mapObjShapes.Clear();
        _mapObjWrap.Clear();
        _mapObjsLoaded = false;

        if (_terrainShader != 0) _gl.DeleteProgram(_terrainShader);
        if (_solidShader != 0) _gl.DeleteProgram(_solidShader);
        if (_modelShader != 0) _gl.DeleteProgram(_modelShader);
    }

    public void Dispose() => Cleanup();

    #region Dome & Sea mesh generation

    private unsafe void BuildDome(float radius)
    {
        const int hSegments = 32;
        const int vSegments = 8;
        int vertCount = (hSegments + 1) * (vSegments + 1);

        // Position(3) + Color(3)
        var verts = new float[vertCount * 6];
        int vi = 0;
        for (int y = 0; y <= vSegments; y++)
        {
            float v = y / (float)vSegments;
            float phi = v * MathF.PI / 2f; // 0 to pi/2 (hemisphere)
            for (int x = 0; x <= hSegments; x++)
            {
                float u = x / (float)hSegments;
                float theta = u * MathF.PI * 2f;

                verts[vi++] = MathF.Cos(theta) * MathF.Cos(phi) * radius;
                verts[vi++] = MathF.Sin(theta) * MathF.Cos(phi) * radius;
                verts[vi++] = MathF.Sin(phi) * radius;

                // Gradient: darker at horizon, lighter at zenith
                float brightness = 0.15f + 0.85f * v;
                verts[vi++] = 0.4f * brightness;
                verts[vi++] = 0.5f * brightness;
                verts[vi++] = 0.9f * brightness;
            }
        }

        var indices = new List<uint>();
        for (int y = 0; y < vSegments; y++)
        {
            for (int x = 0; x < hSegments; x++)
            {
                uint a = (uint)(y * (hSegments + 1) + x);
                uint b = a + (uint)(hSegments + 1);
                uint c = a + 1;
                uint d = b + 1;

                indices.Add(a); indices.Add(b); indices.Add(c);
                indices.Add(c); indices.Add(b); indices.Add(d);
            }
        }

        _domeVao = _gl.GenVertexArray();
        _gl.BindVertexArray(_domeVao);

        _domeVbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _domeVbo);
        fixed (float* p = verts)
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(verts.Length * sizeof(float)),
                p, BufferUsageARB.StaticDraw);

        // Position (location 0)
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)0);
        _gl.EnableVertexAttribArray(0);
        // Color (location 1)
        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float),
            (void*)(3 * sizeof(float)));
        _gl.EnableVertexAttribArray(1);

        _domeEbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _domeEbo);
        var idxArr = indices.ToArray();
        fixed (uint* p = idxArr)
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(idxArr.Length * sizeof(uint)),
                p, BufferUsageARB.StaticDraw);

        _domeIndexCount = idxArr.Length;
        _gl.BindVertexArray(0);
    }

    private unsafe void BuildSea(float radius)
    {
        const int segments = 32;
        const float seaGround = -42f;

        // Fan of triangles: 3 vertices per segment
        var verts = new float[segments * 9]; // 3 verts * 3 floats
        for (int i = 0; i < segments; i++)
        {
            float a1 = i * MathF.PI * 2f / segments;
            float a2 = (i + 1) * MathF.PI * 2f / segments;

            int off = i * 9;
            verts[off + 0] = 0; verts[off + 1] = 0; verts[off + 2] = seaGround;
            verts[off + 3] = MathF.Cos(a1) * radius;
            verts[off + 4] = MathF.Sin(a1) * radius;
            verts[off + 5] = seaGround;
            verts[off + 6] = MathF.Cos(a2) * radius;
            verts[off + 7] = MathF.Sin(a2) * radius;
            verts[off + 8] = seaGround;
        }

        _seaVao = _gl.GenVertexArray();
        _gl.BindVertexArray(_seaVao);

        _seaVbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _seaVbo);
        fixed (float* p = verts)
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(verts.Length * sizeof(float)),
                p, BufferUsageARB.StaticDraw);

        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), null);
        _gl.EnableVertexAttribArray(0);

        _seaVertexCount = segments * 3;
        _gl.BindVertexArray(0);
    }

    #endregion

    #region Shader helpers

    private unsafe void SetUniformMatrix(int location, Matrix4x4 mat)
    {
        // System.Numerics stores row-major. GLSL expects column-major.
        // Row-major M in memory is identical to column-major M^T,
        // which is exactly what GLSL needs for M * v (column-vector).
        float* p = (float*)&mat;
        _gl.UniformMatrix4(location, 1, false, p);
    }

    /// <summary>
    /// Build a model matrix for a game object.
    /// </summary>
    private static Matrix4x4 BuildObjectMatrix(in ObjectInstance obj)
    {
        float deg2rad = MathF.PI / 180f;
        float sx = MathF.Sin(-obj.TiltForward * deg2rad);
        float cx = MathF.Cos(-obj.TiltForward * deg2rad);
        float sy = MathF.Sin(obj.TiltLeft * deg2rad);
        float cy = MathF.Cos(obj.TiltLeft * deg2rad);
        float sz = MathF.Sin(obj.DirFacing * deg2rad);
        float cz = MathF.Cos(obj.DirFacing * deg2rad);
        float s = obj.Scale;

        // Game's 3x3 rotation-scale matrix (row-major, column-vector: M * v):
        //   [cy*cz,             -sx*sy*cz-cx*sz,    -cx*sy*cz+sx*sz]
        //   [cy*sz,             -sx*sy*sz+cx*cz,    -cx*sy*sz-sx*cz]
        //   [sy,                 sx*cy,               cx*cy         ]
        //
        // System.Numerics is row-major row-vector convention (v * M),
        // so we store the transpose of the game matrix.
        return new Matrix4x4(
            cy * cz * s, cy * sz * s, sy * s, 0,
            (-sx * sy * cz - cx * sz) * s, (-sx * sy * sz + cx * cz) * s, sx * cy * s, 0,
            (-cx * sy * cz + sx * sz) * s, (-cx * sy * sz - sx * cz) * s, cx * cy * s, 0,
            obj.Position.X, obj.Position.Y, obj.Position.Z, 1
        );
    }

    private uint CreateShader(string vertSrc, string fragSrc)
    {
        uint vs = CompileShader(ShaderType.VertexShader, vertSrc);
        uint fs = CompileShader(ShaderType.FragmentShader, fragSrc);

        uint prog = _gl.CreateProgram();
        _gl.AttachShader(prog, vs);
        _gl.AttachShader(prog, fs);
        _gl.LinkProgram(prog);

        _gl.GetProgram(prog, ProgramPropertyARB.LinkStatus, out int status);
        if (status == 0)
        {
            string log = _gl.GetProgramInfoLog(prog);
            throw new InvalidOperationException($"Shader link failed: {log}");
        }

        _gl.DeleteShader(vs);
        _gl.DeleteShader(fs);
        return prog;
    }

    private uint CompileShader(ShaderType type, string src)
    {
        uint s = _gl.CreateShader(type);
        _gl.ShaderSource(s, src);
        _gl.CompileShader(s);

        _gl.GetShader(s, ShaderParameterName.CompileStatus, out int status);
        if (status == 0)
        {
            string log = _gl.GetShaderInfoLog(s);
            throw new InvalidOperationException($"Shader compile ({type}) failed: {log}");
        }
        return s;
    }

    #endregion

    #region Shader sources

    private const string TerrainVertSrc = """
        #version 300 es
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec4 aColor;
        uniform mat4 uMVP;
        out vec4 vColor;
        out vec3 vWorldPos;
        void main() {
            gl_Position = uMVP * vec4(aPos, 1.0);
            vColor = aColor;
            vWorldPos = aPos;
        }
        """;

    private const string TerrainFragSrc = """
        #version 300 es
        precision highp float;
        in vec4 vColor;
        in vec3 vWorldPos;
        uniform int uHasTex;
        uniform sampler2D uGroundTex;
        uniform sampler2D uSlopeTex;
        uniform sampler2D uWallTex;
        uniform float uGroundWrap;
        uniform float uSlopeWrap;
        uniform float uWallWrap;
        uniform int uHasNorm;
        uniform sampler2D uGroundNorm;
        uniform sampler2D uSlopeNorm;
        uniform sampler2D uWallNorm;
        uniform float uGroundNormWrap;
        uniform float uSlopeNormWrap;
        uniform float uWallNormWrap;
        uniform vec3 uSunDir;
        out vec4 FragColor;

        // Anti-tiling: hash function for random offsets per tile
        vec4 hash4(vec2 p) {
            return fract(
                sin(vec4(
                    1.0 + dot(p, vec2(37.0, 17.0)),
                    2.0 + dot(p, vec2(11.0, 47.0)),
                    3.0 + dot(p, vec2(41.0, 29.0)),
                    4.0 + dot(p, vec2(23.0, 31.0))
                )) * 103.0);
        }

        // Anti-tiling texture sample using 4 offset/rotated lookups blended together
        vec4 textureNoTile(sampler2D tex, vec2 uv) {
            vec2 iuv = floor(uv);
            vec2 fuv = fract(uv);

            vec4 ofa = hash4(iuv + vec2(0.0, 0.0));
            vec4 ofb = hash4(iuv + vec2(1.0, 0.0));
            vec4 ofc = hash4(iuv + vec2(0.0, 1.0));
            vec4 ofd = hash4(iuv + vec2(1.0, 1.0));

            vec2 ddxuv = dFdx(uv);
            vec2 ddyuv = dFdy(uv);

            ofa.zw = sign(ofa.zw - 0.5);
            ofb.zw = sign(ofb.zw - 0.5);
            ofc.zw = sign(ofc.zw - 0.5);
            ofd.zw = sign(ofd.zw - 0.5);

            vec2 uva = uv * ofa.zw + ofa.xy; vec2 ddxa = ddxuv * ofa.zw; vec2 ddya = ddyuv * ofa.zw;
            vec2 uvb = uv * ofb.zw + ofb.xy; vec2 ddxb = ddxuv * ofb.zw; vec2 ddyb = ddyuv * ofb.zw;
            vec2 uvc = uv * ofc.zw + ofc.xy; vec2 ddxc = ddxuv * ofc.zw; vec2 ddyc = ddyuv * ofc.zw;
            vec2 uvd = uv * ofd.zw + ofd.xy; vec2 ddxd = ddxuv * ofd.zw; vec2 ddyd = ddyuv * ofd.zw;

            vec2 b = smoothstep(0.25, 0.75, fuv);

            return mix(
                mix(textureGrad(tex, uva, ddxa, ddya),
                    textureGrad(tex, uvb, ddxb, ddyb), b.x),
                mix(textureGrad(tex, uvc, ddxc, ddyc),
                    textureGrad(tex, uvd, ddxd, ddyd), b.x),
                b.y);
        }

        // Expand [0,1] to [-1,1]
        vec3 bx2(vec3 x) { return 2.0 * x - 1.0; }

        // Calculate tangent-normal-binormal frame from world position derivatives
        void calcTNB(vec3 worldPos, vec3 N, vec2 uv, out vec3 T, out vec3 B) {
            vec3 dp1 = dFdx(worldPos);
            vec3 dp2 = dFdy(worldPos);
            vec2 duv1 = dFdx(uv);
            vec2 duv2 = dFdy(uv);

            vec3 t = normalize(duv2.y * dp1 - duv1.y * dp2);
            vec3 b = normalize(duv2.x * dp1 - duv1.x * dp2);
            vec3 n = normalize(N);

            // Re-orthogonalize tangent and binormal to the normal
            vec3 x = cross(n, t);
            t = normalize(cross(x, n));

            x = cross(b, n);
            b = normalize(cross(n, x));

            T = t;
            B = b;
        }

        // Sample normal map and compute lighting intensity
        float calcNormalMapLighting(sampler2D normTex, vec3 worldPos, vec3 faceN, vec2 uv) {
            vec3 T, B;
            calcTNB(worldPos, faceN, uv, T, B);

            vec3 bumpSample = bx2(textureNoTile(normTex, uv).rgb);
            vec3 bumpNormal = normalize(bumpSample.x * T + bumpSample.y * B + bumpSample.z * normalize(faceN));

            return max(0.01, clamp(dot(bumpNormal, uSunDir), 0.0, 1.0));
        }

        void main() {
            if (uHasTex == 1) {
                vec3 dpdx = dFdx(vWorldPos);
                vec3 dpdy = dFdy(vWorldPos);
                vec3 faceN = normalize(cross(dpdx, dpdy));
                float steepness = abs(faceN.z);

                vec2 groundUV = vWorldPos.xy / uGroundWrap;
                vec2 slopeUV = vWorldPos.xy / uSlopeWrap;
                vec2 wallUV = abs(faceN.x) > abs(faceN.y)
                    ? vWorldPos.yz / uWallWrap
                    : vWorldPos.xz / uWallWrap;

                vec3 groundCol = textureNoTile(uGroundTex, groundUV).rgb;
                vec3 slopeCol = textureNoTile(uSlopeTex, slopeUV).rgb;
                vec3 wallCol = textureNoTile(uWallTex, wallUV).rgb;

                float groundFactor = smoothstep(0.6, 0.8, steepness);
                float wallFactor = smoothstep(0.4, 0.2, steepness);
                float slopeFactor = 1.0 - groundFactor - wallFactor;

                vec3 texCol = groundCol * groundFactor + slopeCol * slopeFactor + wallCol * wallFactor;

                if (uHasNorm == 1) {
                    // Normal map lighting per terrain type, blended by steepness
                    vec2 groundNormUV = vWorldPos.xy / uGroundNormWrap;
                    vec2 slopeNormUV = vWorldPos.xy / uSlopeNormWrap;
                    vec2 wallNormUV = abs(faceN.x) > abs(faceN.y)
                        ? vWorldPos.yz / uWallNormWrap
                        : vWorldPos.xz / uWallNormWrap;

                    float gLight = calcNormalMapLighting(uGroundNorm, vWorldPos, faceN, groundNormUV);
                    float sLight = calcNormalMapLighting(uSlopeNorm, vWorldPos, faceN, slopeNormUV);
                    float wLight = calcNormalMapLighting(uWallNorm, vWorldPos, faceN, wallNormUV);
                    float bumpLight = gLight * groundFactor + sLight * slopeFactor + wLight * wallFactor;

                    // Game formula: 2.0 * (bumpLight * diffuseTexture + bakedLightmap)
                    FragColor = vec4(2.0 * (bumpLight * texCol + vColor.rgb), 1.0);
                } else {
                    FragColor = vec4(vColor.rgb * texCol * 2.0, 1.0);
                }
            } else {
                FragColor = vColor;
            }
        }
        """;

    private const string SolidVertSrc = """
        #version 300 es
        layout(location = 0) in vec3 aPos;
        uniform mat4 uMVP;
        void main() {
            gl_Position = uMVP * vec4(aPos, 1.0);
        }
        """;

    private const string SolidFragSrc = """
        #version 300 es
        precision mediump float;
        uniform vec4 uColor;
        out vec4 FragColor;
        void main() {
            FragColor = uColor;
        }
        """;

    private const string ModelVertSrc = """
        #version 300 es
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec2 aUV;
        layout(location = 3) in vec3 aColor;
        uniform mat4 uMVP;
        uniform mat4 uModel;
        out vec3 vColor;
        out vec2 vUV;
        out vec3 vNormal;
        out vec3 vWorldPos;
        void main() {
            gl_Position = uMVP * uModel * vec4(aPos, 1.0);
            vColor = aColor;
            vUV = aUV;
            // Transform normal to world space (using model matrix upper-3x3)
            vNormal = mat3(uModel) * aNormal;
            vWorldPos = (uModel * vec4(aPos, 1.0)).xyz;
        }
        """;

    private const string ModelFragSrc = """
        #version 300 es
        precision mediump float;
        in vec3 vColor;
        in vec2 vUV;
        in vec3 vNormal;
        in vec3 vWorldPos;
        uniform int uHasTex;
        uniform sampler2D uTex;
        uniform int uHasNormals;
        uniform int uLightCount;
        uniform vec3 uLightDir[4];
        uniform vec3 uLightColor[4];
        uniform vec3 uSceneAmbient;
        uniform vec3 uMatAmbient;
        uniform vec3 uMatDiffuse;
        uniform vec3 uMatEmissive;
        uniform vec3 uMatSpecular;
        uniform float uMatPower;
        uniform vec3 uCameraPos;
        uniform float uColorScale;
        out vec4 FragColor;
        void main() {
            vec3 texColor = vec3(1.0);
            float alpha = 1.0;
            if (uHasTex == 1) {
                vec4 t = texture(uTex, vUV);
                texColor = t.rgb;
                alpha = t.a;
            }

            if (uHasNormals == 1 && uLightCount > 0) {
                vec3 N = normalize(vNormal);
                vec3 lightDiffuse = vec3(0.0);
                vec3 lightSpecular = vec3(0.0);
                vec3 V = normalize(uCameraPos - vWorldPos);
                for (int i = 0; i < 4; i++) {
                    if (i >= uLightCount) break;
                    vec3 L = normalize(uLightDir[i]);
                    float NdotL = max(0.0, dot(N, L));
                    lightDiffuse += uLightColor[i] * NdotL;
                    if (uMatPower > 0.0) {
                        vec3 H = normalize(V + L);
                        float NdotH = max(0.0, dot(H, N));
                        lightSpecular += uLightColor[i] * pow(NdotH, uMatPower);
                    }
                }
                vec3 specular = clamp(uMatSpecular * lightSpecular, 0.0, 1.0);
                vec3 finalColor;
                if (uColorScale > 0.0) {
                    vec3 ambient = uMatAmbient * uSceneAmbient;
                    vec3 diffuse = uMatDiffuse * lightDiffuse;
                    vec3 lighting = clamp(ambient + diffuse + uMatEmissive, 0.0, 1.0);
                    finalColor = texColor * lighting * uColorScale + specular;
                } else {
                    finalColor = texColor + specular;
                }
                FragColor = vec4(finalColor, alpha);
            } else {
                FragColor = vec4(texColor * vColor, alpha);
            }
        }
        """;

    #endregion

    private struct ModelGpuData
    {
        public uint Vao, Vbo, Ebo;
        public int IndexCount;
        public ModelPartGpu[]? Parts;
        public Vector3 BoundsMin, BoundsMax;
        public bool HasNormals;
    }

    private struct ModelPartGpu
    {
        public int IndexOffset;
        public int IndexCount;
        public uint TextureId; // 0 = no texture
        public bool HasAlpha;
        public Vector3 MaterialAmbient;
        public Vector3 MaterialDiffuse;
        public Vector3 MaterialEmissive;
        public Vector3 MaterialSpecular;
        public float SpecularPower;
        public float Blend;
    }
}
