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

    private const string SkinnedVertexSrc = @"#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec3 aNormal;
layout(location=2) in vec2 aUv;
layout(location=3) in vec4 aColor;
layout(location=4) in ivec4 aJoints;
layout(location=5) in vec4 aWeights;
uniform mat4 uMVP;
uniform mat3 uNormal;
uniform mat4 uJoints[64];
out vec3 vNormal;
out vec2 vUv;
out vec4 vColor;
void main() {
    mat4 skin = uJoints[aJoints.x] * aWeights.x
              + uJoints[aJoints.y] * aWeights.y
              + uJoints[aJoints.z] * aWeights.z
              + uJoints[aJoints.w] * aWeights.w;
    vec4 pos = skin * vec4(aPos, 1.0);
    vec3 nrm = mat3(skin) * aNormal;
    gl_Position = uMVP * pos;
    vNormal = normalize(uNormal * nrm);
    vUv = aUv;
    vColor = aColor;
}
";

    private readonly GL _gl;
    private AnimatedModel? _model;
    private SkinnedModel? _skinnedModel;
    private readonly Shader _shader;
    private readonly Shader _skinnedShader;
    private uint _fbo;
    private uint _colorTex;
    private uint _depthRbo;
    private int _width;
    private int _height;
    private byte[] _pixelBuffer;
    private Bitmap _bitmap;

    private Vector3 _cameraTarget;
    private Vector3 _cameraPos;

    private int _idleAnim;
    private int _walkAnim;
    private int _runAnim;
    private int _sleepAnim;
    private int _eatAnim;
    private int _danceAnim;

    private float _animTime;
    private PetState _lastState = (PetState)(-1);
    private bool _lastSleepTwitch;

    public Scene(GL gl, string modelPath, int width, int height, string? textureOverridePath = null)
    {
        _gl = gl;
        _width = width;
        _height = height;
        _shader = new Shader(gl, VertexSrc, FragmentSrc);
        _skinnedShader = new Shader(gl, SkinnedVertexSrc, FragmentSrc);
        ApplyModel(GltfLoader.Load(gl, modelPath, textureOverridePath));

        _pixelBuffer = Array.Empty<byte>();
        _bitmap = new Bitmap(1, 1, GdiPixelFormat.Format32bppPArgb);
        AllocateRenderTarget(width, height);
    }

    public void Resize(int width, int height)
    {
        if (width == _width && height == _height) return;
        AllocateRenderTarget(width, height);
    }

    private void AllocateRenderTarget(int width, int height)
    {
        if (_fbo != 0) _gl.DeleteFramebuffer(_fbo);
        if (_colorTex != 0) _gl.DeleteTexture(_colorTex);
        if (_depthRbo != 0) _gl.DeleteRenderbuffer(_depthRbo);
        _bitmap.Dispose();

        _width = width;
        _height = height;

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
    }

    public void LoadModel(string path, string? textureOverridePath = null)
    {
        var next = GltfLoader.Load(_gl, path, textureOverridePath);
        var prev = _model;
        var prevSkin = _skinnedModel;
        _skinnedModel = null;
        ApplyModel(next);
        prev?.Dispose();
        prevSkin?.Dispose();
    }

    public void LoadSkinnedModel(string meshGlb, IEnumerable<string> animGlbs, string? texturePath)
    {
        var next = SkinnedLoader.Load(_gl, meshGlb, animGlbs, texturePath);
        var prev = _model;
        var prevSkin = _skinnedModel;
        _model = null;
        _skinnedModel = next;
        FrameCamera(next.Center, next.MaxDim, next.Min.Y);
        _animTime = 0;
        _lastState = (PetState)(-1);
        prev?.Dispose();
        prevSkin?.Dispose();
    }

    private void FrameCamera(Vector3 center, float maxDim, float minY)
    {
        float dist = MathF.Max(maxDim * 2.2f, 2f);
        float halfExtent = dist * MathF.Tan(MathF.PI / 10f);
        _cameraTarget = new Vector3(center.X, minY + halfExtent, center.Z);
        _cameraPos = _cameraTarget + new Vector3(0, dist * 0.35f, dist);
    }

    private void ApplyModel(AnimatedModel model)
    {
        _model = model;
        _skinnedModel = null;
        _idleAnim = _model.FindAnimationIndex("idle");
        _walkAnim = _model.FindAnimationIndex("walk", _idleAnim);
        _runAnim = _model.FindAnimationIndex("run", _walkAnim);
        _sleepAnim = _model.FindAnimationIndex("idle", _idleAnim);
        _eatAnim = _model.FindAnimationIndex("eat", _idleAnim);
        _danceAnim = _model.FindAnimationIndex("dance", _idleAnim);

        FrameCamera(_model.Center, _model.MaxDim, _model.Min.Y);
        _animTime = 0;
        _lastState = (PetState)(-1);
    }

    public Bitmap Render(float yaw, PetState state, bool sleepTwitch, float deltaSeconds)
    {
        if (state != _lastState || sleepTwitch != _lastSleepTwitch)
        {
            _animTime = 0;
            _lastState = state;
            _lastSleepTwitch = sleepTwitch;
        }

        float timeScale = state == PetState.Sleep && !sleepTwitch ? 0.3f : 1f;
        _animTime += deltaSeconds * timeScale;

        _gl.BindFramebuffer(GLEnum.Framebuffer, _fbo);
        _gl.Viewport(0, 0, (uint)_width, (uint)_height);
        _gl.Enable(GLEnum.DepthTest);
        _gl.Disable(GLEnum.CullFace);
        _gl.ClearColor(0, 0, 0, 0);
        _gl.Clear((uint)(GLEnum.ColorBufferBit | GLEnum.DepthBufferBit));

        var center = _model?.Center ?? _skinnedModel!.Center;
        var rootRotation = state == PetState.Sleep
            ? Matrix4x4.CreateRotationZ(MathF.PI / 2f)
            : Matrix4x4.CreateRotationY(yaw);
        var rootTransform = Matrix4x4.CreateTranslation(-center)
            * rootRotation
            * Matrix4x4.CreateTranslation(center);
        var view = Matrix4x4.CreateLookAt(_cameraPos, _cameraTarget, Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 5f,
            (float)_width / _height,
            0.1f, 100f);
        var viewProj = view * proj;

        if (_model != null)
        {
            int animIdx = (state, sleepTwitch) switch
            {
                (PetState.Sleep, true) => _walkAnim,
                (PetState.Walk, _) => _walkAnim,
                (PetState.WalkToSleep, _) => _walkAnim,
                (PetState.ReturnBall, _) => _walkAnim,
                (PetState.FetchBall, _) => _runAnim,
                (PetState.Chase, _) => _runAnim,
                (PetState.Sleep, _) => _sleepAnim,
                (PetState.Eat, _) => _eatAnim,
                (PetState.Dance, _) => _danceAnim,
                _ => _idleAnim,
            };
            _model.Pose(animIdx, _animTime);

            _shader.Use();
            _shader.SetInt("uTex", 0);
            foreach (var (nodeIdx, meshList) in _model.MeshesByNodeIndex)
            {
                var modelMat = _model.WorldMatrices[nodeIdx] * rootTransform;
                var mvp = modelMat * viewProj;
                _shader.SetMat4("uMVP", mvp);
                _shader.SetMat3FromMat4("uNormal", modelMat);
                foreach (var mesh in meshList)
                    mesh.Draw();
            }
        }
        else if (_skinnedModel != null)
        {
            int idleIdx = _skinnedModel.FindAnimationIndex("Idle");
            int animIdx = state switch
            {
                PetState.Walk => _skinnedModel.FindAnimationIndex("Run", idleIdx),
                PetState.WalkToSleep => _skinnedModel.FindAnimationIndex("Run", idleIdx),
                PetState.FetchBall => _skinnedModel.FindAnimationIndex("Run", idleIdx),
                PetState.ReturnBall => _skinnedModel.FindAnimationIndex("Run", idleIdx),
                PetState.Chase => _skinnedModel.FindAnimationIndex("Run", idleIdx),
                PetState.Fall => _skinnedModel.FindAnimationIndex("Fall", idleIdx),
                PetState.Climb => _skinnedModel.FindAnimationIndex("Climb", idleIdx),
                PetState.FacePlant => _skinnedModel.FindAnimationIndex("FacePlant", idleIdx),
                PetState.HeadShake => _skinnedModel.FindAnimationIndex("HeadShake", idleIdx),
                PetState.Eat or PetState.Dance => _skinnedModel.FindAnimationIndex("Jump", idleIdx),
                _ => idleIdx,
            };
            _skinnedModel.Pose(animIdx, _animTime);

            _skinnedShader.Use();
            _skinnedShader.SetInt("uTex", 0);
            var mvp = rootTransform * viewProj;
            _skinnedShader.SetMat4("uMVP", mvp);
            _skinnedShader.SetMat3FromMat4("uNormal", rootTransform);
            _skinnedShader.SetMat4Array("uJoints", _skinnedModel.JointMatrices);
            _skinnedModel.Draw();
        }

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
        _model?.Dispose();
        _skinnedModel?.Dispose();
        _shader.Dispose();
        _skinnedShader.Dispose();
        _gl.DeleteFramebuffer(_fbo);
        _gl.DeleteTexture(_colorTex);
        _gl.DeleteRenderbuffer(_depthRbo);
        _bitmap.Dispose();
    }
}
