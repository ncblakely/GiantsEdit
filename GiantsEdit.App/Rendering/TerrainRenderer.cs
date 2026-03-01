using System.Numerics;
using GiantsEdit.Core.Rendering;
using Silk.NET.OpenGL;

namespace GiantsEdit.App.Rendering;

/// <summary>
/// Handles terrain mesh GPU upload and rendering.
/// </summary>
internal sealed class TerrainRenderer
{
    private readonly GL _gl;

    // VAO/VBO state
    private uint _vao;
    private uint _vboPos;
    private uint _vboColor;
    private uint _vboBumpDiffuse;
    private uint _ebo;
    private int _indexCount;
    private uint _lineEbo;
    private int _lineIndexCount;

    // Textures
    private uint _texGround;
    private uint _texSlope;
    private uint _texWall;
    private uint _bumpTex;
    private bool _hasTextures;
    private bool _hasBump;

    // Uniform locations (cached from shader)
    private readonly int _mvpLoc;
    private readonly int _hasTexLoc;
    private readonly int _groundTexLoc;
    private readonly int _slopeTexLoc;
    private readonly int _wallTexLoc;
    private readonly int _groundWrapLoc;
    private readonly int _slopeWrapLoc;
    private readonly int _wallWrapLoc;
    private readonly int _bumpTexLoc;
    private readonly int _bumpWrapLoc;
    private readonly int _hasBumpLoc;
    private readonly uint _shader;

    public TerrainRenderer(GL gl, uint shader)
    {
        _gl = gl;
        _shader = shader;
        _mvpLoc = gl.GetUniformLocation(shader, "uMVP");
        _hasTexLoc = gl.GetUniformLocation(shader, "uHasTex");
        _groundTexLoc = gl.GetUniformLocation(shader, "uGroundTex");
        _slopeTexLoc = gl.GetUniformLocation(shader, "uSlopeTex");
        _wallTexLoc = gl.GetUniformLocation(shader, "uWallTex");
        _groundWrapLoc = gl.GetUniformLocation(shader, "uGroundWrap");
        _slopeWrapLoc = gl.GetUniformLocation(shader, "uSlopeWrap");
        _wallWrapLoc = gl.GetUniformLocation(shader, "uWallWrap");
        _bumpTexLoc = gl.GetUniformLocation(shader, "uBumpTex");
        _bumpWrapLoc = gl.GetUniformLocation(shader, "uBumpWrap");
        _hasBumpLoc = gl.GetUniformLocation(shader, "uHasBump");
    }

    public bool HasData => _indexCount > 0;

    public unsafe void Upload(TerrainRenderData terrain)
    {
        // Clean up previous terrain buffers
        if (_vao != 0)
        {
            _gl.DeleteVertexArray(_vao);
            _gl.DeleteBuffer(_vboPos);
            _gl.DeleteBuffer(_vboColor);
            if (_vboBumpDiffuse != 0) _gl.DeleteBuffer(_vboBumpDiffuse);
            _gl.DeleteBuffer(_ebo);
            if (_lineEbo != 0) _gl.DeleteBuffer(_lineEbo);
        }
        _vboBumpDiffuse = 0;
        _lineEbo = 0;

        _vao = _gl.GenVertexArray();
        _gl.BindVertexArray(_vao);

        // Position buffer (location 0)
        _vboPos = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboPos);
        fixed (float* p = terrain.Positions)
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(terrain.Positions.Length * sizeof(float)),
                p, BufferUsageARB.StaticDraw);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), null);
        _gl.EnableVertexAttribArray(0);

        // Color buffer (location 1) — packed RGBA uint
        _vboColor = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboColor);
        fixed (uint* p = terrain.Colors)
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(terrain.Colors.Length * sizeof(uint)),
                p, BufferUsageARB.StaticDraw);
        _gl.VertexAttribPointer(1, 4, VertexAttribPointerType.UnsignedByte, true, sizeof(uint), null);
        _gl.EnableVertexAttribArray(1);

        // Bump diffuse buffer (location 2) — per-vertex sun direction in tangent space
        if (terrain.BumpDiffuseColors != null)
        {
            _vboBumpDiffuse = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboBumpDiffuse);
            fixed (uint* p = terrain.BumpDiffuseColors)
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(terrain.BumpDiffuseColors.Length * sizeof(uint)),
                    p, BufferUsageARB.StaticDraw);
            _gl.VertexAttribPointer(2, 4, VertexAttribPointerType.UnsignedByte, true, sizeof(uint), null);
            _gl.EnableVertexAttribArray(2);
        }

        // Index buffer
        _ebo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        fixed (uint* p = terrain.Indices)
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(terrain.Indices.Length * sizeof(uint)),
                p, BufferUsageARB.StaticDraw);

        _indexCount = terrain.IndexCount;

        // Build wireframe line indices from triangles (each triangle → 3 edges)

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

        _lineEbo = _gl.GenBuffer();
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _lineEbo);
        fixed (uint* p = lineIndices)
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(lineIndices.Length * sizeof(uint)),
                p, BufferUsageARB.StaticDraw);
        _lineIndexCount = lineIndices.Length;

        // Re-bind the triangle EBO as default
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);

        _gl.BindVertexArray(0);

        // Upload terrain textures
        DeleteTextures();
        _hasTextures = false;
        _hasBump = false;

        if (terrain.Textures is { } tex)
        {
            _texGround = TextureUploader.UploadTerrainTexWithFalloff(_gl, tex.GroundImage, tex.MipFalloff0, tex.MipFalloff1, tex.MipFalloff2);
            _texSlope = TextureUploader.UploadTerrainTexWithFalloff(_gl, tex.SlopeImage ?? tex.GroundImage, tex.MipFalloff0, tex.MipFalloff1, tex.MipFalloff2);
            _texWall = TextureUploader.UploadTerrainTexWithFalloff(_gl, tex.WallImage ?? tex.SlopeImage ?? tex.GroundImage, tex.MipFalloff0, tex.MipFalloff1, tex.MipFalloff2);

            if (_texGround != 0)
            {
                _hasTextures = true;

                _gl.UseProgram(_shader);
                _gl.Uniform1(_groundWrapLoc, tex.GroundWrap);
                _gl.Uniform1(_slopeWrapLoc, tex.SlopeWrap);
                _gl.Uniform1(_wallWrapLoc, tex.WallWrap);
            }

            // Upload single bump texture (ground bump) for dot3 bump mapping
            _bumpTex = TextureUploader.UploadBumpTexture(_gl, tex.GroundNormalImage);
            if (_bumpTex != 0 && terrain.BumpDiffuseColors != null)
            {
                _hasBump = true;

                _gl.UseProgram(_shader);
                _gl.Uniform1(_bumpWrapLoc, tex.GroundNormalWrap);
            }
        }
    }

    public unsafe void Draw(Matrix4x4 vp, bool showMesh)
    {
        _gl.Disable(EnableCap.CullFace);
        _gl.UseProgram(_shader);
        SetUniformMatrix(_mvpLoc, vp);

        // Bind terrain textures if available
        _gl.Uniform1(_hasTexLoc, _hasTextures ? 1 : 0);
        if (_hasTextures)
        {
            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindTexture(TextureTarget.Texture2D, _texGround);
            _gl.Uniform1(_groundTexLoc, 0);

            _gl.ActiveTexture(TextureUnit.Texture1);
            _gl.BindTexture(TextureTarget.Texture2D, _texSlope);
            _gl.Uniform1(_slopeTexLoc, 1);

            _gl.ActiveTexture(TextureUnit.Texture2);
            _gl.BindTexture(TextureTarget.Texture2D, _texWall);
            _gl.Uniform1(_wallTexLoc, 2);

            // Bind bump texture on unit 3
            _gl.Uniform1(_hasBumpLoc, _hasBump ? 1 : 0);
            if (_hasBump)
            {
                _gl.ActiveTexture(TextureUnit.Texture3);
                _gl.BindTexture(TextureTarget.Texture2D, _bumpTex);
                _gl.Uniform1(_bumpTexLoc, 3);
            }

            _gl.ActiveTexture(TextureUnit.Texture0);
        }

        _gl.BindVertexArray(_vao);

        if (showMesh)
        {
            // Wireframe: disable textures, use vertex colors only
            _gl.Uniform1(_hasTexLoc, 0);
            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _lineEbo);
            _gl.DrawElements(PrimitiveType.Lines, (uint)_lineIndexCount, DrawElementsType.UnsignedInt, null);
            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        }
        else
        {
            _gl.DrawElements(PrimitiveType.Triangles, (uint)_indexCount, DrawElementsType.UnsignedInt, null);
        }

        _gl.Enable(EnableCap.CullFace);
    }

    public void Cleanup()
    {
        if (_vao != 0) _gl.DeleteVertexArray(_vao);
        if (_vboPos != 0) _gl.DeleteBuffer(_vboPos);
        if (_vboColor != 0) _gl.DeleteBuffer(_vboColor);
        if (_ebo != 0) _gl.DeleteBuffer(_ebo);
        DeleteTextures();
    }

    private void DeleteTextures()
    {
        if (_texGround != 0) { _gl.DeleteTexture(_texGround); _texGround = 0; }
        if (_texSlope != 0) { _gl.DeleteTexture(_texSlope); _texSlope = 0; }
        if (_texWall != 0) { _gl.DeleteTexture(_texWall); _texWall = 0; }
        if (_bumpTex != 0) { _gl.DeleteTexture(_bumpTex); _bumpTex = 0; }
    }

    private unsafe void SetUniformMatrix(int location, Matrix4x4 mat)
    {
        float* p = (float*)&mat;
        _gl.UniformMatrix4(location, 1, false, p);
    }
}
