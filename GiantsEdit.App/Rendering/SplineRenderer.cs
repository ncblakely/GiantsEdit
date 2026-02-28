using System.Numerics;
using GiantsEdit.Core.Rendering;
using Silk.NET.OpenGL;

namespace GiantsEdit.App.Rendering;

/// <summary>
/// Handles spline line rendering for waypoint connections.
/// </summary>
internal sealed class SplineRenderer
{
    private readonly GL _gl;

    private uint _lineVao;
    private uint _lineVbo;

    private readonly uint _solidShader;
    private readonly int _mvpLoc;
    private readonly int _colorLoc;

    public SplineRenderer(GL gl, uint solidShader, int mvpLoc, int colorLoc)
    {
        _gl = gl;
        _solidShader = solidShader;
        _mvpLoc = mvpLoc;
        _colorLoc = colorLoc;
    }

    public uint LineVao => _lineVao;
    public uint LineVbo => _lineVbo;

    public unsafe void Init()
    {
        _lineVao = _gl.GenVertexArray();
        _lineVbo = _gl.GenBuffer();
        _gl.BindVertexArray(_lineVao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _lineVbo);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), (void*)0);
        _gl.EnableVertexAttribArray(0);
        _gl.BindVertexArray(0);
    }

    public unsafe void Draw(RenderState state, Matrix4x4 vp)
    {
        _gl.UseProgram(_solidShader);
        SetUniformMatrix(_mvpLoc, vp);
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

            _gl.Uniform4(_colorLoc, spline.Color.X, spline.Color.Y, spline.Color.Z, 1.0f);
            _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)spline.PointCount);
        }

        _gl.Enable(EnableCap.DepthTest);
    }

    public void Cleanup()
    {
        if (_lineVao != 0) _gl.DeleteVertexArray(_lineVao);
        if (_lineVbo != 0) _gl.DeleteBuffer(_lineVbo);
    }

    private unsafe void SetUniformMatrix(int location, Matrix4x4 mat)
    {
        float* p = (float*)&mat;
        _gl.UniformMatrix4(location, 1, false, p);
    }
}
