using Silk.NET.OpenGL;

namespace Neko.Renderer;

internal sealed class Texture : IDisposable
{
    private readonly GL _gl;
    public uint Id { get; }

    public unsafe Texture(GL gl, ReadOnlySpan<byte> rgba, int width, int height)
    {
        _gl = gl;
        Id = _gl.GenTexture();
        _gl.BindTexture(GLEnum.Texture2D, Id);
        fixed (byte* p = rgba)
        {
            _gl.TexImage2D(GLEnum.Texture2D, 0, (int)GLEnum.Rgba8,
                (uint)width, (uint)height, 0,
                GLEnum.Rgba, GLEnum.UnsignedByte, p);
        }
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)GLEnum.ClampToEdge);
    }

    public void Bind(uint unit)
    {
        _gl.ActiveTexture(GLEnum.Texture0 + (int)unit);
        _gl.BindTexture(GLEnum.Texture2D, Id);
    }

    public void Dispose() => _gl.DeleteTexture(Id);
}
