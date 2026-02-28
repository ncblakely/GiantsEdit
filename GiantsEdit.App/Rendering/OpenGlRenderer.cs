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

    private void TexParam(TextureParameterName pname, int value)
    {
        _gl.TexParameterI(TextureTarget.Texture2D, pname, in value);
    }
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
    private uint _terrainVboBumpDiffuse;
    private uint _terrainEbo;
    private int _terrainIndexCount;
    private uint _terrainLineEbo;
    private int _terrainLineIndexCount;
    private uint _terrainTexGround;
    private uint _terrainTexSlope;
    private uint _terrainTexWall;
    private uint _terrainBumpTex;
    private bool _terrainHasTextures;
    private bool _terrainHasBump;

    // Dome
    private uint _domeVao;
    private uint _domeVbo;
    private uint _domeEbo;
    private int _domeIndexCount;
    private uint _domeTex;
    private bool _domeHasTexture;

    // Sea
    private uint _seaVao;
    private uint _seaVbo;
    private int _seaVertexCount;

    // Shaders
    private uint _terrainShader;
    private uint _solidShader;
    private uint _modelShader;
    private uint _domeShader;

    // Uniform locations
    private int _terrainMvpLoc;
    private int _terrainHasTexLoc;
    private int _terrainGroundTexLoc;
    private int _terrainSlopeTexLoc;
    private int _terrainWallTexLoc;
    private int _terrainGroundWrapLoc;
    private int _terrainSlopeWrapLoc;
    private int _terrainWallWrapLoc;
    private int _terrainBumpTexLoc;
    private int _terrainBumpWrapLoc;
    private int _terrainHasBumpLoc;
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
    private int _domeMvpLoc;
    private int _domeTexLoc;

    private readonly Dictionary<int, ModelGpuData> _models = new();
    private int _nextModelId;

    // Map object shapes: shapeIndex → GPU data, typeId → shapeIndex
    private readonly Dictionary<int, ModelGpuData> _mapObjShapes = new();
    private readonly Dictionary<int, int> _mapObjWrap = new();
    private bool _mapObjsLoaded;

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
        _terrainBumpTexLoc = _gl.GetUniformLocation(_terrainShader, "uBumpTex");
        _terrainBumpWrapLoc = _gl.GetUniformLocation(_terrainShader, "uBumpWrap");
        _terrainHasBumpLoc = _gl.GetUniformLocation(_terrainShader, "uHasBump");

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

        _domeShader = CreateShader(DomeVertSrc, DomeFragSrc);
        _domeMvpLoc = _gl.GetUniformLocation(_domeShader, "uMVP");
        _domeTexLoc = _gl.GetUniformLocation(_domeShader, "uTex");

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
        if (state.ShowDome && _domeIndexCount > 0 && _domeHasTexture)
        {
            _gl.DepthMask(false);
            _gl.Disable(EnableCap.CullFace);
            _gl.UseProgram(_domeShader);
            SetUniformMatrix(_domeMvpLoc, vp);
            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindTexture(TextureTarget.Texture2D, _domeTex);
            _gl.Uniform1(_domeTexLoc, 0);
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

                // Bind bump texture on unit 3
                _gl.Uniform1(_terrainHasBumpLoc, _terrainHasBump ? 1 : 0);
                if (_terrainHasBump)
                {
                    _gl.ActiveTexture(TextureUnit.Texture3);
                    _gl.BindTexture(TextureTarget.Texture2D, _terrainBumpTex);
                    _gl.Uniform1(_terrainBumpTexLoc, 3);
                }

                _gl.ActiveTexture(TextureUnit.Texture0);
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
            if (_terrainVboBumpDiffuse != 0) _gl.DeleteBuffer(_terrainVboBumpDiffuse);
            _gl.DeleteBuffer(_terrainEbo);
            if (_terrainLineEbo != 0) _gl.DeleteBuffer(_terrainLineEbo);
        }
        _terrainVboBumpDiffuse = 0;

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

        // Bump diffuse buffer (location 2) — per-vertex sun direction in tangent space
        if (terrain.BumpDiffuseColors != null)
        {
            _terrainVboBumpDiffuse = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _terrainVboBumpDiffuse);
            fixed (uint* p = terrain.BumpDiffuseColors)
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(terrain.BumpDiffuseColors.Length * sizeof(uint)),
                    p, BufferUsageARB.StaticDraw);
            _gl.VertexAttribPointer(2, 4, VertexAttribPointerType.UnsignedByte, true, sizeof(uint), null);
            _gl.EnableVertexAttribArray(2);
        }

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
        _terrainHasBump = false;

        if (terrain.Textures is { } tex)
        {
            _terrainTexGround = UploadTerrainTexWithFalloff(tex.GroundImage, tex.MipFalloff0, tex.MipFalloff1, tex.MipFalloff2);
            _terrainTexSlope = UploadTerrainTexWithFalloff(tex.SlopeImage ?? tex.GroundImage, tex.MipFalloff0, tex.MipFalloff1, tex.MipFalloff2);
            _terrainTexWall = UploadTerrainTexWithFalloff(tex.WallImage ?? tex.SlopeImage ?? tex.GroundImage, tex.MipFalloff0, tex.MipFalloff1, tex.MipFalloff2);

            if (_terrainTexGround != 0)
            {
                _terrainHasTextures = true;

                _gl.UseProgram(_terrainShader);
                _gl.Uniform1(_terrainGroundWrapLoc, tex.GroundWrap);
                _gl.Uniform1(_terrainSlopeWrapLoc, tex.SlopeWrap);
                _gl.Uniform1(_terrainWallWrapLoc, tex.WallWrap);
            }

            // Upload single bump texture (ground bump) for dot3 bump mapping
            // Convert diffuse image to normal map
            _terrainBumpTex = UploadBumpTex(tex.GroundNormalImage);
            if (_terrainBumpTex != 0 && terrain.BumpDiffuseColors != null)
            {
                _terrainHasBump = true;

                _gl.UseProgram(_terrainShader);
                _gl.Uniform1(_terrainBumpWrapLoc, tex.GroundNormalWrap);
            }
        }
    }

    private unsafe uint UploadTerrainTex(TgaImage? img)
    {
        if (img == null) return 0;

        uint tex = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, tex);
        TexParam(TextureParameterName.TextureWrapS, (int)GLEnum.Repeat);
        TexParam(TextureParameterName.TextureWrapT, (int)GLEnum.Repeat);
        TexParam(TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        TexParam(TextureParameterName.TextureMinFilter, (int)GLEnum.LinearMipmapLinear);

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

    private unsafe uint UploadBumpTex(TgaImage? img)
    {
        if (img == null) return 0;

        byte[] normalMapData = ConvertDiffuseToNormalMap(img.Pixels, img.Width, img.Height, img.Channels);

        uint tex = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, tex);
        TexParam(TextureParameterName.TextureWrapS, (int)GLEnum.Repeat);
        TexParam(TextureParameterName.TextureWrapT, (int)GLEnum.Repeat);
        TexParam(TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        TexParam(TextureParameterName.TextureMinFilter, (int)GLEnum.LinearMipmapLinear);

        fixed (byte* px = normalMapData)
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba,
                (uint)img.Width, (uint)img.Height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, px);

        _gl.GenerateMipmap(TextureTarget.Texture2D);
        return tex;
    }

    /// <summary>
    /// Converts a diffuse texture to a normal map
    /// 1. RGB → height via screen-blend formula (colorspace)
    /// 2. Height → normal map via finite differences with scale factor
    /// </summary>
    private static byte[] ConvertDiffuseToNormalMap(byte[] pixels, int width, int height, int channels, float scale = 1.0f / 64.0f)
    {
        // Step 1: Compute height from RGB using the colorspace (screen blend) formula
        var heights = new byte[width * height];
        for (int j = 0; j < height; j++)
        {
            for (int i = 0; i < width; i++)
            {
                int idx = (j * width + i) * channels;
                float fr = pixels[idx] / 255.0f;
                float fg = pixels[idx + 1] / 255.0f;
                float fb = (channels >= 3) ? pixels[idx + 2] / 255.0f : 0f;

                float fa = 1.0f - (1.0f - fr) * (1.0f - fg) * (1.0f - fb);
                heights[j * width + i] = (byte)Math.Min(255, (int)(fa * 255));
            }
        }

        // Step 2: Convert height map to normal map via finite differences
        var result = new byte[width * height * 4];
        for (int j = 0; j < height; j++)
        {
            for (int i = 0; i < width; i++)
            {
                float h0 = heights[j * width + i];
                float h1 = heights[j * width + Math.Min(i + 1, width - 1)];
                float h2 = heights[Math.Min(j + 1, height - 1) * width + i];

                // Normal = cross(v2, v1) where v1 = (1, scale*(h1-h0), 0), v2 = (0, scale*(h2-h0), 1)
                float nx = -scale * (h1 - h0);
                float ny = 1.0f;
                float nz = -scale * (h2 - h0);

                float len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                if (len > 0)
                {
                    nx /= len;
                    ny /= len;
                    nz /= len;
                }

                int outIdx = (j * width + i) * 4;
                result[outIdx] = (byte)Math.Min(255, (int)((nx + 1.0f) * 127.5f));     // R = X
                result[outIdx + 1] = (byte)Math.Min(255, (int)((ny + 1.0f) * 127.5f)); // G = Y (up)
                result[outIdx + 2] = (byte)Math.Min(255, (int)((nz + 1.0f) * 127.5f)); // B = Z
                result[outIdx + 3] = heights[j * width + i];                            // A = height
            }
        }

        return result;
    }

    private void DeleteTerrainTextures()
    {
        if (_terrainTexGround != 0) { _gl.DeleteTexture(_terrainTexGround); _terrainTexGround = 0; }
        if (_terrainTexSlope != 0) { _gl.DeleteTexture(_terrainTexSlope); _terrainTexSlope = 0; }
        if (_terrainTexWall != 0) { _gl.DeleteTexture(_terrainTexWall); _terrainTexWall = 0; }
        if (_terrainBumpTex != 0) { _gl.DeleteTexture(_terrainBumpTex); _terrainBumpTex = 0; }
    }

    /// <summary>
    /// Uploads a terrain texture with manually generated mipmaps that fade to black at higher levels.
    /// </summary>
    private unsafe uint UploadTerrainTexWithFalloff(TgaImage? img, float c0, float c1, float c2)
    {
        if (img == null) return 0;

        uint tex = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, tex);
        TexParam(TextureParameterName.TextureWrapS, (int)GLEnum.Repeat);
        TexParam(TextureParameterName.TextureWrapT, (int)GLEnum.Repeat);
        TexParam(TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        TexParam(TextureParameterName.TextureMinFilter, (int)GLEnum.LinearMipmapLinear);

        int ch = img.Channels;
        var format = ch switch { 1 => InternalFormat.R8, 3 => InternalFormat.Rgb, 4 => InternalFormat.Rgba, _ => InternalFormat.Rgb };
        var pixelFmt = ch switch { 1 => PixelFormat.Red, 3 => PixelFormat.Rgb, 4 => PixelFormat.Rgba, _ => PixelFormat.Rgb };

        // Level 0: full brightness
        int mipW = img.Width, mipH = img.Height;
        int maxLevels = 1 + (int)MathF.Floor(MathF.Log2(MathF.Max(mipW, mipH)));
        TexParam(TextureParameterName.TextureMaxLevel, maxLevels - 1);

        fixed (byte* px = img.Pixels)
            _gl.TexImage2D(TextureTarget.Texture2D, 0, format, (uint)mipW, (uint)mipH, 0, pixelFmt, PixelType.UnsignedByte, px);

        // Generate subsequent mip levels with brightness falloff
        byte[] src = img.Pixels;
        int srcW = mipW, srcH = mipH;
        float mapIndex = 0f;

        for (int level = 1; level < maxLevels; level++)
        {
            int newW = Math.Max(1, srcW / 2);
            int newH = Math.Max(1, srcH / 2);

            mapIndex += 1f;
            float brightness = Math.Clamp(c0 + mapIndex * c1 + mapIndex * mapIndex * c2, 0f, 1f);

            byte[] mip = DownscaleWithBrightness(src, srcW, srcH, ch, newW, newH, brightness);

            fixed (byte* px = mip)
                _gl.TexImage2D(TextureTarget.Texture2D, level, format, (uint)newW, (uint)newH, 0, pixelFmt, PixelType.UnsignedByte, px);

            src = mip;
            srcW = newW;
            srcH = newH;
        }

        return tex;
    }

    /// <summary>
    /// Downscales an image by 2x using box filter, then applies a brightness multiplier to RGB channels.
    /// </summary>
    private static byte[] DownscaleWithBrightness(byte[] src, int srcW, int srcH, int ch, int dstW, int dstH, float brightness)
    {
        var dst = new byte[dstW * dstH * ch];
        for (int y = 0; y < dstH; y++)
        {
            for (int x = 0; x < dstW; x++)
            {
                int sx = x * 2, sy = y * 2;
                int sx1 = Math.Min(sx + 1, srcW - 1);
                int sy1 = Math.Min(sy + 1, srcH - 1);

                for (int c = 0; c < ch; c++)
                {
                    bool isAlpha = (ch == 4 && c == 3);
                    int sum = src[(sy * srcW + sx) * ch + c]
                            + src[(sy * srcW + sx1) * ch + c]
                            + src[(sy1 * srcW + sx) * ch + c]
                            + src[(sy1 * srcW + sx1) * ch + c];
                    float avg = sum / 4f;
                    // Apply brightness to color channels only, not alpha
                    if (!isAlpha) avg *= brightness;
                    dst[(y * dstW + x) * ch + c] = (byte)Math.Clamp((int)avg, 0, 255);
                }
            }
        }
        return dst;
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
                    TexParam(TextureParameterName.TextureWrapS, (int)GLEnum.Repeat);
                    TexParam(TextureParameterName.TextureWrapT, (int)GLEnum.Repeat);
                    TexParam(TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
                    TexParam(TextureParameterName.TextureMinFilter, (int)GLEnum.LinearMipmapLinear);

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
        if (_domeTex != 0) _gl.DeleteTexture(_domeTex);

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
        if (_domeShader != 0) _gl.DeleteProgram(_domeShader);
    }

    public void Dispose() => Cleanup();

    #region Dome & Sea mesh generation

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

    public unsafe void UploadDome(Gb2Object dome, TgaImage? texture)
    {
        // Delete previous dome GPU resources
        if (_domeVao != 0) { _gl.DeleteVertexArray(_domeVao); _domeVao = 0; }
        if (_domeVbo != 0) { _gl.DeleteBuffer(_domeVbo); _domeVbo = 0; }
        if (_domeEbo != 0) { _gl.DeleteBuffer(_domeEbo); _domeEbo = 0; }
        if (_domeTex != 0) { _gl.DeleteTexture(_domeTex); _domeTex = 0; }
        _domeHasTexture = false;
        _domeIndexCount = 0;

        if (dome.Vertices.Length == 0 || dome.Triangles.Length == 0)
            return;
        if (!dome.HasUVs || dome.UVs.Length != dome.Vertices.Length || texture == null)
            return;

        // Build interleaved vertex data: position(3) + uv(2)
        const int stride = 5;
        var verts = new float[dome.Vertices.Length * stride];
        for (int i = 0; i < dome.Vertices.Length; i++)
        {
            int off = i * stride;
            verts[off + 0] = dome.Vertices[i].X;
            verts[off + 1] = dome.Vertices[i].Y;
            verts[off + 2] = dome.Vertices[i].Z;
            verts[off + 3] = dome.UVs[i][0];
            verts[off + 4] = dome.UVs[i][1];
        }

        // Convert triangle indices from int to uint
        var indices = new uint[dome.Triangles.Length];
        for (int i = 0; i < dome.Triangles.Length; i++)
            indices[i] = (uint)dome.Triangles[i];

        _domeVao = _gl.GenVertexArray();
        _gl.BindVertexArray(_domeVao);

        _domeVbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _domeVbo);
        fixed (float* p = verts)
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(verts.Length * sizeof(float)),
                p, BufferUsageARB.StaticDraw);

        // Position (location 0)
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride * sizeof(float), (void*)0);
        _gl.EnableVertexAttribArray(0);
        // UV (location 1)
        _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride * sizeof(float),
            (void*)(3 * sizeof(float)));
        _gl.EnableVertexAttribArray(1);

        _domeEbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _domeEbo);
        fixed (uint* p = indices)
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indices.Length * sizeof(uint)),
                p, BufferUsageARB.StaticDraw);

        _domeIndexCount = indices.Length;
        _gl.BindVertexArray(0);

        _domeTex = UploadTerrainTex(texture);
        _domeHasTexture = _domeTex != 0;
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
        layout(location = 2) in vec4 aBumpDiffuse;
        uniform mat4 uMVP;
        out vec4 vColor;
        out vec4 vBumpDiffuse;
        out vec3 vWorldPos;
        void main() {
            gl_Position = uMVP * vec4(aPos, 1.0);
            vColor = aColor;
            vBumpDiffuse = aBumpDiffuse;
            vWorldPos = aPos;
        }
        """;

    private const string TerrainFragSrc = """
        #version 300 es
        precision highp float;
        in vec4 vColor;
        in vec4 vBumpDiffuse;
        in vec3 vWorldPos;
        uniform int uHasTex;
        uniform sampler2D uGroundTex;
        uniform sampler2D uSlopeTex;
        uniform sampler2D uWallTex;
        uniform float uGroundWrap;
        uniform float uSlopeWrap;
        uniform float uWallWrap;
        uniform int uHasBump;
        uniform sampler2D uBumpTex;
        uniform float uBumpWrap;
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
        vec4 bx2(vec4 x) { return 2.0 * x - 1.0; }

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

                if (uHasBump == 1) {
                    // Dot3 bump mapping: bump texture dotted with per-vertex light direction
                    vec2 bumpUV = vWorldPos.xy / uBumpWrap;
                    vec4 bumpTexColor = bx2(textureNoTile(uBumpTex, bumpUV));
                    vec4 bumpDiffuse = bx2(vBumpDiffuse);
                    float dot3Light = clamp(dot(bumpTexColor.rgb, bumpDiffuse.rgb), 0.0, 1.0);

                    // Game formula: 2.0 * (dot3Light * diffuseTexture + bakedLightmap * 0.5)
                    FragColor = vec4(2.0 * (dot3Light * texCol + vColor.rgb * 0.5), 1.0);
                } else {
                    // Non-bump: game uses saturate(texture + lightmap)
                    FragColor = vec4(clamp(texCol + vColor.rgb, 0.0, 1.0), 1.0);
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

    private const string DomeVertSrc = """
        #version 300 es
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec2 aUV;
        uniform mat4 uMVP;
        out vec2 vUV;
        void main() {
            gl_Position = uMVP * vec4(aPos, 1.0);
            vUV = aUV;
        }
        """;

    private const string DomeFragSrc = """
        #version 300 es
        precision mediump float;
        uniform sampler2D uTex;
        in vec2 vUV;
        out vec4 FragColor;
        void main() {
            FragColor = texture(uTex, vUV);
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
