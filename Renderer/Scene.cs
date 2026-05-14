using System.Drawing;
using System.Numerics;
using Silk.NET.OpenGL;
using GdiPixelFormat = System.Drawing.Imaging.PixelFormat;
using ImageLockMode = System.Drawing.Imaging.ImageLockMode;

namespace Neko.Renderer;

internal sealed class Scene : IDisposable
{
    private const string VertexSrc = @"#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec3 aNormal;
layout(location=2) in vec2 aUv;
layout(location=3) in vec4 aColor;
uniform mat4 uMVP;
uniform mat3 uNormal;
out vec3 vNormal;
out vec2 vUv;
out vec4 vColor;
void main() {
    gl_Position = uMVP * vec4(aPos, 1.0);
    vNormal = normalize(uNormal * aNormal);
    vUv = aUv;
    vColor = aColor;
}
";

    private const string FragmentSrc = @"#version 330 core
in vec3 vNormal;
in vec2 vUv;
in vec4 vColor;
uniform sampler2D uTex;
out vec4 fragColor;
void main() {
    vec3 lightDir = normalize(vec3(0.4, 0.85, 0.5));
    float ndl = max(dot(normalize(vNormal), lightDir), 0.0);
    float lambert = 0.45 + 0.55 * ndl;
    vec4 tex = texture(uTex, vUv);
    vec4 base = tex * vColor;
    fragColor = vec4(base.rgb * lambert, base.a);
}
";

    private readonly GL _gl;
    private readonly LoadedModel _model;
    private readonly Shader _shader;
    private readonly uint _fbo;
    private readonly uint _colorTex;
    private readonly uint _depthRbo;
    private readonly int _width;
    private readonly int _height;
    private readonly byte[] _pixelBuffer;
    private readonly Bitmap _bitmap;

    private readonly Vector3 _cameraTarget;
    private readonly Vector3 _cameraPos;

    public Scene(GL gl, string modelPath, int width, int height)
    {
        _gl = gl;
        _width = width;
        _height = height;
        _model = GltfLoader.Load(gl, modelPath);
        _shader = new Shader(gl, VertexSrc, FragmentSrc);

        _fbo = _gl.GenFramebuffer();
        _colorTex = _gl.GenTexture();
        _depthRbo = _gl.GenRenderbuffer();

        _gl.BindTexture(GLEnum.Texture2D, _colorTex);
        unsafe
        {
            _gl.TexImage2D(GLEnum.Texture2D, 0, (int)GLEnum.Rgba8,
                (uint)width, (uint)height, 0,
                GLEnum.Rgba, GLEnum.UnsignedByte, null);
        }
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Nearest);

        _gl.BindRenderbuffer(GLEnum.Renderbuffer, _depthRbo);
        _gl.RenderbufferStorage(GLEnum.Renderbuffer, GLEnum.DepthComponent24, (uint)width, (uint)height);

        _gl.BindFramebuffer(GLEnum.Framebuffer, _fbo);
        _gl.FramebufferTexture2D(GLEnum.Framebuffer, GLEnum.ColorAttachment0,
            GLEnum.Texture2D, _colorTex, 0);
        _gl.FramebufferRenderbuffer(GLEnum.Framebuffer, GLEnum.DepthAttachment,
            GLEnum.Renderbuffer, _depthRbo);

        var status = _gl.CheckFramebufferStatus(GLEnum.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
            throw new InvalidOperationException("FBO incomplete: " + status);

        _gl.BindFramebuffer(GLEnum.Framebuffer, 0);

        _pixelBuffer = new byte[width * height * 4];
        _bitmap = new Bitmap(width, height, GdiPixelFormat.Format32bppPArgb);

        _cameraTarget = _model.Center;
        float dist = MathF.Max(_model.MaxDim * 2.2f, 2f);
        _cameraPos = _cameraTarget + new Vector3(0, dist * 0.35f, dist);
    }

    public Bitmap Render(float yaw)
    {
        _gl.BindFramebuffer(GLEnum.Framebuffer, _fbo);
        _gl.Viewport(0, 0, (uint)_width, (uint)_height);
        _gl.Enable(GLEnum.DepthTest);
        _gl.Disable(GLEnum.CullFace);
        _gl.ClearColor(0, 0, 0, 0);
        _gl.Clear((uint)(GLEnum.ColorBufferBit | GLEnum.DepthBufferBit));

        _shader.Use();

        var modelMat = Matrix4x4.CreateTranslation(-_model.Center)
            * Matrix4x4.CreateRotationY(yaw)
            * Matrix4x4.CreateTranslation(_model.Center);
        var view = Matrix4x4.CreateLookAt(_cameraPos, _cameraTarget, Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 5f,
            (float)_width / _height,
            0.1f, 100f);
        var mvp = modelMat * view * proj;

        _shader.SetMat4("uMVP", mvp);
        _shader.SetMat3FromMat4("uNormal", modelMat);
        _shader.SetInt("uTex", 0);

        foreach (var mesh in _model.Meshes)
            mesh.Draw();

        unsafe
        {
            fixed (byte* p = _pixelBuffer)
                _gl.ReadPixels(0, 0, (uint)_width, (uint)_height,
                    GLEnum.Bgra, GLEnum.UnsignedByte, p);
        }

        var bd = _bitmap.LockBits(
            new Rectangle(0, 0, _width, _height),
            ImageLockMode.WriteOnly,
            GdiPixelFormat.Format32bppPArgb);
        try
        {
            unsafe
            {
                byte* dst0 = (byte*)bd.Scan0;
                for (int y = 0; y < _height; y++)
                {
                    int srcRowStart = (_height - 1 - y) * _width * 4;
                    byte* dstRow = dst0 + y * bd.Stride;
                    for (int x = 0; x < _width; x++)
                    {
                        int s = srcRowStart + x * 4;
                        byte b = _pixelBuffer[s + 0];
                        byte g = _pixelBuffer[s + 1];
                        byte r = _pixelBuffer[s + 2];
                        byte a = _pixelBuffer[s + 3];
                        dstRow[x * 4 + 0] = (byte)(b * a / 255);
                        dstRow[x * 4 + 1] = (byte)(g * a / 255);
                        dstRow[x * 4 + 2] = (byte)(r * a / 255);
                        dstRow[x * 4 + 3] = a;
                    }
                }
            }
        }
        finally
        {
            _bitmap.UnlockBits(bd);
        }

        _gl.BindFramebuffer(GLEnum.Framebuffer, 0);
        return _bitmap;
    }

    public void Dispose()
    {
        foreach (var m in _model.Meshes)
            m.Dispose();
        _shader.Dispose();
        _gl.DeleteFramebuffer(_fbo);
        _gl.DeleteTexture(_colorTex);
        _gl.DeleteRenderbuffer(_depthRbo);
        _bitmap.Dispose();
    }
}
