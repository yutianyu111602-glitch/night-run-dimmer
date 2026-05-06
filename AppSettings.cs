using System.Text.Json;

namespace NightRunDimmer;

internal sealed class AppSettings
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NightRunDimmer");

    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");

    public int DimPercent { get; set; } = 65;
    public bool VisualDimEnabled { get; set; }
    public bool HardwareBrightnessEnabled { get; set; }
    public bool PreventSleepEnabled { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
            return settings ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDirectory);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }
}
