using System;
using System.IO;
using System.Windows.Forms;

namespace Neko;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var modelPath = ResolveModelPath();
        if (modelPath == null)
        {
            MessageBox.Show(
                "Couldn't find a .glb in the 'pets' folder next to Neko.exe.",
                "Neko", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        using var glHost = new Renderer.GlHost();
        using var scene = new Renderer.Scene(glHost.Gl, modelPath, LayeredPetForm.RenderSize, LayeredPetForm.RenderSize);

        using var tray = new TrayIcon();
        using var form = new LayeredPetForm(scene);

        tray.ExitRequested += (_, _) => Application.ExitThread();
        form.Show();

        Application.Run();
    }

    private static string? ResolveModelPath()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "pets");
        if (!Directory.Exists(dir)) return null;

        var preferred = Path.Combine(dir, "animal-cat.glb");
        if (File.Exists(preferred)) return preferred;

        var any = Directory.GetFiles(dir, "*.glb");
        Array.Sort(any);
        return any.Length > 0 ? any[0] : null;
    }
}
