using System.Numerics;
using System.Runtime.InteropServices;
using SharpGLTF.Animations;
using Silk.NET.OpenGL;

namespace Neko.Renderer;

[StructLayout(LayoutKind.Sequential)]
internal struct SkinnedVertex
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector2 Uv;
    public Vector4 Color;
    public int J0, J1, J2, J3;
    public float W0, W1, W2, W3;
    public const int SizeBytes = 12 + 12 + 8 + 16 + 16 + 16;
}

internal sealed class SkinnedAnimation
{
    public required string Name { get; init; }
    public required float Duration { get; init; }
    public required Dictionary<int, ICurveSampler<Vector3>> Translation { get; init; }
    public required Dictionary<int, ICurveSampler<Quaternion>> Rotation { get; init; }
    public required Dictionary<int, ICurveSampler<Vector3>> Scale { get; init; }
}

internal sealed class SkinnedModel : IDisposable
{
    private readonly GL _gl;

    public required NodeData[] Nodes { get; init; }
    public required int[] JointNodeIndices { get; init; }
    public required Matrix4x4[] InverseBindMatrices { get; init; }
    public required Matrix4x4 MeshBindShape { get; init; }
    public required uint Vao { get; init; }
    public required uint Vbo { get; init; }
    public required uint Ebo { get; init; }
    public required uint IndexCount { get; init; }
    public required Texture? BaseTexture { get; init; }
    public required SkinnedAnimation[] Animations { get; init; }
    public required Dictionary<string, int> AnimationByName { get; init; }
    public required Vector3 Min { get; init; }
    public required Vector3 Max { get; init; }

    public Matrix4x4[] JointMatrices { get; }

    private readonly Vector3[] _tBuf;
    private readonly Quaternion[] _rBuf;
    private readonly Vector3[] _sBuf;
    private readonly Matrix4x4[] _worldMatrices;

    public Vector3 Center => (Min + Max) * 0.5f;
    public float MaxDim => MathF.Max(Max.X - Min.X, MathF.Max(Max.Y - Min.Y, Max.Z - Min.Z));

    public SkinnedModel(GL gl, int nodeCount, int jointCount)
    {
        _gl = gl;
        _tBuf = new Vector3[nodeCount];
        _rBuf = new Quaternion[nodeCount];
        _sBuf = new Vector3[nodeCount];
        _worldMatrices = new Matrix4x4[nodeCount];
        JointMatrices = new Matrix4x4[jointCount];
    }

    public int FindAnimationIndex(string name, int fallback = 0)
    {
        return AnimationByName.TryGetValue(name, out int idx) ? idx : fallback;
    }

    public void Pose(int animIndex, float time)
    {
        for (int i = 0; i < Nodes.Length; i++)
        {
            _tBuf[i] = Nodes[i].RestT;
            _rBuf[i] = Nodes[i].RestR;
            _sBuf[i] = Nodes[i].RestS;
        }

        if (animIndex >= 0 && animIndex < Animations.Length)
        {
            var anim = Animations[animIndex];
            float t = anim.Duration > 0 ? time - MathF.Floor(time / anim.Duration) * anim.Duration : 0;
            foreach (var (idx, s) in anim.Translation) _tBuf[idx] = s.GetPoint(t);
            foreach (var (idx, s) in anim.Rotation)    _rBuf[idx] = s.GetPoint(t);
            foreach (var (idx, s) in anim.Scale)       _sBuf[idx] = s.GetPoint(t);
        }

        for (int i = 0; i < Nodes.Length; i++)
        {
            var local = Matrix4x4.CreateScale(_sBuf[i])
                * Matrix4x4.CreateFromQuaternion(_rBuf[i])
                * Matrix4x4.CreateTranslation(_tBuf[i]);
            int parent = Nodes[i].ParentIndex;
            _worldMatrices[i] = parent < 0 ? local : local * _worldMatrices[parent];
        }

        for (int j = 0; j < JointNodeIndices.Length; j++)
        {
            int nodeIdx = JointNodeIndices[j];
            JointMatrices[j] = InverseBindMatrices[j] * _worldMatrices[nodeIdx];
        }
    }

    public void Draw()
    {
        BaseTexture?.Bind(0);
        _gl.BindVertexArray(Vao);
        unsafe { _gl.DrawElements(GLEnum.Triangles, IndexCount, GLEnum.UnsignedInt, (void*)0); }
    }

    public void Dispose()
    {
        _gl.DeleteVertexArray(Vao);
        _gl.DeleteBuffer(Vbo);
        _gl.DeleteBuffer(Ebo);
        BaseTexture?.Dispose();
    }
}
