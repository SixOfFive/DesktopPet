using System;
using System.Collections.Generic;
using System.Drawing;

namespace Neko;

internal sealed class WindowWalker : IPetBehavior
{
    private const float WalkSpeed = 60f;
    private const float Gravity = 900f;
    private const float MaxFallSpeed = 900f;
    private const float GroundSnapPx = 6f;
    private const float EdgeDropThresholdPx = 8f;
    private const double WindowRefreshSec = 0.25;
    private const double IdlePauseChancePerSec = 0.15;
    private const double IdleDurationSec = 1.8;

    private static readonly Random Rng = new();

    private readonly Rectangle _screenBounds;
    private readonly Size _size;
    private readonly IntPtr _selfHwnd;

    private PointF _position;
    private float _vx = WalkSpeed;
    private float _vy;
    private List<Rectangle> _windows = new();
    private double _windowRefreshTimer;
    private double _idleRemaining;

    public PetState State { get; private set; } = PetState.Idle;
    public float Yaw { get; private set; }
    public bool SleepTwitch => false;
    public PointF Position => _position;
    public Size Size => _size;

    public WindowWalker(Rectangle screenBounds, Size petSize, IntPtr selfHwnd)
    {
        _screenBounds = screenBounds;
        _size = petSize;
        _selfHwnd = selfHwnd;
        _position = new PointF(
            screenBounds.X + screenBounds.Width / 2f - petSize.Width / 2f,
            screenBounds.Y);
        Yaw = MathF.PI / 2f;
    }

    public void Update(double deltaSeconds, Point cursorPos)
    {
        _windowRefreshTimer -= deltaSeconds;
        if (_windowRefreshTimer <= 0)
        {
            _windowRefreshTimer = WindowRefreshSec;
            _windows = WindowEnumerator.GetVisibleWindowRects(_selfHwnd);
        }

        float dt = (float)deltaSeconds;
        float feetY = _position.Y + _size.Height;
        float centerX = _position.X + _size.Width / 2f;

        float? groundUnder = FindGroundUnder(centerX, feetY);

        bool onGround = groundUnder.HasValue
            && groundUnder.Value >= feetY - 1f
            && groundUnder.Value <= feetY + GroundSnapPx;

        if (onGround)
        {
            _position.Y = groundUnder!.Value - _size.Height;
            _vy = 0;

            if (_idleRemaining > 0)
            {
                _idleRemaining -= deltaSeconds;
                State = PetState.Idle;
                return;
            }

            if (Rng.NextDouble() < IdlePauseChancePerSec * deltaSeconds)
            {
                _idleRemaining = IdleDurationSec * (0.5 + Rng.NextDouble());
                State = PetState.Idle;
                return;
            }

            _position.X += _vx * dt;
            State = PetState.Walk;
            Yaw = _vx > 0 ? MathF.PI / 2f : -MathF.PI / 2f;

            float newCenterX = _position.X + _size.Width / 2f;
            float? newGround = FindGroundUnder(newCenterX, _position.Y + _size.Height);
            bool walkedOffEdge = !newGround.HasValue
                || newGround.Value > groundUnder.Value + EdgeDropThresholdPx;
            if (walkedOffEdge)
                _vx = -_vx;

            if (_position.X < _screenBounds.Left)
            {
                _position.X = _screenBounds.Left;
                _vx = MathF.Abs(_vx);
            }
            else if (_position.X + _size.Width > _screenBounds.Right)
            {
                _position.X = _screenBounds.Right - _size.Width;
                _vx = -MathF.Abs(_vx);
            }
        }
        else
        {
            _vy = MathF.Min(_vy + Gravity * dt, MaxFallSpeed);
            _position.Y += _vy * dt;
            State = PetState.Fall;

            if (groundUnder.HasValue && _position.Y + _size.Height >= groundUnder.Value)
            {
                _position.Y = groundUnder.Value - _size.Height;
                _vy = 0;
            }
        }
    }

    private float? FindGroundUnder(float x, float feetY)
    {
        float minViableTop = _screenBounds.Top + _size.Height;
        float? best = null;
        foreach (var w in _windows)
        {
            if (x < w.Left || x > w.Right) continue;
            float top = w.Top;
            if (top < minViableTop) continue;
            if (top < feetY - 1f) continue;
            if (best == null || top < best.Value) best = top;
        }
        float screenBottom = _screenBounds.Bottom;
        if (screenBottom >= feetY - 1f && (best == null || screenBottom < best.Value))
            best = screenBottom;
        return best;
    }
}
