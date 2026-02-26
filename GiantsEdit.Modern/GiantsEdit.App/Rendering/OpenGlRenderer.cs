using System.Numerics;
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

    /// <summary>
    /// Sets the GL context. Must be called before Init().
    /// </summary>
    public void SetGlContext(GL gl)
    {
        _gl = gl;
    }

    public void Init(int viewportWidth, int viewportHeight)
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
            _gl.DrawElements(PrimitiveType.Triangles, (uint)_terrainIndexCount, DrawElementsType.UnsignedInt, null);
            _gl.Enable(EnableCap.CullFace);
        }

        // Draw objects
        if (state.ShowObjects)
        {
            _gl.UseProgram(_modelShader);
            SetUniformMatrix(_modelMvpLoc, vp);

            foreach (var obj in state.Objects)
            {
                if (!_models.TryGetValue(obj.ModelId, out var gpuData))
                    continue;

                var model = Matrix4x4.CreateScale(obj.Scale)
                    * Matrix4x4.CreateRotationZ(obj.Rotation.Z)
                    * Matrix4x4.CreateRotationY(obj.Rotation.Y)
                    * Matrix4x4.CreateRotationX(obj.Rotation.X)
                    * Matrix4x4.CreateTranslation(obj.Position);

                SetUniformMatrix(_modelModelLoc, model);
                _gl.BindVertexArray(gpuData.Vao);
                _gl.DrawElements(PrimitiveType.Triangles, (uint)gpuData.IndexCount,
                    DrawElementsType.UnsignedInt, null);
            }
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

        foreach (var m in _models.Values)
        {
            _gl.DeleteVertexArray(m.Vao);
            _gl.DeleteBuffer(m.Vbo);
            _gl.DeleteBuffer(m.Ebo);
        }
        _models.Clear();

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
