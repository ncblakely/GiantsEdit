using System.Numerics;
using GiantsEdit.Core.Formats;
using Silk.NET.OpenGL;

namespace GiantsEdit.App.Rendering;

/// <summary>
/// Handles dome mesh GPU upload and rendering.
/// </summary>
internal sealed class DomeRenderer
{
    private const int VertexStride = 5; // position(3) + uv(2)

    private readonly GL _gl;

    private uint _vao;
    private uint _vbo;
    private uint _ebo;
    private int _indexCount;
    private uint _tex;
    private bool _hasTexture;

    private readonly uint _shader;
    private readonly int _mvpLoc;
    private readonly int _texLoc;

    public DomeRenderer(GL gl, uint shader)
    {
        _gl = gl;
        _shader = shader;
        _mvpLoc = gl.GetUniformLocation(shader, "uMVP");
        _texLoc = gl.GetUniformLocation(shader, "uTex");
    }

    public bool HasData => _indexCount > 0 && _hasTexture;

    public unsafe void Upload(Gb2Object dome, TgaImage? texture)
    {
        // Delete previous dome GPU resources
        if (_vao != 0) { _gl.DeleteVertexArray(_vao); _vao = 0; }
        if (_vbo != 0) { _gl.DeleteBuffer(_vbo); _vbo = 0; }
        if (_ebo != 0) { _gl.DeleteBuffer(_ebo); _ebo = 0; }
        if (_tex != 0) { _gl.DeleteTexture(_tex); _tex = 0; }
        _hasTexture = false;
        _indexCount = 0;

        if (dome.Vertices.Length == 0 || dome.Triangles.Length == 0)
            return;
        if (!dome.HasUVs || dome.UVs.Length != dome.Vertices.Length || texture == null)
            return;

        // Build interleaved vertex data: position(3) + uv(2)
        var verts = new float[dome.Vertices.Length * VertexStride];
        for (int i = 0; i < dome.Vertices.Length; i++)
        {
            int off = i * VertexStride;
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

        _vao = _gl.GenVertexArray();
        _gl.BindVertexArray(_vao);

        _vbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (float* p = verts)
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(verts.Length * sizeof(float)),
                p, BufferUsageARB.StaticDraw);

        // Position (location 0)
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, VertexStride * sizeof(float), (void*)0);
        _gl.EnableVertexAttribArray(0);
        // UV (location 1)
        _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, VertexStride * sizeof(float),
            (void*)(3 * sizeof(float)));
        _gl.EnableVertexAttribArray(1);

        _ebo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        fixed (uint* p = indices)
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indices.Length * sizeof(uint)),
                p, BufferUsageARB.StaticDraw);

        _indexCount = indices.Length;
        _gl.BindVertexArray(0);

        _tex = TextureUploader.UploadTgaTexture(_gl, texture);
        _hasTexture = _tex != 0;
    }

    public unsafe void Draw(Matrix4x4 vp)
    {
        _gl.DepthMask(false);
        _gl.Disable(EnableCap.CullFace);
        _gl.UseProgram(_shader);
        SetUniformMatrix(_mvpLoc, vp);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _tex);
        _gl.Uniform1(_texLoc, 0);
        _gl.BindVertexArray(_vao);
        _gl.DrawElements(PrimitiveType.Triangles, (uint)_indexCount, DrawElementsType.UnsignedInt, null);
        _gl.Enable(EnableCap.CullFace);
        _gl.DepthMask(true);
    }

    public void Cleanup()
    {
        if (_vao != 0) _gl.DeleteVertexArray(_vao);
        if (_vbo != 0) _gl.DeleteBuffer(_vbo);
        if (_ebo != 0) _gl.DeleteBuffer(_ebo);
        if (_tex != 0) _gl.DeleteTexture(_tex);
    }

    private unsafe void SetUniformMatrix(int location, Matrix4x4 mat)
    {
        float* p = (float*)&mat;
        _gl.UniformMatrix4(location, 1, false, p);
    }
}
