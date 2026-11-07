using System.IO;
using System.Text.Json;

namespace OsdPanel;

public class AppSettings
{
    public double Left { get; set; }
    public double Top { get; set; }
    public int? PixelLeft { get; set; }
    public int? PixelTop { get; set; }
    public int? GameOverlayPixelLeft { get; set; }
    public int? GameOverlayPixelTop { get; set; }
    public bool TopMost { get; set; } = true;
    public double FontSize { get; set; } = 18;
    public int BackgroundOpacity { get; set; } = 80;
    public int UpdateIntervalMs { get; set; } = 1000;
    public bool Locked { get; set; }
    public bool ShowLoads { get; set; } = true;
    public bool ShowFreeMemory { get; set; } = true;
    public bool ShowDiskSpace { get; set; } = true;
    public bool ShowTemperatures { get; set; } = true;
    public bool ShowFans { get; set; } = true;
    public bool ShowIp { get; set; } = true;
    public bool ShowNetwork { get; set; } = true;
    public bool ShowDiskActivity { get; set; } = true;
    public bool ShowDiskHealth { get; set; } = true;
    public bool ShowDesStatus { get; set; } = true;
    public bool ColorThresholds { get; set; } = true;
    public int DiskTemperatureDefaultsVersion { get; set; }
    public string NetworkAdapterId { get; set; } = "";
    public Dictionary<string, ThresholdPair> TemperatureLimits { get; set; } = new()
    {
        ["CPU"] = new(80, 90), ["GPU"] = new(80, 90), ["MB"] = new(60, 75)
    };
    public Dictionary<string, ThresholdPair> DiskFreeLimits { get; set; } = new();

    public ThresholdPair GetTemperatureLimits(string key) =>
        TemperatureLimits.TryGetValue(key, out var value) ? value :
            key is "CPU" or "GPU" ? new(80, 90) : key == "MB" ? new(60, 75) : new(55, 60);
    public ThresholdPair GetFreeLimits(string letter) =>
        DiskFreeLimits.TryGetValue(letter, out var value) ? value : new(10, 5);
    public AppSettings Clone() => JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this))!;

    public void Normalize()
    {
        FontSize = double.IsFinite(FontSize) ? Math.Clamp(FontSize, 12, 30) : 18;
        BackgroundOpacity = Math.Clamp(BackgroundOpacity, 20, 100);
        UpdateIntervalMs = Math.Clamp(UpdateIntervalMs, 500, 5000);
        TemperatureLimits ??= new();
        DiskFreeLimits ??= new();
        NetworkAdapterId ??= "";
        if (DiskTemperatureDefaultsVersion < 1)
        {
            // Migrate only the old disk default; keep custom and CPU/GPU/board thresholds.
            foreach (var key in TemperatureLimits.Keys.ToArray())
                if (key.Length == 2 && char.IsLetter(key[0]) && key[1] == ':' && TemperatureLimits[key] == new ThresholdPair(50, 60))
                    TemperatureLimits[key] = new(55, 60);
            DiskTemperatureDefaultsVersion = 1;
        }
        foreach (var key in TemperatureLimits.Keys.ToArray())
        {
            var pair = TemperatureLimits[key];
            if (pair is null || !pair.IsValid(true)) TemperatureLimits.Remove(key);
        }
        foreach (var key in DiskFreeLimits.Keys.ToArray())
        {
            var pair = DiskFreeLimits[key];
            if (pair is null || !pair.IsValid(false)) DiskFreeLimits.Remove(key);
        }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public static string SettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OsdPanel", "settings.json");

    public static AppSettings Load(string? source = null)
    {
        try
        {
            var path = source ?? SettingsPath;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                settings.Normalize();
                return settings;
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save(string? destination = null)
    {
        try
        {
            var path = destination ?? SettingsPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch { }
    }
}

public sealed record ThresholdPair(double Warning, double Critical)
{
    public bool IsValid(bool temperature) => double.IsFinite(Warning) && double.IsFinite(Critical) &&
        (temperature ? Warning >= 0 && Critical <= 150 && Warning < Critical :
            Critical >= 0 && Warning <= 100 && Critical < Warning);
}

public static class Thresholds
{
    public static string Temperature(double? value, ThresholdPair pair, string normal, bool enabled) =>
        !enabled || !value.HasValue ? normal : value >= pair.Critical ? "#FF6464" : value >= pair.Warning ? "#FFAA40" : normal;
    public static string FreeSpace(double value, ThresholdPair pair, bool enabled) =>
        !enabled ? "#FFD700" : value <= pair.Critical ? "#FF6464" : value <= pair.Warning ? "#FFAA40" : "#FFD700";
}
