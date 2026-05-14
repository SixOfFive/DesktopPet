using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Neko;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var modelPaths = DiscoverModels();
        if (modelPaths.Length == 0)
        {
            MessageBox.Show(
                "Couldn't find any .glb in the 'pets' folder next to Neko.exe.",
                "Neko", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var initialPath = modelPaths.FirstOrDefault(p =>
            Path.GetFileName(p).Equals("animal-cat.glb", StringComparison.OrdinalIgnoreCase))
            ?? modelPaths[0];

        using var glHost = new Renderer.GlHost();
        using var scene = new Renderer.Scene(glHost.Gl, initialPath, LayeredPetForm.RenderSize, LayeredPetForm.RenderSize);

        var models = modelPaths.Select(p => (Name: PrettyName(p), Path: p));
        using var tray = new TrayIcon(models, initialPath);
        using var form = new LayeredPetForm(scene);

        tray.ExitRequested += (_, _) => Application.ExitThread();
        tray.ModelChangeRequested += (_, path) => scene.LoadModel(path);

        form.Show();

        Application.Run();
    }

    private static string[] DiscoverModels()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "pets");
        if (!Directory.Exists(dir)) return Array.Empty<string>();
        var files = Directory.GetFiles(dir, "*.glb");
        Array.Sort(files);
        return files;
    }

    private static string PrettyName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (name.StartsWith("animal-", StringComparison.OrdinalIgnoreCase))
            name = name[7..];
        return name.Length == 0 ? path : char.ToUpper(name[0]) + name[1..];
    }
}
