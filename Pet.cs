using System;
using System.Drawing;

namespace Neko;

internal enum PetState
{
    Idle,
    Walk,
    Sleep,
    Chase,
    Eat,
    Dance,
    WalkToSleep,
    Fall,
    Climb,
    FacePlant,
    HeadShake,
}

internal sealed class Pet : IPetBehavior
{
    private const float BaseWalkSpeed = 80f;
    private const float BaseChaseSpeed = 220f;
    private const float NoticeDistance = 90f;
    private const float CatchDistance = 28f;
    private const double EatDurationSec = 1.5;
    private const double DanceDurationSec = 2.0;
    private const double EatCooldownSec = 60.0;
    private const double CursorIdleSecForSleep = 60.0;
    private const double CursorMovingGraceSec = 0.3;
    private const float SleepArrivalDistance = 24f;
    private const double SleepTwitchMinIntervalSec = 4.0;
    private const double SleepTwitchMaxIntervalSec = 9.0;
    private const double SleepTwitchDurationSec = 0.45;
    private const double MinStateSeconds = 1.5;
    private const double MaxStateSeconds = 4.5;
    private const float TurnRatePerSec = 9f;

    private readonly Random _rng = new();
    private readonly Rectangle _bounds;
    private readonly Size _size;
    private readonly float _walkSpeed;
    private readonly float _chaseSpeed;

    private PointF _position;
    private PointF _velocity;
    private double _stateTimer;
    private float _targetYaw;
    private Point _lastCursorPos = new(int.MinValue, int.MinValue);
    private double _cursorStillSec;
    private double _sleepTwitchTimer;
    private double _eatCooldownTimer;

    public PetState State { get; private set; } = PetState.Idle;
    public bool SleepTwitch { get; private set; }
    public float Yaw { get; private set; }
    public PointF Position => _position;
    public Size Size => _size;

    public Pet(Rectangle screenBounds, Size petSize)
    {
        _bounds = screenBounds;
        _size = petSize;
        _walkSpeed = BaseWalkSpeed * petSize.Height / 128f;
        _chaseSpeed = BaseChaseSpeed * petSize.Height / 128f;
        _position = new PointF(
            screenBounds.X + screenBounds.Width / 2f - petSize.Width / 2f,
            screenBounds.Y + screenBounds.Height / 2f - petSize.Height / 2f);
        ScheduleNextRandomState();
    }

    public void Update(double deltaSeconds, Point cursorPos)
    {
        bool cursorMoved = cursorPos != _lastCursorPos;
        if (cursorMoved) _cursorStillSec = 0;
        else _cursorStillSec += deltaSeconds;
        _lastCursorPos = cursorPos;

        if (_eatCooldownTimer > 0) _eatCooldownTimer -= deltaSeconds;

        bool cursorIsMoving = _cursorStillSec < CursorMovingGraceSec;

        float centerX = _position.X + _size.Width / 2f;
        float centerY = _position.Y + _size.Height / 2f;
        float dx = cursorPos.X - centerX;
        float dy = cursorPos.Y - centerY;
        float dist = MathF.Sqrt(dx * dx + dy * dy);

        switch (State)
        {
            case PetState.Eat:
                _stateTimer -= deltaSeconds;
                _velocity = PointF.Empty;
                if (_stateTimer <= 0)
                {
                    State = PetState.Dance;
                    _stateTimer = DanceDurationSec;
                }
                break;

            case PetState.Dance:
                _stateTimer -= deltaSeconds;
                _velocity = PointF.Empty;
                if (_stateTimer <= 0)
                    ScheduleNextRandomState();
                break;

            case PetState.Sleep:
                _velocity = PointF.Empty;
                if (cursorMoved)
                {
                    State = PetState.Idle;
                    _stateTimer = MinStateSeconds;
                    SleepTwitch = false;
                }
                else
                {
                    _sleepTwitchTimer -= deltaSeconds;
                    if (_sleepTwitchTimer <= 0)
                    {
                        if (SleepTwitch)
                        {
                            SleepTwitch = false;
                            _sleepTwitchTimer = SleepTwitchMinIntervalSec
                                + _rng.NextDouble() * (SleepTwitchMaxIntervalSec - SleepTwitchMinIntervalSec);
                        }
                        else
                        {
                            SleepTwitch = true;
                            _sleepTwitchTimer = SleepTwitchDurationSec;
                        }
                    }
                }
                break;

            case PetState.Chase:
                if (!cursorIsMoving)
                {
                    ScheduleNextRandomState();
                }
                else if (dist <= CatchDistance)
                {
                    _velocity = PointF.Empty;
                    if (_eatCooldownTimer <= 0)
                    {
                        State = PetState.Eat;
                        _stateTimer = EatDurationSec;
                        _eatCooldownTimer = EatCooldownSec;
                    }
                    else
                    {
                        ScheduleNextRandomState();
                    }
                }
                else
                {
                    float inv = 1f / dist;
                    _velocity = new PointF(dx * inv * _chaseSpeed, dy * inv * _chaseSpeed);
                }
                break;

            case PetState.WalkToSleep:
                if (cursorIsMoving)
                {
                    if (dist > NoticeDistance)
                    {
                        State = PetState.Chase;
                        float invc = dist > 0 ? 1f / dist : 0;
                        _velocity = new PointF(dx * invc * _chaseSpeed, dy * invc * _chaseSpeed);
                    }
                    else
                    {
                        ScheduleNextRandomState();
                    }
                }
                else if (dist <= SleepArrivalDistance)
                {
                    State = PetState.Sleep;
                    _velocity = PointF.Empty;
                    SleepTwitch = false;
                    _sleepTwitchTimer = SleepTwitchMinIntervalSec
                        + _rng.NextDouble() * (SleepTwitchMaxIntervalSec - SleepTwitchMinIntervalSec);
                }
                else
                {
                    float inv = 1f / dist;
                    _velocity = new PointF(dx * inv * _walkSpeed, dy * inv * _walkSpeed);
                }
                break;

            default:
                if (cursorIsMoving && dist > NoticeDistance)
                {
                    State = PetState.Chase;
                    float inv = dist > 0 ? 1f / dist : 0;
                    _velocity = new PointF(dx * inv * _chaseSpeed, dy * inv * _chaseSpeed);
                }
                else if (_cursorStillSec > CursorIdleSecForSleep)
                {
                    State = PetState.WalkToSleep;
                }
                else
                {
                    _stateTimer -= deltaSeconds;
                    if (_stateTimer <= 0)
                        ScheduleNextRandomState();
                }
                break;
        }

        if (State == PetState.Walk || State == PetState.Chase || State == PetState.WalkToSleep)
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

    private void ScheduleNextRandomState()
    {
        _stateTimer = MinStateSeconds + _rng.NextDouble() * (MaxStateSeconds - MinStateSeconds);
        if (_rng.NextDouble() < 0.65)
        {
            State = PetState.Walk;
            var angle = _rng.NextDouble() * Math.PI * 2;
            _velocity = new PointF(
                (float)(Math.Cos(angle) * _walkSpeed),
                (float)(Math.Sin(angle) * _walkSpeed));
        }
        else
        {
            State = PetState.Idle;
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
