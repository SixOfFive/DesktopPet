using System;
using System.IO;
using System.Text.Json;

namespace Neko;

internal sealed class Settings
{
    public float SizeMultiplier { get; set; } = 1.0f;
    public string? SelectedModelPath { get; set; }
    public string? SelectedBehavior { get; set; }
    public string? SelectedTexturePath { get; set; }
    public bool BallVisible { get; set; }

    public const float MinMultiplier = 0.25f;
    public const float MaxMultiplier = 4.0f;

    private const string FileName = "settings.json";

    public static Settings Load()
    {
        try
        {
            var path = GetPath();
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<Settings>(json);
                if (loaded != null)
                {
                    loaded.SizeMultiplier = Math.Clamp(loaded.SizeMultiplier, MinMultiplier, MaxMultiplier);
                    return loaded;
                }
            }
        }
        catch { }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            var path = GetPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch { }
    }

    private static string GetPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Neko");
        return Path.Combine(dir, FileName);
    }
}
