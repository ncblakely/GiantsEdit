using System.Numerics;
using Silk.NET.OpenGL;

namespace GiantsEdit.App.Rendering;

/// <summary>
/// Handles sea surface and sea ground rendering.
/// </summary>
internal sealed class SeaRenderer
{
    private const int Segments = 32;
    private const float SeaGroundDepth = -42f;
    private const int FloatsPerTriangleVertex = 9; // 3 verts * 3 floats

    private readonly GL _gl;

    private uint _vao;
    private uint _vbo;
    private int _vertexCount;

    // Solid shader uniforms (shared with other renderers)
    private readonly uint _solidShader;
    private readonly int _mvpLoc;
    private readonly int _colorLoc;

    public SeaRenderer(GL gl, uint solidShader, int mvpLoc, int colorLoc)
    {
        _gl = gl;
        _solidShader = solidShader;
        _mvpLoc = mvpLoc;
        _colorLoc = colorLoc;
    }

    public bool HasData => _vertexCount > 0;

    public unsafe void Build(float radius)
    {
        // Fan of triangles: 3 vertices per segment
        var verts = new float[Segments * FloatsPerTriangleVertex];
        for (int i = 0; i < Segments; i++)
        {
            float a1 = i * MathF.PI * 2f / Segments;
            float a2 = (i + 1) * MathF.PI * 2f / Segments;

            int off = i * 9;
            verts[off + 0] = 0; verts[off + 1] = 0; verts[off + 2] = SeaGroundDepth;
            verts[off + 3] = MathF.Cos(a1) * radius;
            verts[off + 4] = MathF.Sin(a1) * radius;
            verts[off + 5] = SeaGroundDepth;
            verts[off + 6] = MathF.Cos(a2) * radius;
            verts[off + 7] = MathF.Sin(a2) * radius;
            verts[off + 8] = SeaGroundDepth;
        }

        _vao = _gl.GenVertexArray();
        _gl.BindVertexArray(_vao);

        _vbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (float* p = verts)
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(verts.Length * sizeof(float)),
                p, BufferUsageARB.StaticDraw);

        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), null);
        _gl.EnableVertexAttribArray(0);

        _vertexCount = Segments * 3;
        _gl.BindVertexArray(0);
    }

    public unsafe void Draw(Matrix4x4 vp, Vector3 seaColor)
    {
        _gl.Disable(EnableCap.CullFace);
        _gl.UseProgram(_solidShader);
        SetUniformMatrix(_mvpLoc, vp);

        // Draw sea ground
        _gl.Uniform4(_colorLoc, 0.05f, 0.15f, 0.1f, 1.0f);
        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_vertexCount);

        // Draw sea surface with blending
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.Uniform4(_colorLoc, seaColor.X, seaColor.Y, seaColor.Z, 0.5f);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_vertexCount);
        _gl.Disable(EnableCap.Blend);
        _gl.Enable(EnableCap.CullFace);
    }

    public void Cleanup()
    {
        if (_vao != 0) _gl.DeleteVertexArray(_vao);
        if (_vbo != 0) _gl.DeleteBuffer(_vbo);
    }

    private unsafe void SetUniformMatrix(int location, Matrix4x4 mat)
    {
        float* p = (float*)&mat;
        _gl.UniformMatrix4(location, 1, false, p);
    }
}
