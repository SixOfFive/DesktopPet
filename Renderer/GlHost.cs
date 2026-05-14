using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace Neko.Renderer;

internal sealed class GlHost : IDisposable
{
    public GL Gl { get; }
    public IWindow Window { get; }

    public GlHost()
    {
        var options = WindowOptions.Default with
        {
            Size = new Vector2D<int>(1, 1),
            Title = "Neko-GL-Hidden",
            IsVisible = false,
            ShouldSwapAutomatically = false,
            API = new GraphicsAPI(
                ContextAPI.OpenGL,
                ContextProfile.Core,
                ContextFlags.ForwardCompatible,
                new APIVersion(3, 3)),
        };
        Window = Silk.NET.Windowing.Window.Create(options);
        Window.Initialize();
        Gl = Window.CreateOpenGL();
    }

    public void Dispose()
    {
        Window.Dispose();
    }
}
