using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Neko;

internal enum BallState { Free, Held, Thrown, CarriedByPet }

internal sealed class BallForm : Form
{
    public const int BallSize = 48;

    private const float Gravity = 1400f;
    private const float AirDrag = 0.998f;
    private const float BounceDamping = 0.55f;
    private const float GroundFriction = 0.92f;
    private const float StopThreshold = 4f;

    private readonly System.Windows.Forms.Timer _timer;
    private readonly Bitmap _ballBitmap;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private TimeSpan _lastTick;
    private readonly Queue<(PointF pos, double t)> _dragHistory = new();

    private PointF _position;
    private PointF _velocity;
    private Point _dragOffset;
    private Rectangle _screenArea;

    public BallState State { get; private set; } = BallState.Free;
    public PointF Position => _position;
    public PointF Center => new(_position.X + BallSize / 2f, _position.Y + BallSize / 2f);
    public bool IsAvailable => State == BallState.Free || State == BallState.Thrown;

    public BallForm(Rectangle screenArea)
    {
        _screenArea = screenArea;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(BallSize, BallSize);

        _ballBitmap = CreateBallBitmap();
        _position = new PointF(
            screenArea.X + screenArea.Width / 2f - BallSize / 2f,
            screenArea.Y + screenArea.Height / 2f - BallSize / 2f);

        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;

        _timer = new System.Windows.Forms.Timer { Interval = 16 };
        _timer.Tick += OnTick;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_LAYERED = 0x00080000;
            const int WS_EX_TOOLWINDOW = 0x00000080;
            const int WS_EX_NOACTIVATE = 0x08000000;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _timer.Start();
        SyncWindow();
    }

    public void GrabByPet()
    {
        State = BallState.CarriedByPet;
        _velocity = PointF.Empty;
    }

    public void DropAt(PointF position)
    {
        _position = position;
        State = BallState.Free;
        _velocity = PointF.Empty;
        SyncWindow();
    }

    public void PositionWithPet(PointF petCenter)
    {
        _position = new PointF(petCenter.X - BallSize / 2f, petCenter.Y - BallSize / 2f);
        SyncWindow();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = _stopwatch.Elapsed;
        double dt = (now - _lastTick).TotalSeconds;
        _lastTick = now;

        if (State == BallState.Free || State == BallState.Thrown)
            UpdatePhysics(dt);

        SyncWindow();
    }

    private void UpdatePhysics(double dt)
    {
        float fdt = (float)dt;
        _velocity.Y += Gravity * fdt;
        _velocity.X *= AirDrag;
        _velocity.Y *= AirDrag;

        _position.X += _velocity.X * fdt;
        _position.Y += _velocity.Y * fdt;

        if (_position.X < _screenArea.Left)
        {
            _position.X = _screenArea.Left;
            _velocity.X = -_velocity.X * BounceDamping;
        }
        else if (_position.X + BallSize > _screenArea.Right)
        {
            _position.X = _screenArea.Right - BallSize;
            _velocity.X = -_velocity.X * BounceDamping;
        }

        if (_position.Y < _screenArea.Top)
        {
            _position.Y = _screenArea.Top;
            _velocity.Y = -_velocity.Y * BounceDamping;
        }
        else if (_position.Y + BallSize > _screenArea.Bottom)
        {
            _position.Y = _screenArea.Bottom - BallSize;
            _velocity.Y = -_velocity.Y * BounceDamping;
            _velocity.X *= GroundFriction;
        }

        if (MathF.Abs(_velocity.X) < StopThreshold
            && MathF.Abs(_velocity.Y) < StopThreshold
            && _position.Y + BallSize >= _screenArea.Bottom - 1f)
        {
            _velocity = PointF.Empty;
            State = BallState.Free;
        }
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        State = BallState.Held;
        _dragOffset = e.Location;
        _dragHistory.Clear();
        Capture = true;
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (State != BallState.Held) return;
        var cursor = Cursor.Position;
        _position = new PointF(cursor.X - _dragOffset.X, cursor.Y - _dragOffset.Y);
        _dragHistory.Enqueue((_position, _stopwatch.Elapsed.TotalSeconds));
        while (_dragHistory.Count > 6) _dragHistory.Dequeue();
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (State != BallState.Held) return;
        Capture = false;

        _velocity = PointF.Empty;
        if (_dragHistory.Count >= 2)
        {
            var arr = _dragHistory.ToArray();
            var first = arr[0];
            var last = arr[arr.Length - 1];
            double dtSpan = last.t - first.t;
            if (dtSpan > 0.01)
            {
                _velocity = new PointF(
                    (float)((last.pos.X - first.pos.X) / dtSpan),
                    (float)((last.pos.Y - first.pos.Y) / dtSpan));
            }
        }

        State = (MathF.Abs(_velocity.X) > 1f || MathF.Abs(_velocity.Y) > 1f)
            ? BallState.Thrown
            : BallState.Free;
    }

    private void SyncWindow()
    {
        if (!IsHandleCreated) return;
        Location = new Point((int)_position.X, (int)_position.Y);
        PushLayered(_ballBitmap);
    }

    private void PushLayered(Bitmap bmp)
    {
        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(screenDc);
        IntPtr hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
        IntPtr oldBitmap = SelectObject(memDc, hBitmap);
        try
        {
            var size = new SIZE { cx = bmp.Width, cy = bmp.Height };
            var srcLoc = new POINT { x = 0, y = 0 };
            var topPos = new POINT { x = Location.X, y = Location.Y };
            var blend = new BLENDFUNCTION
            {
                BlendOp = 0,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = 1,
            };
            UpdateLayeredWindow(Handle, screenDc, ref topPos, ref size, memDc, ref srcLoc,
                0, ref blend, 2);
        }
        finally
        {
            SelectObject(memDc, oldBitmap);
            DeleteObject(hBitmap);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static Bitmap CreateBallBitmap()
    {
        var bmp = new Bitmap(BallSize, BallSize, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // soft shadow
        using (var shadow = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))
            g.FillEllipse(shadow, 3, 6, 42, 42);

        // ball with radial gradient
        var ballRect = new Rectangle(2, 2, 42, 42);
        using var path = new GraphicsPath();
        path.AddEllipse(ballRect);
        using var brush = new PathGradientBrush(path)
        {
            CenterPoint = new PointF(15, 13),
            CenterColor = Color.FromArgb(255, 255, 180, 180),
            SurroundColors = new[] { Color.FromArgb(255, 180, 30, 30) },
        };
        g.FillEllipse(brush, ballRect);

        // small highlight
        using (var highlight = new SolidBrush(Color.FromArgb(200, 255, 255, 255)))
            g.FillEllipse(highlight, 13, 10, 8, 6);

        return bmp;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _ballBitmap.Dispose();
        }
        base.Dispose(disposing);
    }

    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx; public int cy; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x; public int y; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst,
        ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc,
        uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
}
