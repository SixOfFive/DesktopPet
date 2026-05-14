using System;
using System.Drawing;

namespace Neko;

internal enum PetState
{
    Idle,
    Walk,
    Sleep,
    Chase,
}

internal sealed class Pet
{
    private const float WalkSpeed = 80f;
    private const float ChaseSpeed = 220f;
    private const float NoticeDistance = 90f;
    private const float CatchDistance = 28f;
    private const double PostCatchPauseSeconds = 1.2;
    private const double MinStateSeconds = 1.5;
    private const double MaxStateSeconds = 4.5;
    private const float TurnRatePerSec = 9f;

    private readonly Random _rng = new();
    private readonly Rectangle _bounds;
    private readonly Size _size;

    private PointF _position;
    private PointF _velocity;
    private double _stateTimer;
    private float _targetYaw;

    public PetState State { get; private set; } = PetState.Idle;
    public float Yaw { get; private set; }
    public PointF Position => _position;
    public Size Size => _size;

    public Pet(Rectangle screenBounds, Size petSize)
    {
        _bounds = screenBounds;
        _size = petSize;
        _position = new PointF(
            screenBounds.X + screenBounds.Width / 2f - petSize.Width / 2f,
            screenBounds.Y + screenBounds.Height / 2f - petSize.Height / 2f);
        PickNextRandomState();
    }

    public void Update(double deltaSeconds, Point cursorPos)
    {
        float centerX = _position.X + _size.Width / 2f;
        float centerY = _position.Y + _size.Height / 2f;
        float dx = cursorPos.X - centerX;
        float dy = cursorPos.Y - centerY;
        float dist = MathF.Sqrt(dx * dx + dy * dy);

        if (State == PetState.Chase)
        {
            if (dist <= CatchDistance)
            {
                State = PetState.Idle;
                _velocity = PointF.Empty;
                _stateTimer = PostCatchPauseSeconds;
            }
            else
            {
                float inv = 1f / dist;
                _velocity = new PointF(dx * inv * ChaseSpeed, dy * inv * ChaseSpeed);
            }
        }
        else if (dist > NoticeDistance)
        {
            State = PetState.Chase;
            float inv = dist > 0 ? 1f / dist : 0;
            _velocity = new PointF(dx * inv * ChaseSpeed, dy * inv * ChaseSpeed);
        }
        else
        {
            _stateTimer -= deltaSeconds;
            if (_stateTimer <= 0)
                PickNextRandomState();
        }

        if (State == PetState.Walk || State == PetState.Chase)
        {
            _position.X += _velocity.X * (float)deltaSeconds;
            _position.Y += _velocity.Y * (float)deltaSeconds;
            ClampToBounds();

            if (MathF.Abs(_velocity.X) > 1f || MathF.Abs(_velocity.Y) > 1f)
                _targetYaw = MathF.Atan2(_velocity.X, _velocity.Y);
        }

        UpdateYaw((float)deltaSeconds);
    }

    private void UpdateYaw(float dt)
    {
        float diff = _targetYaw - Yaw;
        while (diff > MathF.PI) diff -= MathF.Tau;
        while (diff < -MathF.PI) diff += MathF.Tau;
        Yaw += diff * MathF.Min(1f, dt * TurnRatePerSec);
    }

    private void PickNextRandomState()
    {
        _stateTimer = MinStateSeconds + _rng.NextDouble() * (MaxStateSeconds - MinStateSeconds);
        var roll = _rng.NextDouble();
        if (roll < 0.6)
        {
            State = PetState.Walk;
            var angle = _rng.NextDouble() * Math.PI * 2;
            _velocity = new PointF(
                (float)(Math.Cos(angle) * WalkSpeed),
                (float)(Math.Sin(angle) * WalkSpeed));
        }
        else if (roll < 0.9)
        {
            State = PetState.Idle;
            _velocity = PointF.Empty;
        }
        else
        {
            State = PetState.Sleep;
            _velocity = PointF.Empty;
        }
    }

    private void ClampToBounds()
    {
        if (_position.X < _bounds.Left)
        {
            _position.X = _bounds.Left;
            _velocity.X = -_velocity.X;
        }
        else if (_position.X + _size.Width > _bounds.Right)
        {
            _position.X = _bounds.Right - _size.Width;
            _velocity.X = -_velocity.X;
        }

        if (_position.Y < _bounds.Top)
        {
            _position.Y = _bounds.Top;
            _velocity.Y = -_velocity.Y;
        }
        else if (_position.Y + _size.Height > _bounds.Bottom)
        {
            _position.Y = _bounds.Bottom - _size.Height;
            _velocity.Y = -_velocity.Y;
        }
    }
}
