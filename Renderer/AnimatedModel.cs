using System.Numerics;
using SharpGLTF.Animations;

namespace Neko.Renderer;

internal struct NodeData
{
    public int ParentIndex;
    public string Name;
    public Vector3 RestT;
    public Quaternion RestR;
    public Vector3 RestS;
}

internal sealed class AnimationData
{
    public required string Name { get; init; }
    public required float Duration { get; init; }
    public required Dictionary<int, ICurveSampler<Vector3>> Translation { get; init; }
    public required Dictionary<int, ICurveSampler<Quaternion>> Rotation { get; init; }
    public required Dictionary<int, ICurveSampler<Vector3>> Scale { get; init; }
}

internal sealed class AnimatedModel : IDisposable
{
    public required NodeData[] Nodes { get; init; }
    public required Dictionary<int, List<Mesh>> MeshesByNodeIndex { get; init; }
    public required AnimationData[] Animations { get; init; }
    public required Dictionary<string, int> AnimationByName { get; init; }
    public required Vector3 Min { get; init; }
    public required Vector3 Max { get; init; }
    public Vector3 Center => (Min + Max) * 0.5f;
    public float MaxDim => MathF.Max(Max.X - Min.X, MathF.Max(Max.Y - Min.Y, Max.Z - Min.Z));

    public Matrix4x4[] WorldMatrices { get; }

    private readonly Vector3[] _tBuf;
    private readonly Quaternion[] _rBuf;
    private readonly Vector3[] _sBuf;

    public AnimatedModel(int nodeCount)
    {
        WorldMatrices = new Matrix4x4[nodeCount];
        _tBuf = new Vector3[nodeCount];
        _rBuf = new Quaternion[nodeCount];
        _sBuf = new Vector3[nodeCount];
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
            foreach (var (idx, sampler) in anim.Translation) _tBuf[idx] = sampler.GetPoint(t);
            foreach (var (idx, sampler) in anim.Rotation)    _rBuf[idx] = sampler.GetPoint(t);
            foreach (var (idx, sampler) in anim.Scale)       _sBuf[idx] = sampler.GetPoint(t);
        }

        for (int i = 0; i < Nodes.Length; i++)
        {
            var local = Matrix4x4.CreateScale(_sBuf[i])
                * Matrix4x4.CreateFromQuaternion(_rBuf[i])
                * Matrix4x4.CreateTranslation(_tBuf[i]);
            int parent = Nodes[i].ParentIndex;
            WorldMatrices[i] = parent < 0 ? local : local * WorldMatrices[parent];
        }
    }

    public void Dispose()
    {
        foreach (var list in MeshesByNodeIndex.Values)
            foreach (var m in list) m.Dispose();
    }
}
