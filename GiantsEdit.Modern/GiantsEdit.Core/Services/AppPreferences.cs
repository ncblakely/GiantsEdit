using System.Text.Json;

namespace GiantsEdit.Core.Services;

/// <summary>
/// Persists user preferences to ~/GiantsEdit/preferences.json.
/// </summary>
public class AppPreferences
{
    private static readonly string PrefsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "GiantsEdit");

    private static readonly string PrefsFile = Path.Combine(PrefsDir, "preferences.json");

    public string GamePath { get; set; } = "";
    public string LastOpenFolder { get; set; } = "";

    public void Save()
    {
        Directory.CreateDirectory(PrefsDir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(PrefsFile, json);
    }

    public static AppPreferences Load()
    {
        if (!File.Exists(PrefsFile))
            return new AppPreferences();

        try
        {
            var json = File.ReadAllText(PrefsFile);
            return JsonSerializer.Deserialize<AppPreferences>(json) ?? new AppPreferences();
        }
        catch
        {
            return new AppPreferences();
        }
    }
}
