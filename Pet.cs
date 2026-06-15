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
    FetchBall,
    ReturnBall,
}

internal enum IdleAction
{
    Wander,
    Patrol,
    Pace,
    Circle,
    FigureEight,
    Zigzag,
    Sprint,
    Sneak,
    PerimeterWalk,
    CornerVisit,
    Tiptoe,
    Lap,
    SpinInPlace,
    ReverseSpin,
    LookAround,
    HeadShakeQuirk,
    StareAtCorner,
    Sit,
    Stretch,
    Sneeze,
    Groom,
    Yawn,
    Bow,
    Pounce,
    Spook,
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
    private const float TurnRatePerSec = 9f;
    private const float WaypointArriveRadius = 18f;

    private readonly Random _rng = new();
    private readonly Rectangle _bounds;
    private readonly Size _size;
    private readonly float _walkSpeed;
    private readonly float _chaseSpeed;
    private readonly float _ballCatchDistance;

    private PointF _position;
    private PointF _velocity;
    private double _stateTimer; // used by Eat/Dance
    private float _targetYaw;
    private Point _lastCursorPos = new(int.MinValue, int.MinValue);
    private double _cursorStillSec;
    private double _sleepTwitchTimer;
    private double _eatCooldownTimer;
    private BallForm? _ball;

    // Idle action state machine
    private IdleAction _action;
    private int _actionPhase;
    private double _actionPhaseTimer;
    private PointF _actionAnchor;
    private PointF[] _actionWaypoints = Array.Empty<PointF>();
    private int _actionWaypointIdx;
    private float _actionAngle;
    private float _actionAngularSpeed;
    private float _actionRadius;
    private float _actionYawOrigin;
    private double _actionElapsed;
    private bool _eatChainsToDance = true;

    public void SetBall(BallForm? ball) => _ball = ball;

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
        _ballCatchDistance = petSize.Height / 2f + BallForm.BallSize / 2f + 8f;
        _position = new PointF(
            screenBounds.X + screenBounds.Width / 2f - petSize.Width / 2f,
            screenBounds.Y + screenBounds.Height / 2f - petSize.Height / 2f);
        StartNewIdleAction();
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
            case PetState.FetchBall:
                if (_ball == null || !_ball.IsAvailable)
                {
                    StartNewIdleAction();
                    break;
                }
                {
                    var bc = _ball.Center;
                    float bdx = bc.X - centerX;
                    float bdy = bc.Y - centerY;
                    float bdist = MathF.Sqrt(bdx * bdx + bdy * bdy);
                    if (bdist < _ballCatchDistance)
                    {
                        _ball.GrabByPet();
                        State = PetState.ReturnBall;
                        _velocity = PointF.Empty;
                    }
                    else
                    {
                        float inv = 1f / bdist;
                        _velocity = new PointF(bdx * inv * _chaseSpeed, bdy * inv * _chaseSpeed);
                    }
                }
                break;

            case PetState.ReturnBall:
                if (_ball == null || _ball.State != BallState.CarriedByPet)
                {
                    _velocity = PointF.Empty;
                    StartNewIdleAction();
                    break;
                }
                if (dist < CatchDistance)
                {
                    _ball.DropAt(new PointF(centerX - BallForm.BallSize / 2f, centerY - BallForm.BallSize / 2f));
                    _velocity = PointF.Empty;
                    StartNewIdleAction();
                }
                else
                {
                    float inv = 1f / dist;
                    _velocity = new PointF(dx * inv * _chaseSpeed, dy * inv * _chaseSpeed);
                }
                break;

            case PetState.Eat:
                _stateTimer -= deltaSeconds;
                _velocity = PointF.Empty;
                if (_stateTimer <= 0)
                {
                    if (_eatChainsToDance)
                    {
                        State = PetState.Dance;
                        _stateTimer = DanceDurationSec;
                    }
                    else
                    {
                        AdvanceOrFinishAction();
                    }
                }
                break;

            case PetState.Dance:
                _stateTimer -= deltaSeconds;
                _velocity = PointF.Empty;
                if (_stateTimer <= 0)
                    StartNewIdleAction();
                break;

            case PetState.Sleep:
                _velocity = PointF.Empty;
                if (cursorMoved)
                {
                    SleepTwitch = false;
                    StartNewIdleAction();
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
                    StartNewIdleAction();
                }
                else if (dist <= CatchDistance)
                {
                    _velocity = PointF.Empty;
                    if (_eatCooldownTimer <= 0)
                    {
                        State = PetState.Eat;
                        _stateTimer = EatDurationSec;
                        _eatCooldownTimer = EatCooldownSec;
                        _eatChainsToDance = true;
                    }
                    else
                    {
                        StartNewIdleAction();
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
                        StartNewIdleAction();
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
                // Idle or Walk under idle-action control
                if (_ball != null && _ball.IsAvailable)
                {
                    State = PetState.FetchBall;
                    _velocity = PointF.Empty;
                }
                else if (cursorIsMoving && dist > NoticeDistance)
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
                    TickIdleAction(deltaSeconds, cursorPos);
                }
                break;
        }

        if (State == PetState.Walk || State == PetState.Chase || State == PetState.WalkToSleep
            || State == PetState.FetchBall || State == PetState.ReturnBall)
        {
            _position.X += _velocity.X * (float)deltaSeconds;
            _position.Y += _velocity.Y * (float)deltaSeconds;
            ClampToBounds();

            if (MathF.Abs(_velocity.X) > 1f || MathF.Abs(_velocity.Y) > 1f)
                _targetYaw = MathF.Atan2(_velocity.X, _velocity.Y);
        }

        if (State == PetState.ReturnBall && _ball != null && _ball.State == BallState.CarriedByPet)
        {
            _ball.PositionWithPet(new PointF(_position.X + _size.Width / 2f, _position.Y + _size.Height / 2f));
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

    // ---- Idle action system ------------------------------------------------

    private void StartNewIdleAction()
    {
        var values = Enum.GetValues<IdleAction>();
        _action = values[_rng.Next(values.Length)];
        _eatChainsToDance = false;
        InitActionPhase(0);
    }

    private void AdvanceOrFinishAction()
    {
        int maxPhase = _action switch
        {
            IdleAction.Sneeze => 1,
            IdleAction.Yawn => 1,
            IdleAction.Bow => 1,
            IdleAction.Pounce => 1,
            IdleAction.CornerVisit => 1,
            IdleAction.LookAround => 2,
            _ => 0,
        };
        if (_actionPhase < maxPhase) InitActionPhase(_actionPhase + 1);
        else StartNewIdleAction();
    }

    private void InitActionPhase(int phase)
    {
        _actionPhase = phase;
        _actionElapsed = 0;
        State = PetState.Idle;
        _velocity = PointF.Empty;
        float cx = _position.X + _size.Width / 2f;
        float cy = _position.Y + _size.Height / 2f;

        switch (_action)
        {
            case IdleAction.Wander:
                StartWalkRandomDir(1f);
                _actionPhaseTimer = 2.0 + _rng.NextDouble() * 2.5;
                break;

            case IdleAction.Patrol:
                if (phase == 0) GenerateWaypoints(3, 120f, 220f, cx, cy);
                if (_actionWaypointIdx < _actionWaypoints.Length)
                    StartWalkToward(_actionWaypoints[_actionWaypointIdx], 1f);
                _actionPhaseTimer = 7.0;
                break;

            case IdleAction.Pace:
                if (phase == 0) GeneratePaceWaypoints(70f, cx, cy);
                if (_actionWaypointIdx < _actionWaypoints.Length)
                    StartWalkToward(_actionWaypoints[_actionWaypointIdx], 0.85f);
                _actionPhaseTimer = 5.0;
                break;

            case IdleAction.Circle:
            case IdleAction.Lap:
                _actionRadius = 50f + (float)_rng.NextDouble() * 25f;
                // place anchor so the pet starts on the perimeter
                _actionAnchor = new PointF(cx, cy - _actionRadius);
                _actionAngle = MathF.PI; // current pos = anchor + (sin π, cos π)*r = (0, -r) → cy - r ... no, want (0, +r) → angle 0. Recompute.
                // Want sin(a)*r=0 and cos(a)*r=+r so pet=anchor+(0,r)=(cx,cy). a=0.
                _actionAngle = 0f;
                _actionAngularSpeed = (_rng.Next(2) == 0 ? 1f : -1f) * (MathF.Tau / 3.2f);
                State = PetState.Walk;
                _actionPhaseTimer = _action == IdleAction.Lap ? 9.5 : 3.5;
                break;

            case IdleAction.FigureEight:
                _actionRadius = 55f + (float)_rng.NextDouble() * 20f;
                _actionAnchor = new PointF(cx, cy);
                _actionAngle = 0f;
                _actionAngularSpeed = MathF.Tau / 4.5f;
                State = PetState.Walk;
                _actionPhaseTimer = 5.5;
                break;

            case IdleAction.Zigzag:
                if (phase == 0) GenerateZigzagWaypoints(60f, cx, cy);
                if (_actionWaypointIdx < _actionWaypoints.Length)
                    StartWalkToward(_actionWaypoints[_actionWaypointIdx], 1f);
                _actionPhaseTimer = 7.0;
                break;

            case IdleAction.Sprint:
                StartWalkRandomDir(1.9f);
                _actionPhaseTimer = 0.9;
                break;

            case IdleAction.Sneak:
                StartWalkRandomDir(0.4f);
                _actionPhaseTimer = 3.0;
                break;

            case IdleAction.PerimeterWalk:
                StartPerimeterWalk(cx, cy);
                _actionPhaseTimer = 4.5;
                break;

            case IdleAction.CornerVisit:
                if (phase == 0)
                {
                    int corner = _rng.Next(4);
                    float pad = 24f;
                    float tx = (corner % 2 == 0 ? _bounds.Left + pad : _bounds.Right - pad - _size.Width) + _size.Width / 2f;
                    float ty = (corner < 2 ? _bounds.Top + pad : _bounds.Bottom - pad - _size.Height) + _size.Height / 2f;
                    _actionWaypoints = new[] { new PointF(tx, ty) };
                    _actionWaypointIdx = 0;
                    StartWalkToward(_actionWaypoints[0], 1f);
                    _actionPhaseTimer = 7.0;
                }
                else
                {
                    State = PetState.Idle;
                    _actionPhaseTimer = 1.3;
                }
                break;

            case IdleAction.Tiptoe:
                _actionYawOrigin = (float)(_rng.NextDouble() * Math.PI * 2);
                State = PetState.Walk;
                _actionPhaseTimer = 3.0;
                break;

            case IdleAction.SpinInPlace:
            case IdleAction.ReverseSpin:
                _actionYawOrigin = Yaw;
                _actionAngularSpeed = (_action == IdleAction.SpinInPlace ? 1f : -1f) * (MathF.Tau / 1.6f);
                _actionPhaseTimer = 1.6;
                break;

            case IdleAction.LookAround:
                if (phase == 0)
                {
                    _actionYawOrigin = Yaw;
                    _targetYaw = _actionYawOrigin + MathF.PI / 2f;
                    _actionPhaseTimer = 0.9;
                }
                else if (phase == 1)
                {
                    _targetYaw = _actionYawOrigin - MathF.PI / 2f;
                    _actionPhaseTimer = 1.1;
                }
                else
                {
                    _targetYaw = _actionYawOrigin;
                    _actionPhaseTimer = 0.8;
                }
                break;

            case IdleAction.HeadShakeQuirk:
                _actionYawOrigin = Yaw;
                _actionPhaseTimer = 1.4;
                break;

            case IdleAction.StareAtCorner:
                {
                    int corner = _rng.Next(4);
                    float tx = (corner % 2 == 0 ? _bounds.Left + 20f : _bounds.Right - 20f);
                    float ty = (corner < 2 ? _bounds.Top + 20f : _bounds.Bottom - 20f);
                    _targetYaw = MathF.Atan2(tx - cx, ty - cy);
                }
                _actionPhaseTimer = 2.5;
                break;

            case IdleAction.Sit:
                _actionPhaseTimer = 2.8;
                break;

            case IdleAction.Stretch:
                _actionYawOrigin = Yaw;
                _actionPhaseTimer = 2.4;
                break;

            case IdleAction.Sneeze:
                if (phase == 0)
                {
                    State = PetState.Eat;
                    _stateTimer = 0.22;
                    _eatChainsToDance = false;
                    _actionPhaseTimer = 0.22;
                }
                else
                {
                    State = PetState.Idle;
                    _actionPhaseTimer = 0.45;
                }
                break;

            case IdleAction.Groom:
                State = PetState.Eat;
                _stateTimer = 0.9;
                _eatChainsToDance = false;
                _actionPhaseTimer = 0.9;
                break;

            case IdleAction.Yawn:
                if (phase == 0)
                {
                    State = PetState.Eat;
                    _stateTimer = 0.7;
                    _eatChainsToDance = false;
                    _actionPhaseTimer = 0.7;
                }
                else
                {
                    State = PetState.Idle;
                    _actionPhaseTimer = 0.8;
                }
                break;

            case IdleAction.Bow:
                if (phase == 0)
                {
                    float bx = _lastCursorPos.X - cx;
                    float by = _lastCursorPos.Y - cy;
                    if (bx * bx + by * by > 1f)
                        _targetYaw = MathF.Atan2(bx, by);
                    _actionPhaseTimer = 0.55;
                }
                else
                {
                    State = PetState.Eat;
                    _stateTimer = 0.6;
                    _eatChainsToDance = false;
                    _actionPhaseTimer = 0.6;
                }
                break;

            case IdleAction.Pounce:
                if (phase == 0)
                {
                    State = PetState.Idle;
                    _actionPhaseTimer = 0.65;
                }
                else
                {
                    StartWalkRandomDir(2.0f);
                    _actionPhaseTimer = 0.55;
                }
                break;

            case IdleAction.Spook:
                {
                    float newYaw = Yaw + MathF.PI;
                    _targetYaw = newYaw;
                    float spd = _walkSpeed * 1.7f;
                    _velocity = new PointF(MathF.Sin(newYaw) * spd, MathF.Cos(newYaw) * spd);
                    State = PetState.Walk;
                    _actionPhaseTimer = 1.1;
                }
                break;
        }
    }

    private void TickIdleAction(double dt, Point cursorPos)
    {
        _actionPhaseTimer -= dt;
        _actionElapsed += dt;
        float cx = _position.X + _size.Width / 2f;
        float cy = _position.Y + _size.Height / 2f;

        switch (_action)
        {
            case IdleAction.Wander:
            case IdleAction.Sprint:
            case IdleAction.Sneak:
            case IdleAction.PerimeterWalk:
            case IdleAction.Sit:
            case IdleAction.StareAtCorner:
            case IdleAction.Groom:
            case IdleAction.Spook:
                if (_actionPhaseTimer <= 0) AdvanceOrFinishAction();
                break;

            case IdleAction.Patrol:
            case IdleAction.Zigzag:
            case IdleAction.Pace:
                if (_actionWaypointIdx >= _actionWaypoints.Length)
                {
                    AdvanceOrFinishAction();
                    break;
                }
                {
                    var t = _actionWaypoints[_actionWaypointIdx];
                    float wdx = t.X - cx, wdy = t.Y - cy;
                    float wd = MathF.Sqrt(wdx * wdx + wdy * wdy);
                    if (wd < WaypointArriveRadius)
                    {
                        _actionWaypointIdx++;
                        if (_actionWaypointIdx >= _actionWaypoints.Length)
                        {
                            AdvanceOrFinishAction();
                            break;
                        }
                        float mul = _action == IdleAction.Pace ? 0.85f : 1f;
                        StartWalkToward(_actionWaypoints[_actionWaypointIdx], mul);
                    }
                    else
                    {
                        float mul = _action == IdleAction.Pace ? 0.85f : 1f;
                        float inv = 1f / wd;
                        _velocity = new PointF(wdx * inv * _walkSpeed * mul, wdy * inv * _walkSpeed * mul);
                    }
                }
                if (_actionPhaseTimer <= 0) AdvanceOrFinishAction();
                break;

            case IdleAction.Circle:
            case IdleAction.Lap:
            case IdleAction.FigureEight:
                _actionAngle += (float)(_actionAngularSpeed * dt);
                {
                    float tx, ty;
                    if (_action == IdleAction.FigureEight)
                    {
                        tx = _actionAnchor.X + MathF.Sin(_actionAngle) * _actionRadius;
                        ty = _actionAnchor.Y + MathF.Sin(_actionAngle) * MathF.Cos(_actionAngle) * _actionRadius;
                    }
                    else
                    {
                        tx = _actionAnchor.X + MathF.Sin(_actionAngle) * _actionRadius;
                        ty = _actionAnchor.Y + MathF.Cos(_actionAngle) * _actionRadius;
                    }
                    float vx = tx - cx, vy = ty - cy;
                    float vd = MathF.Sqrt(vx * vx + vy * vy);
                    if (vd > 0.001f)
                    {
                        float inv = _walkSpeed / vd;
                        _velocity = new PointF(vx * inv, vy * inv);
                    }
                }
                if (_actionPhaseTimer <= 0) AdvanceOrFinishAction();
                break;

            case IdleAction.CornerVisit:
                if (_actionPhase == 0)
                {
                    var t = _actionWaypoints[0];
                    float wdx = t.X - cx, wdy = t.Y - cy;
                    float wd = MathF.Sqrt(wdx * wdx + wdy * wdy);
                    if (wd < WaypointArriveRadius)
                    {
                        AdvanceOrFinishAction();
                    }
                    else if (_actionPhaseTimer <= 0)
                    {
                        StartNewIdleAction();
                    }
                    else
                    {
                        float inv = 1f / wd;
                        _velocity = new PointF(wdx * inv * _walkSpeed, wdy * inv * _walkSpeed);
                    }
                }
                else
                {
                    if (_actionPhaseTimer <= 0) AdvanceOrFinishAction();
                }
                break;

            case IdleAction.Tiptoe:
                {
                    float wobble = MathF.Sin((float)(_actionElapsed * 7.0)) * 0.30f;
                    float dir = _actionYawOrigin + wobble;
                    float spd = _walkSpeed * 0.5f;
                    _velocity = new PointF(MathF.Sin(dir) * spd, MathF.Cos(dir) * spd);
                }
                if (_actionPhaseTimer <= 0) AdvanceOrFinishAction();
                break;

            case IdleAction.SpinInPlace:
            case IdleAction.ReverseSpin:
                {
                    float y = _actionYawOrigin + (float)(_actionAngularSpeed * _actionElapsed);
                    Yaw = y;
                    _targetYaw = y;
                }
                if (_actionPhaseTimer <= 0) AdvanceOrFinishAction();
                break;

            case IdleAction.LookAround:
                if (_actionPhaseTimer <= 0) AdvanceOrFinishAction();
                break;

            case IdleAction.HeadShakeQuirk:
                {
                    float y = _actionYawOrigin + MathF.Sin((float)(_actionElapsed * 14.0)) * 0.35f;
                    Yaw = y;
                    _targetYaw = y;
                }
                if (_actionPhaseTimer <= 0) AdvanceOrFinishAction();
                break;

            case IdleAction.Stretch:
                {
                    float y = _actionYawOrigin + MathF.Sin((float)(_actionElapsed * 1.8)) * 0.08f;
                    _targetYaw = y;
                }
                if (_actionPhaseTimer <= 0) AdvanceOrFinishAction();
                break;

            case IdleAction.Sneeze:
            case IdleAction.Yawn:
            case IdleAction.Bow:
            case IdleAction.Pounce:
                // Eat-state phases are handled by the Eat case; idle phases land here.
                if (_actionPhaseTimer <= 0) AdvanceOrFinishAction();
                break;
        }
    }

    // ---- Helpers -----------------------------------------------------------

    private void StartWalkRandomDir(float speedMul)
    {
        State = PetState.Walk;
        double ang = _rng.NextDouble() * Math.PI * 2;
        _velocity = new PointF(
            (float)(Math.Cos(ang) * _walkSpeed * speedMul),
            (float)(Math.Sin(ang) * _walkSpeed * speedMul));
    }

    private void StartWalkToward(PointF target, float speedMul)
    {
        State = PetState.Walk;
        float cx = _position.X + _size.Width / 2f;
        float cy = _position.Y + _size.Height / 2f;
        float dx = target.X - cx;
        float dy = target.Y - cy;
        float d = MathF.Sqrt(dx * dx + dy * dy);
        if (d < 0.001f) { _velocity = PointF.Empty; return; }
        float spd = _walkSpeed * speedMul;
        _velocity = new PointF(dx / d * spd, dy / d * spd);
    }

    private void StartPerimeterWalk(float cx, float cy)
    {
        float distL = cx - _bounds.Left;
        float distR = _bounds.Right - cx;
        float distT = cy - _bounds.Top;
        float distB = _bounds.Bottom - cy;
        float minD = MathF.Min(MathF.Min(distL, distR), MathF.Min(distT, distB));
        State = PetState.Walk;
        int dir = _rng.Next(2) == 0 ? 1 : -1;
        if (minD == distT || minD == distB)
            _velocity = new PointF(_walkSpeed * dir, 0);
        else
            _velocity = new PointF(0, _walkSpeed * dir);
    }

    private void GenerateWaypoints(int n, float minR, float maxR, float cx, float cy)
    {
        _actionWaypoints = new PointF[n];
        for (int i = 0; i < n; i++)
        {
            double ang = _rng.NextDouble() * Math.PI * 2;
            float r = minR + (float)_rng.NextDouble() * (maxR - minR);
            float tx = cx + (float)Math.Cos(ang) * r;
            float ty = cy + (float)Math.Sin(ang) * r;
            tx = Math.Clamp(tx, _bounds.Left + _size.Width / 2f, _bounds.Right - _size.Width / 2f);
            ty = Math.Clamp(ty, _bounds.Top + _size.Height / 2f, _bounds.Bottom - _size.Height / 2f);
            _actionWaypoints[i] = new PointF(tx, ty);
        }
        _actionWaypointIdx = 0;
    }

    private void GeneratePaceWaypoints(float dist, float cx, float cy)
    {
        double ang = _rng.NextDouble() * Math.PI * 2;
        float dx = (float)Math.Cos(ang) * dist;
        float dy = (float)Math.Sin(ang) * dist;
        var a = ClampToInside(new PointF(cx + dx, cy + dy));
        var b = ClampToInside(new PointF(cx - dx, cy - dy));
        _actionWaypoints = new[] { a, b, a, b };
        _actionWaypointIdx = 0;
    }

    private void GenerateZigzagWaypoints(float step, float cx, float cy)
    {
        double ang = _rng.NextDouble() * Math.PI * 2;
        float fx = (float)Math.Cos(ang), fy = (float)Math.Sin(ang);
        float px = -fy, py = fx;
        _actionWaypoints = new PointF[4];
        for (int i = 0; i < 4; i++)
        {
            float t = (i + 1) * step;
            float side = (i % 2 == 0 ? 1f : -1f) * step * 0.5f;
            _actionWaypoints[i] = ClampToInside(new PointF(cx + fx * t + px * side, cy + fy * t + py * side));
        }
        _actionWaypointIdx = 0;
    }

    private PointF ClampToInside(PointF p)
    {
        float halfW = _size.Width / 2f, halfH = _size.Height / 2f;
        return new PointF(
            Math.Clamp(p.X, _bounds.Left + halfW, _bounds.Right - halfW),
            Math.Clamp(p.Y, _bounds.Top + halfH, _bounds.Bottom - halfH));
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
