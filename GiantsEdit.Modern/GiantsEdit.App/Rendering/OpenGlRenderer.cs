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

    // Terrain
    private uint _terrainVao;
    private uint _terrainVboPos;
    private uint _terrainVboColor;
    private uint _terrainEbo;
    private int _terrainIndexCount;
    private uint _terrainLineEbo;
    private int _terrainLineIndexCount;

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
    private int _solidMvpLoc;
    private int _solidColorLoc;
    private int _modelMvpLoc;
    private int _modelModelLoc;

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

        _terrainShader = CreateShader(TerrainVertSrc, TerrainFragSrc);
        _terrainMvpLoc = _gl.GetUniformLocation(_terrainShader, "uMVP");

        _solidShader = CreateShader(SolidVertSrc, SolidFragSrc);
        _solidMvpLoc = _gl.GetUniformLocation(_solidShader, "uMVP");
        _solidColorLoc = _gl.GetUniformLocation(_solidShader, "uColor");

        _modelShader = CreateShader(ModelVertSrc, ModelFragSrc);
        _modelMvpLoc = _gl.GetUniformLocation(_modelShader, "uMVP");
        _modelModelLoc = _gl.GetUniformLocation(_modelShader, "uModel");

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
    }

    public void Resize(int width, int height)
    {
        _viewportWidth = width;
        _viewportHeight = height;
        _gl.Viewport(0, 0, (uint)width, (uint)height);
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
            _gl.BindVertexArray(_terrainVao);

            if (state.ShowTerrainMesh)
            {
                // Wireframe overlay: draw edges as lines
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
            _gl.UseProgram(_modelShader);
            SetUniformMatrix(_modelMvpLoc, vp);
            _gl.Disable(EnableCap.CullFace);

            foreach (var obj in state.Objects)
            {
                // Try real model first, then mapobj shape
                ModelGpuData gpuData;
                bool isRealModel = false;
                if (_models.TryGetValue(obj.ModelId, out gpuData))
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

                // Delphi DrawObject only applies scale when DrawRealObjects is true.
                // For Mapobj shapes, only translate + Z-rotate.
                Matrix4x4 model;
                if (isRealModel)
                {
                    model = Matrix4x4.CreateScale(obj.Scale)
                        * Matrix4x4.CreateRotationZ(obj.Rotation.Z)
                        * Matrix4x4.CreateRotationY(obj.Rotation.Y)
                        * Matrix4x4.CreateRotationX(obj.Rotation.X)
                        * Matrix4x4.CreateTranslation(obj.Position);
                }
                else
                {
                    model = Matrix4x4.CreateRotationZ(obj.Rotation.Z)
                        * Matrix4x4.CreateTranslation(obj.Position);
                }

                SetUniformMatrix(_modelModelLoc, model);
                _gl.BindVertexArray(gpuData.Vao);
                _gl.DrawElements(PrimitiveType.Triangles, (uint)gpuData.IndexCount,
                    DrawElementsType.UnsignedInt, null);
            }
            _gl.Enable(EnableCap.CullFace);
        }

        // Draw spline lines
        if (state.ShowObjects && state.SplineLines.Count > 0)
        {
            _gl.UseProgram(_solidShader);
            SetUniformMatrix(_solidMvpLoc, vp);
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

        _gl.BindVertexArray(0);
        _gl.UseProgram(0);
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
    }

    public unsafe int UploadModel(ModelRenderData model)
    {
        int id = _nextModelId++;

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

    public void Cleanup()
    {
        if (_terrainVao != 0) _gl.DeleteVertexArray(_terrainVao);
        if (_terrainVboPos != 0) _gl.DeleteBuffer(_terrainVboPos);
        if (_terrainVboColor != 0) _gl.DeleteBuffer(_terrainVboColor);
        if (_terrainEbo != 0) _gl.DeleteBuffer(_terrainEbo);

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
        void main() {
            gl_Position = uMVP * vec4(aPos, 1.0);
            vColor = aColor;
        }
        """;

    private const string TerrainFragSrc = """
        #version 300 es
        precision mediump float;
        in vec4 vColor;
        out vec4 FragColor;
        void main() {
            FragColor = vColor;
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
        void main() {
            gl_Position = uMVP * uModel * vec4(aPos, 1.0);
            vColor = aColor;
            vUV = aUV;
        }
        """;

    private const string ModelFragSrc = """
        #version 300 es
        precision mediump float;
        in vec3 vColor;
        in vec2 vUV;
        out vec4 FragColor;
        void main() {
            FragColor = vec4(vColor, 1.0);
        }
        """;

    #endregion

    private struct ModelGpuData
    {
        public uint Vao, Vbo, Ebo;
        public int IndexCount;
    }
}
