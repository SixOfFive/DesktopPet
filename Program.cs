using System;
using System.Collections.Generic;
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
        var initial = new PetSelection(initialPath, BehaviorKind.FreeWander);

        using var glHost = new Renderer.GlHost();
        using var scene = new Renderer.Scene(glHost.Gl, initialPath, LayeredPetForm.CubePetRenderSize, LayeredPetForm.CubePetRenderSize);

        var groups = BuildMenuGroups(modelPaths);
        using var tray = new TrayIcon(groups, initial);
        using var form = new LayeredPetForm(scene);

        tray.ExitRequested += (_, _) => Application.ExitThread();
        tray.PetSelected += (_, selection) =>
        {
            bool isSkinned = IsSkinnedCharacter(selection.ModelPath, out var anims, out var tex);
            int size = isSkinned ? LayeredPetForm.CharacterRenderSize : LayeredPetForm.CubePetRenderSize;
            form.SetRenderSize(size);
            scene.Resize(size, size);
            if (isSkinned)
                scene.LoadSkinnedModel(selection.ModelPath, anims, tex);
            else
                scene.LoadModel(selection.ModelPath);
            form.SetBehavior(selection.Behavior);
        };

        form.Show();

        Application.Run();
    }

    private static IEnumerable<TrayMenuGroup> BuildMenuGroups(string[] modelPaths)
    {
        var pets = modelPaths
            .Select(p => new TrayMenuEntry(PrettyName(p), p, BehaviorKind.FreeWander))
            .ToArray();

        var monkey = modelPaths.FirstOrDefault(p =>
            Path.GetFileName(p).Equals("animal-monkey.glb", StringComparison.OrdinalIgnoreCase))
            ?? modelPaths[0];

        var charactersDir = Path.Combine(AppContext.BaseDirectory, "characters");
        var zombieMesh = Path.Combine(charactersDir, "characterMedium.glb");
        var characters = new List<TrayMenuEntry>
        {
            new("Window Walker", monkey, BehaviorKind.WindowWalker),
        };
        if (File.Exists(zombieMesh))
            characters.Add(new TrayMenuEntry("Zombie", zombieMesh, BehaviorKind.WindowWalker));

        return new[]
        {
            new TrayMenuGroup("Pets", pets),
            new TrayMenuGroup("Characters", characters),
        };
    }

    private static bool IsSkinnedCharacter(string path, out string[] animGlbs, out string? texture)
    {
        animGlbs = Array.Empty<string>();
        texture = null;
        var dir = Path.GetDirectoryName(path);
        if (dir == null || !Path.GetFileName(dir).Equals("characters", StringComparison.OrdinalIgnoreCase))
            return false;
        animGlbs = new[] { "idle.glb", "jump.glb", "run.glb" }
            .Select(f => Path.Combine(dir, f))
            .Where(File.Exists)
            .ToArray();
        var tex = Path.Combine(dir, "zombieMaleA.png");
        texture = File.Exists(tex) ? tex : null;
        return true;
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
