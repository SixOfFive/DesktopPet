using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

namespace Neko.Renderer;

[StructLayout(LayoutKind.Sequential)]
internal struct Vertex
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector2 Uv;
    public Vector4 Color;

    public const int SizeBytes = 12 + 12 + 8 + 16;
}

internal sealed class Mesh : IDisposable
{
    private readonly GL _gl;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly uint _ebo;
    private readonly uint _indexCount;
    public Texture? Texture { get; set; }

    public unsafe Mesh(GL gl, ReadOnlySpan<Vertex> vertices, ReadOnlySpan<uint> indices)
    {
        _gl = gl;
        _vao = _gl.GenVertexArray();
        _gl.BindVertexArray(_vao);

        _vbo = _gl.GenBuffer();
        _gl.BindBuffer(GLEnum.ArrayBuffer, _vbo);
        fixed (Vertex* p = vertices)
            _gl.BufferData(GLEnum.ArrayBuffer, (nuint)(vertices.Length * Vertex.SizeBytes), p, GLEnum.StaticDraw);

        _ebo = _gl.GenBuffer();
        _gl.BindBuffer(GLEnum.ElementArrayBuffer, _ebo);
        fixed (uint* p = indices)
            _gl.BufferData(GLEnum.ElementArrayBuffer, (nuint)(indices.Length * sizeof(uint)), p, GLEnum.StaticDraw);

        _indexCount = (uint)indices.Length;

        uint stride = Vertex.SizeBytes;
        _gl.VertexAttribPointer(0, 3, GLEnum.Float, false, stride, (void*)0);
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(1, 3, GLEnum.Float, false, stride, (void*)12);
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(2, 2, GLEnum.Float, false, stride, (void*)24);
        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(3, 4, GLEnum.Float, false, stride, (void*)32);
        _gl.EnableVertexAttribArray(3);

        _gl.BindVertexArray(0);
    }

    public unsafe void Draw()
    {
        Texture?.Bind(0);
        _gl.BindVertexArray(_vao);
        _gl.DrawElements(GLEnum.Triangles, _indexCount, GLEnum.UnsignedInt, (void*)0);
    }

    public void Dispose()
    {
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteBuffer(_ebo);
    }
}
