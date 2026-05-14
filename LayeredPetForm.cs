using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Neko;

internal sealed class LayeredPetForm : Form
{
    public const int RenderSize = 128;
    private const int FrameIntervalMs = 16;

    private IPetBehavior _pet;
    private readonly Renderer.Scene _scene;
    private readonly ZParticles _zParticles = new();
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private TimeSpan _lastTick;
    private double _lastDelta;
    private Rectangle _screenArea;

    public LayeredPetForm(Renderer.Scene scene)
    {
        _scene = scene;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(RenderSize, RenderSize);

        _screenArea = Screen.PrimaryScreen?.WorkingArea
            ?? new Rectangle(0, 0, 1920, 1080);
        _pet = new Pet(_screenArea, Size);

        _timer = new System.Windows.Forms.Timer { Interval = FrameIntervalMs };
        _timer.Tick += OnTick;
        _timer.Start();

        Location = new Point((int)_pet.Position.X, (int)_pet.Position.Y);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_LAYERED = 0x00080000;
            const int WS_EX_TOOLWINDOW = 0x00000080;
            const int WS_EX_NOACTIVATE = 0x08000000;
            const int WS_EX_TRANSPARENT = 0x00000020;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RenderAndPush();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = _stopwatch.Elapsed;
        var delta = (now - _lastTick).TotalSeconds;
        _lastTick = now;
        _lastDelta = delta;

        _pet.Update(delta, Cursor.Position);

        Location = new Point((int)_pet.Position.X, (int)_pet.Position.Y);
        RenderAndPush();
    }

    private void RenderAndPush()
    {
        if (!IsHandleCreated) return;
        _zParticles.Update(_lastDelta, _pet.State == PetState.Sleep, RenderSize);
        var bmp = _scene.Render(_pet.Yaw, _pet.State, _pet.SleepTwitch, (float)_lastDelta);
        _zParticles.Draw(bmp);
        PushLayered(bmp);
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
                BlendOp = AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = AC_SRC_ALPHA,
            };
            UpdateLayeredWindow(Handle, screenDc, ref topPos, ref size, memDc, ref srcLoc,
                0, ref blend, ULW_ALPHA);
        }
        finally
        {
            SelectObject(memDc, oldBitmap);
            DeleteObject(hBitmap);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    public void SetBehavior(BehaviorKind kind)
    {
        _pet = kind switch
        {
            BehaviorKind.WindowWalker => new WindowWalker(_screenArea, Size, Handle),
            _ => new Pet(_screenArea, Size),
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }

    private const byte AC_SRC_OVER = 0;
    private const byte AC_SRC_ALPHA = 1;
    private const uint ULW_ALPHA = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx; public int cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(
        IntPtr hwnd, IntPtr hdcDst,
        ref POINT pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pptSrc,
        uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
}
