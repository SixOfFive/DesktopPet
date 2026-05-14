using System.Numerics;
using Silk.NET.OpenGL;

namespace Neko.Renderer;

internal sealed class Shader : IDisposable
{
    private readonly GL _gl;
    private readonly Dictionary<string, int> _uniforms = new();
    public uint Program { get; }

    public Shader(GL gl, string vertSrc, string fragSrc)
    {
        _gl = gl;
        uint vs = Compile(GLEnum.VertexShader, vertSrc);
        uint fs = Compile(GLEnum.FragmentShader, fragSrc);
        Program = _gl.CreateProgram();
        _gl.AttachShader(Program, vs);
        _gl.AttachShader(Program, fs);
        _gl.LinkProgram(Program);
        _gl.GetProgram(Program, GLEnum.LinkStatus, out int status);
        if (status == 0)
            throw new InvalidOperationException("Shader link failed: " + _gl.GetProgramInfoLog(Program));
        _gl.DetachShader(Program, vs);
        _gl.DetachShader(Program, fs);
        _gl.DeleteShader(vs);
        _gl.DeleteShader(fs);
    }

    private uint Compile(GLEnum type, string src)
    {
        uint id = _gl.CreateShader(type);
        _gl.ShaderSource(id, src);
        _gl.CompileShader(id);
        _gl.GetShader(id, GLEnum.CompileStatus, out int status);
        if (status == 0)
            throw new InvalidOperationException($"Shader compile ({type}) failed: {_gl.GetShaderInfoLog(id)}");
        return id;
    }

    public void Use() => _gl.UseProgram(Program);

    private int Loc(string name)
    {
        if (!_uniforms.TryGetValue(name, out int loc))
        {
            loc = _gl.GetUniformLocation(Program, name);
            _uniforms[name] = loc;
        }
        return loc;
    }

    public unsafe void SetMat4(string name, Matrix4x4 m)
    {
        _gl.UniformMatrix4(Loc(name), 1, false, (float*)&m);
    }

    public unsafe void SetMat4Array(string name, Matrix4x4[] mats)
    {
        if (mats.Length == 0) return;
        fixed (Matrix4x4* p = mats)
            _gl.UniformMatrix4(Loc(name), (uint)mats.Length, false, (float*)p);
    }

    public unsafe void SetMat3FromMat4(string name, Matrix4x4 m)
    {
        float* data = stackalloc float[9]
        {
            m.M11, m.M12, m.M13,
            m.M21, m.M22, m.M23,
            m.M31, m.M32, m.M33,
        };
        _gl.UniformMatrix3(Loc(name), 1, false, data);
    }

    public void SetInt(string name, int v) => _gl.Uniform1(Loc(name), v);

    public void Dispose() => _gl.DeleteProgram(Program);
}
