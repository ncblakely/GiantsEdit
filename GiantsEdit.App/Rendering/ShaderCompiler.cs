using Silk.NET.OpenGL;

namespace GiantsEdit.App.Rendering;

/// <summary>
/// Static utility for compiling and linking GLSL shaders.
/// </summary>
internal static class ShaderCompiler
{
    /// <summary>
    /// Compiles a vertex/fragment pair and links them into a program.
    /// </summary>
    public static uint CreateShader(GL gl, string vertSrc, string fragSrc)
    {
        uint vs = CompileShader(gl, ShaderType.VertexShader, vertSrc);
        uint fs = CompileShader(gl, ShaderType.FragmentShader, fragSrc);

        uint prog = gl.CreateProgram();
        gl.AttachShader(prog, vs);
        gl.AttachShader(prog, fs);
        gl.LinkProgram(prog);

        gl.GetProgram(prog, ProgramPropertyARB.LinkStatus, out int status);
        if (status == 0)
        {
            string log = gl.GetProgramInfoLog(prog);
            throw new InvalidOperationException($"Shader link failed: {log}");
        }

        gl.DeleteShader(vs);
        gl.DeleteShader(fs);
        return prog;
    }

    private static uint CompileShader(GL gl, ShaderType type, string src)
    {
        uint s = gl.CreateShader(type);
        gl.ShaderSource(s, src);
        gl.CompileShader(s);

        gl.GetShader(s, ShaderParameterName.CompileStatus, out int status);
        if (status == 0)
        {
            string log = gl.GetShaderInfoLog(s);
            throw new InvalidOperationException($"Shader compile ({type}) failed: {log}");
        }
        return s;
    }
}
