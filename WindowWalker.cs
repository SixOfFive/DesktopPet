using System;
using System.Collections.Generic;
using System.Drawing;

namespace Neko;

internal sealed class WindowWalker : IPetBehavior
{
    private const float BaseWalkSpeed = 60f;
    private const float Gravity = 900f;
    private const float MaxFallSpeed = 900f;
    private const float GroundSnapPx = 6f;
    private const float EdgeDropThresholdPx = 8f;
    private const double WindowRefreshSec = 0.05;
    private const double IdlePauseChancePerSec = 0.15;
    private const double IdleDurationSec = 1.8;
    private const double FacePlantDurationSec = 1.0;
    private const double HeadShakeDurationSec = 0.6;
    private const float FallLandImpactSpeed = 200f;
    private const float ClimbSpeed = 60f;
    private const double ClimbChance = 0.55;
    private const double GripLossChancePerSec = 0.12;
    private const float ClimbWallSearchPx = 24f;

    private static readonly Random Rng = new();

    private readonly Rectangle _screenBounds;
    private readonly Size _size;
    private readonly IntPtr _selfHwnd;
    private readonly float _walkSpeed;

    private PointF _position;
    private float _vx;
    private float _vy;
    private List<Rectangle> _windows = new();
    private double _windowRefreshTimer;
    private double _idleRemaining;
    private double _stateTimer;
    private float _climbTargetTop;
    private float _climbWallX;
    private int _climbAwayDir = 1;

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
        _walkSpeed = BaseWalkSpeed * petSize.Height / 128f;
        _vx = _walkSpeed;
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

        if (State == PetState.FacePlant)
        {
            _stateTimer -= deltaSeconds;
            if (_stateTimer <= 0)
            {
                State = PetState.HeadShake;
                _stateTimer = HeadShakeDurationSec;
            }
            return;
        }
        if (State == PetState.HeadShake)
        {
            _stateTimer -= deltaSeconds;
            if (_stateTimer <= 0) State = PetState.Idle;
            return;
        }
        if (State == PetState.Climb)
        {
            if (Rng.NextDouble() < GripLossChancePerSec * deltaSeconds)
            {
                State = PetState.Fall;
                _vy = 0;
                return;
            }
            _position.Y -= ClimbSpeed * dt;
            if (_position.Y + _size.Height <= _climbTargetTop)
            {
                _position.Y = _climbTargetTop - _size.Height;
                _vx = _walkSpeed * _climbAwayDir;
                Yaw = _climbAwayDir > 0 ? MathF.PI / 2f : -MathF.PI / 2f;
                State = PetState.Walk;
            }
            return;
        }

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
            {
                if (TryStartClimb(newCenterX, groundUnder.Value))
                    return;
                _vx = -_vx;
            }

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
                bool hardLanding = _vy >= FallLandImpactSpeed;
                _position.Y = groundUnder.Value - _size.Height;
                _vy = 0;
                if (hardLanding)
                {
                    State = PetState.FacePlant;
                    _stateTimer = FacePlantDurationSec;
                }
            }
        }
    }

    private bool TryStartClimb(float petCenterX, float currentGroundY)
    {
        if (Rng.NextDouble() > ClimbChance) return false;

        Rectangle? best = null;
        bool bestIsLeftEdge = false;
        float bestDx = float.MaxValue;
        for (int i = 0; i < _windows.Count; i++)
        {
            var w = _windows[i];
            if (w.Top >= currentGroundY - 4f) continue;
            float minViableTop = _screenBounds.Top + _size.Height;
            if (w.Top < minViableTop) continue;

            float distLeft = MathF.Abs(w.Left - petCenterX);
            float distRight = MathF.Abs(w.Right - petCenterX);
            float dx = MathF.Min(distLeft, distRight);
            if (dx > ClimbWallSearchPx) continue;

            float landingX = distLeft < distRight ? w.Left - _size.Width / 2f : w.Right + _size.Width / 2f;
            var topRect = new Rectangle(
                (int)(landingX - _size.Width / 2f),
                w.Top - _size.Height,
                _size.Width,
                _size.Height);
            bool obscured = false;
            for (int j = 0; j < i; j++)
            {
                if (_windows[j].IntersectsWith(topRect))
                {
                    obscured = true;
                    break;
                }
            }
            if (obscured) continue;

            if (dx < bestDx)
            {
                bestDx = dx;
                best = w;
                bestIsLeftEdge = distLeft < distRight;
            }
        }

        if (!best.HasValue) return false;

        _climbTargetTop = best.Value.Top;
        if (bestIsLeftEdge)
        {
            _climbWallX = best.Value.Left;
            _position.X = best.Value.Left - _size.Width;
            _climbAwayDir = -1;
        }
        else
        {
            _climbWallX = best.Value.Right;
            _position.X = best.Value.Right;
            _climbAwayDir = 1;
        }
        State = PetState.Climb;
        return true;
    }

    private float? FindGroundUnder(float x, float feetY)
    {
        float minViableTop = _screenBounds.Top + _size.Height;
        float? best = null;
        int halfW = _size.Width / 2;
        for (int i = 0; i < _windows.Count; i++)
        {
            var w = _windows[i];
            if (x < w.Left || x > w.Right) continue;
            float top = w.Top;
            if (top < minViableTop) continue;
            if (top < feetY - 1f) continue;

            var bodyRect = new Rectangle(
                (int)(x - halfW),
                w.Top - _size.Height,
                _size.Width,
                _size.Height);

            bool obscured = false;
            for (int j = 0; j < i; j++)
            {
                if (_windows[j].IntersectsWith(bodyRect))
                {
                    obscured = true;
                    break;
                }
            }
            if (obscured) continue;

            if (best == null || top < best.Value) best = top;
        }
        float screenBottom = _screenBounds.Bottom;
        if (screenBottom >= feetY - 1f && (best == null || screenBottom < best.Value))
            best = screenBottom;
        return best;
    }
}
