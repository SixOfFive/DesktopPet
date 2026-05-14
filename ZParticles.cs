using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Neko;

internal sealed class ZParticles
{
    private struct Z
    {
        public float StartX, StartY;
        public float DriftXAmp;
        public float SwayFreq;
        public float Age;
        public float Lifetime;
    }

    private const float SpawnIntervalSec = 1.5f;
    private const float LifetimeSec = 3.2f;
    private const float DriftUpPxPerSec = 28f;
    private const float StartFontPx = 18f;
    private const float EndFontPx = 30f;

    private readonly List<Z> _particles = new();
    private readonly Random _rng = new();
    private float _spawnTimer;

    public void Update(double deltaSeconds, bool spawning, int frameWidth)
    {
        float dt = (float)deltaSeconds;

        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var z = _particles[i];
            z.Age += dt;
            if (z.Age >= z.Lifetime) _particles.RemoveAt(i);
            else _particles[i] = z;
        }

        if (!spawning)
        {
            _spawnTimer = 0;
            return;
        }

        _spawnTimer -= dt;
        if (_spawnTimer <= 0)
        {
            _spawnTimer = SpawnIntervalSec;
            _particles.Add(new Z
            {
                StartX = frameWidth * 0.5f + (float)(_rng.NextDouble() * 20 - 10),
                StartY = frameWidth * 0.45f,
                DriftXAmp = 5f + (float)(_rng.NextDouble() * 6),
                SwayFreq = 1.6f + (float)(_rng.NextDouble() * 0.8),
                Age = 0,
                Lifetime = LifetimeSec,
            });
        }
    }

    public void Draw(Bitmap bmp)
    {
        if (_particles.Count == 0) return;

        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;

        foreach (var z in _particles)
        {
            float t = z.Age / z.Lifetime;
            float alphaCurve = t < 0.15f ? t / 0.15f : 1f - (t - 0.15f) / 0.85f;
            int alpha = (int)(255 * Math.Clamp(alphaCurve, 0f, 1f));
            if (alpha <= 0) continue;

            float x = z.StartX + MathF.Sin(z.Age * z.SwayFreq) * z.DriftXAmp;
            float y = z.StartY - DriftUpPxPerSec * z.Age;
            float size = StartFontPx + (EndFontPx - StartFontPx) * t;

            using var font = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(Color.FromArgb(alpha, 130, 170, 240));
            g.DrawString("Z", font, brush, x, y);
        }
    }
}
