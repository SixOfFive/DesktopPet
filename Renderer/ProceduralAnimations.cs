using System.Numerics;

namespace Neko.Renderer;

internal interface IProceduralAnimation
{
    string Name { get; }
    void Evaluate(
        float time,
        Span<Vector3> tBuf,
        Span<Quaternion> rBuf,
        Span<Vector3> sBuf,
        IReadOnlyDictionary<string, int> nameToIdx);
}

internal sealed class FallAnimation : IProceduralAnimation
{
    private const float TumbleSpeed = 4.5f;
    private const float ArmFlailFreq = 8f;
    private const float ArmFlailAmp = 0.7f;

    public string Name => "Fall";

    public void Evaluate(float time, Span<Vector3> tBuf, Span<Quaternion> rBuf, Span<Vector3> sBuf,
        IReadOnlyDictionary<string, int> nameToIdx)
    {
        if (nameToIdx.TryGetValue("Hips", out int hips))
            rBuf[hips] = rBuf[hips] * Quaternion.CreateFromAxisAngle(Vector3.UnitX, time * TumbleSpeed);

        float wave = MathF.Sin(time * ArmFlailFreq);
        if (nameToIdx.TryGetValue("LeftArm", out int la))
            rBuf[la] = rBuf[la] * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 1.2f + wave * ArmFlailAmp);
        if (nameToIdx.TryGetValue("RightArm", out int ra))
            rBuf[ra] = rBuf[ra] * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -1.2f - wave * ArmFlailAmp);

        if (nameToIdx.TryGetValue("LeftUpLeg", out int ll))
            rBuf[ll] = rBuf[ll] * Quaternion.CreateFromAxisAngle(Vector3.UnitX, -0.4f + wave * 0.3f);
        if (nameToIdx.TryGetValue("RightUpLeg", out int rl))
            rBuf[rl] = rBuf[rl] * Quaternion.CreateFromAxisAngle(Vector3.UnitX, -0.4f - wave * 0.3f);
    }
}

internal sealed class FacePlantAnimation : IProceduralAnimation
{
    public string Name => "FacePlant";

    public void Evaluate(float time, Span<Vector3> tBuf, Span<Quaternion> rBuf, Span<Vector3> sBuf,
        IReadOnlyDictionary<string, int> nameToIdx)
    {
        if (nameToIdx.TryGetValue("Hips", out int hips))
            rBuf[hips] = rBuf[hips] * Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2f);
        if (nameToIdx.TryGetValue("LeftArm", out int la))
            rBuf[la] = rBuf[la] * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 1.5f);
        if (nameToIdx.TryGetValue("RightArm", out int ra))
            rBuf[ra] = rBuf[ra] * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -1.5f);
        if (nameToIdx.TryGetValue("LeftUpLeg", out int ll))
            rBuf[ll] = rBuf[ll] * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.3f);
        if (nameToIdx.TryGetValue("RightUpLeg", out int rl))
            rBuf[rl] = rBuf[rl] * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -0.3f);
    }
}

internal sealed class HeadShakeAnimation : IProceduralAnimation
{
    public string Name => "HeadShake";

    public void Evaluate(float time, Span<Vector3> tBuf, Span<Quaternion> rBuf, Span<Vector3> sBuf,
        IReadOnlyDictionary<string, int> nameToIdx)
    {
        if (nameToIdx.TryGetValue("Hips", out int hips))
            rBuf[hips] = rBuf[hips] * Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2f);
        if (nameToIdx.TryGetValue("LeftArm", out int la))
            rBuf[la] = rBuf[la] * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 1.1f);
        if (nameToIdx.TryGetValue("RightArm", out int ra))
            rBuf[ra] = rBuf[ra] * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -1.1f);

        float shake = MathF.Sin(time * 22f) * 0.45f;
        if (nameToIdx.TryGetValue("Head", out int head))
            rBuf[head] = rBuf[head] * Quaternion.CreateFromAxisAngle(Vector3.UnitY, shake);
        if (nameToIdx.TryGetValue("Neck", out int neck))
            rBuf[neck] = rBuf[neck] * Quaternion.CreateFromAxisAngle(Vector3.UnitY, shake * 0.5f);
    }
}

internal sealed class ClimbAnimation : IProceduralAnimation
{
    private const float CycleFreq = 3.5f;

    public string Name => "Climb";

    public void Evaluate(float time, Span<Vector3> tBuf, Span<Quaternion> rBuf, Span<Vector3> sBuf,
        IReadOnlyDictionary<string, int> nameToIdx)
    {
        float t = time * CycleFreq;
        float phase = MathF.Sin(t);
        float opp = MathF.Sin(t + MathF.PI);

        if (nameToIdx.TryGetValue("Spine", out int spine))
            rBuf[spine] = rBuf[spine] * Quaternion.CreateFromAxisAngle(Vector3.UnitX, -0.35f);
        if (nameToIdx.TryGetValue("Chest", out int chest))
            rBuf[chest] = rBuf[chest] * Quaternion.CreateFromAxisAngle(Vector3.UnitX, -0.15f);

        if (nameToIdx.TryGetValue("LeftArm", out int la))
            rBuf[la] = rBuf[la] * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 1.4f + phase * 0.5f);
        if (nameToIdx.TryGetValue("LeftForeArm", out int lf))
            rBuf[lf] = rBuf[lf] * Quaternion.CreateFromAxisAngle(Vector3.UnitY, -0.6f);

        if (nameToIdx.TryGetValue("RightArm", out int ra))
            rBuf[ra] = rBuf[ra] * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -1.4f + opp * 0.5f);
        if (nameToIdx.TryGetValue("RightForeArm", out int rf))
            rBuf[rf] = rBuf[rf] * Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.6f);

        if (nameToIdx.TryGetValue("LeftUpLeg", out int ll))
            rBuf[ll] = rBuf[ll] * Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.5f + opp * 0.6f);
        if (nameToIdx.TryGetValue("RightUpLeg", out int rl))
            rBuf[rl] = rBuf[rl] * Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.5f + phase * 0.6f);
    }
}
