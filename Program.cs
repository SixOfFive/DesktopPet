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

        var settings = Settings.Load();

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
        var currentSelection = new PetSelection(initialPath, BehaviorKind.FreeWander);

        int initialSize = LayeredPetForm.SizeForMultiplier(settings.SizeMultiplier);

        string? initialSkin = null;
        var initialSkinPath = Path.ChangeExtension(initialPath, null) + "-skin.png";
        if (File.Exists(initialSkinPath)) initialSkin = initialSkinPath;

        using var glHost = new Renderer.GlHost();
        using var scene = new Renderer.Scene(glHost.Gl, initialPath, initialSize, initialSize, initialSkin);

        var groups = BuildMenuGroups(modelPaths);
        using var tray = new TrayIcon(groups, currentSelection);
        using var form = new LayeredPetForm(scene);
        form.SetRenderSize(initialSize);

        tray.ExitRequested += (_, _) => Application.ExitThread();

        tray.PetSelected += (_, selection) =>
        {
            currentSelection = selection;
            ApplySelection(selection, scene, form);
        };

        tray.SizeChangeRequested += (_, change) =>
        {
            float newMul = change switch
            {
                SizeChange.Half => settings.SizeMultiplier * 0.5f,
                SizeChange.Regular => 1.0f,
                SizeChange.Double => settings.SizeMultiplier * 2.0f,
                _ => settings.SizeMultiplier,
            };
            settings.SizeMultiplier = Math.Clamp(newMul, Settings.MinMultiplier, Settings.MaxMultiplier);
            settings.Save();

            int size = LayeredPetForm.SizeForMultiplier(settings.SizeMultiplier);
            form.SetRenderSize(size);
            scene.Resize(size, size);
            form.SetBehavior(currentSelection.Behavior);
        };

        form.Show();
        Application.Run();
    }

    private static void ApplySelection(PetSelection selection, Renderer.Scene scene, LayeredPetForm form)
    {
        if (IsSkinnedCharacter(selection.ModelPath, out var anims, out var tex))
        {
            scene.LoadSkinnedModel(selection.ModelPath, anims, tex);
        }
        else
        {
            var skinPath = Path.ChangeExtension(selection.ModelPath, null) + "-skin.png";
            scene.LoadModel(selection.ModelPath, File.Exists(skinPath) ? skinPath : null);
        }
        form.SetBehavior(selection.Behavior);
    }

    private static IEnumerable<TrayMenuGroup> BuildMenuGroups(string[] modelPaths)
    {
        var charactersDir = Path.Combine(AppContext.BaseDirectory, "characters");
        var zombieMesh = Path.Combine(charactersDir, "characterMedium.glb");
        bool hasZombie = File.Exists(zombieMesh);

        var pets = modelPaths
            .Select(p => new TrayMenuEntry(PrettyName(p), p, BehaviorKind.FreeWander))
            .ToList();
        if (hasZombie)
            pets.Add(new TrayMenuEntry("Zombie", zombieMesh, BehaviorKind.FreeWander));

        var characters = new List<TrayMenuEntry>();
        if (hasZombie)
            characters.Add(new TrayMenuEntry("Zombie", zombieMesh, BehaviorKind.WindowWalker));

        var groups = new List<TrayMenuGroup> { new("Pets", pets) };
        if (characters.Count > 0)
            groups.Add(new TrayMenuGroup("Characters", characters));
        return groups;
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
