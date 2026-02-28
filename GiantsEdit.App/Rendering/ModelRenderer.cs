using System.Numerics;
using GiantsEdit.Core.Formats;
using GiantsEdit.Core.Rendering;
using Silk.NET.OpenGL;

namespace GiantsEdit.App.Rendering;

/// <summary>
/// Handles model/object rendering including GBS models, map object shapes,
/// and selection box drawing.
/// </summary>
internal sealed class ModelRenderer
{
    private const int MaxLights = 4;
    private const int MapObjVertexStride = 11; // pos(3) + normal(3) + uv(2) + color(3)
    private const float DefaultBoundsHalf = 16f;

    private readonly GL _gl;

    // Shader and uniforms
    private readonly uint _modelShader;
    private readonly int _mvpLoc;
    private readonly int _modelLoc;
    private readonly int _hasTexLoc;
    private readonly int _texLoc;
    private readonly int _hasNormLoc;
    private readonly int _lightCountLoc;
    private readonly int[] _lightDirLocs = new int[MaxLights];
    private readonly int[] _lightColorLocs = new int[MaxLights];
    private readonly int _sceneAmbientLoc;
    private readonly int _matAmbientLoc;
    private readonly int _matDiffuseLoc;
    private readonly int _matEmissiveLoc;
    private readonly int _matSpecularLoc;
    private readonly int _matPowerLoc;
    private readonly int _cameraPosLoc;
    private readonly int _colorScaleLoc;

    // Solid shader for selection box
    private readonly uint _solidShader;
    private readonly int _solidMvpLoc;
    private readonly int _solidColorLoc;

    // Line VAO/VBO for selection box (shared with SplineRenderer via init)
    private uint _lineVao;
    private uint _lineVbo;

    // GPU data
    private readonly Dictionary<int, ModelGpuData> _models = new();
    private int _nextModelId;
    private readonly Dictionary<int, ModelGpuData> _mapObjShapes = new();
    private readonly Dictionary<int, int> _mapObjWrap = new();
    private bool _mapObjsLoaded;

    public ModelRenderer(GL gl, uint modelShader, uint solidShader, int solidMvpLoc, int solidColorLoc)
    {
        _gl = gl;
        _modelShader = modelShader;
        _solidShader = solidShader;
        _solidMvpLoc = solidMvpLoc;
        _solidColorLoc = solidColorLoc;

        _mvpLoc = gl.GetUniformLocation(modelShader, "uMVP");
        _modelLoc = gl.GetUniformLocation(modelShader, "uModel");
        _hasTexLoc = gl.GetUniformLocation(modelShader, "uHasTex");
        _texLoc = gl.GetUniformLocation(modelShader, "uTex");
        _hasNormLoc = gl.GetUniformLocation(modelShader, "uHasNormals");
        _lightCountLoc = gl.GetUniformLocation(modelShader, "uLightCount");
        _sceneAmbientLoc = gl.GetUniformLocation(modelShader, "uSceneAmbient");
        _matAmbientLoc = gl.GetUniformLocation(modelShader, "uMatAmbient");
        _matDiffuseLoc = gl.GetUniformLocation(modelShader, "uMatDiffuse");
        _matEmissiveLoc = gl.GetUniformLocation(modelShader, "uMatEmissive");
        _matSpecularLoc = gl.GetUniformLocation(modelShader, "uMatSpecular");
        _matPowerLoc = gl.GetUniformLocation(modelShader, "uMatPower");
        _cameraPosLoc = gl.GetUniformLocation(modelShader, "uCameraPos");
        _colorScaleLoc = gl.GetUniformLocation(modelShader, "uColorScale");
        for (int i = 0; i < MaxLights; i++)
        {
            _lightDirLocs[i] = gl.GetUniformLocation(modelShader, $"uLightDir[{i}]");
            _lightColorLocs[i] = gl.GetUniformLocation(modelShader, $"uLightColor[{i}]");
        }
    }

    /// <summary>
    /// Sets the shared line VAO/VBO (created by SplineRenderer or the orchestrator).
    /// </summary>
    public void SetLineBuffers(uint lineVao, uint lineVbo)
    {
        _lineVao = lineVao;
        _lineVbo = lineVbo;
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
                    uint tex = TextureUploader.UploadModelPartTexture(_gl, img);
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
            var verts = new float[vertCount * MapObjVertexStride];
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

            uint stride = MapObjVertexStride * sizeof(float);
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
                int off = vi * MapObjVertexStride;
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
            int off = vi * MapObjVertexStride;
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

    public unsafe void Draw(RenderState state, Matrix4x4 vp)
    {
        if (state.ViewObjThruTerrain)
            _gl.Disable(EnableCap.DepthTest);

        _gl.UseProgram(_modelShader);
        SetUniformMatrix(_mvpLoc, vp);
        _gl.Disable(EnableCap.CullFace);

        // Set up directional lights and scene ambient
        int lightCount = Math.Min(state.Lights.Count, MaxLights);
        _gl.Uniform1(_lightCountLoc, lightCount);
        Vector3 sceneAmbient = state.WorldAmbientColor;
        for (int i = 0; i < lightCount; i++)
        {
            var light = state.Lights[i];
            _gl.Uniform3(_lightDirLocs[i], light.Direction.X, light.Direction.Y, light.Direction.Z);
            _gl.Uniform3(_lightColorLocs[i], light.Color.X, light.Color.Y, light.Color.Z);
            sceneAmbient += light.Color;
        }
        sceneAmbient = Vector3.Min(sceneAmbient, Vector3.One);
        _gl.Uniform3(_sceneAmbientLoc, sceneAmbient.X, sceneAmbient.Y, sceneAmbient.Z);
        _gl.Uniform3(_cameraPosLoc, state.CameraPosition.X, state.CameraPosition.Y, state.CameraPosition.Z);

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

            SetUniformMatrix(_modelLoc, model);
            _gl.Uniform1(_hasNormLoc, gpuData.HasNormals ? 1 : 0);
            _gl.BindVertexArray(gpuData.Vao);

            if (gpuData.Parts != null && gpuData.Parts.Length > 0)
            {
                // Draw per-part with texture binding
                _gl.ActiveTexture(TextureUnit.Texture0);
                _gl.Uniform1(_texLoc, 0);

                foreach (var part in gpuData.Parts)
                {
                    // Set per-part material properties for lighting
                    _gl.Uniform3(_matAmbientLoc, part.MaterialAmbient.X, part.MaterialAmbient.Y, part.MaterialAmbient.Z);
                    _gl.Uniform3(_matDiffuseLoc, part.MaterialDiffuse.X, part.MaterialDiffuse.Y, part.MaterialDiffuse.Z);
                    _gl.Uniform3(_matEmissiveLoc, part.MaterialEmissive.X, part.MaterialEmissive.Y, part.MaterialEmissive.Z);
                    _gl.Uniform3(_matSpecularLoc, part.MaterialSpecular.X, part.MaterialSpecular.Y, part.MaterialSpecular.Z);
                    _gl.Uniform1(_matPowerLoc, part.SpecularPower);
                    _gl.Uniform1(_colorScaleLoc, part.Blend > 0f ? 4.0f : 0.0f);

                    if (part.TextureId != 0)
                    {
                        _gl.BindTexture(TextureTarget.Texture2D, part.TextureId);
                        _gl.Uniform1(_hasTexLoc, 1);

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
                        _gl.Uniform1(_hasTexLoc, 0);
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
                _gl.Uniform1(_hasTexLoc, 0);
                _gl.DrawElements(PrimitiveType.Triangles, (uint)gpuData.IndexCount,
                    DrawElementsType.UnsignedInt, null);
            }
        }
        _gl.Enable(EnableCap.CullFace);

        if (state.ViewObjThruTerrain)
            _gl.Enable(EnableCap.DepthTest);
    }

    public unsafe void DrawSelectionBox(RenderState state, Matrix4x4 vp)
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
            bMin = new Vector3(-DefaultBoundsHalf);
            bMax = new Vector3(DefaultBoundsHalf);
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
        const int BoxLineVertexCount = 24;
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

        _gl.DrawArrays(PrimitiveType.Lines, 0, BoxLineVertexCount);

        _gl.Disable(EnableCap.Blend);
        _gl.Enable(EnableCap.DepthTest);
    }

    public void Cleanup()
    {
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
    }

    /// <summary>
    /// Build a model matrix for a game object.
    /// </summary>
    internal static Matrix4x4 BuildObjectMatrix(in ObjectInstance obj)
    {
        float deg2rad = MathF.PI / 180f;
        float sx = MathF.Sin(-obj.TiltForward * deg2rad);
        float cx = MathF.Cos(-obj.TiltForward * deg2rad);
        float sy = MathF.Sin(obj.TiltLeft * deg2rad);
        float cy = MathF.Cos(obj.TiltLeft * deg2rad);
        float sz = MathF.Sin(obj.DirFacing * deg2rad);
        float cz = MathF.Cos(obj.DirFacing * deg2rad);
        float s = obj.Scale;

        return new Matrix4x4(
            cy * cz * s, cy * sz * s, sy * s, 0,
            (-sx * sy * cz - cx * sz) * s, (-sx * sy * sz + cx * cz) * s, sx * cy * s, 0,
            (-cx * sy * cz + sx * sz) * s, (-cx * sy * sz - sx * cz) * s, cx * cy * s, 0,
            obj.Position.X, obj.Position.Y, obj.Position.Z, 1
        );
    }

    private unsafe void SetUniformMatrix(int location, Matrix4x4 mat)
    {
        float* p = (float*)&mat;
        _gl.UniformMatrix4(location, 1, false, p);
    }

    internal struct ModelGpuData
    {
        public uint Vao, Vbo, Ebo;
        public int IndexCount;
        public ModelPartGpu[]? Parts;
        public Vector3 BoundsMin, BoundsMax;
        public bool HasNormals;
    }

    internal struct ModelPartGpu
    {
        public int IndexOffset;
        public int IndexCount;
        public uint TextureId;
        public bool HasAlpha;
        public Vector3 MaterialAmbient;
        public Vector3 MaterialDiffuse;
        public Vector3 MaterialEmissive;
        public Vector3 MaterialSpecular;
        public float SpecularPower;
        public float Blend;
    }
}
