using System.Text.Json;
using System.Text.Json.Serialization;

namespace GiantsEdit.Core.Services;

/// <summary>
/// Camera/input control scheme.
/// </summary>
public enum ControlScheme
{
    /// <summary>UE5-style: RMB+drag=look, MMB=pan, scroll=zoom, RMB+WASD=fly.</summary>
    Default,
    /// <summary>Original Delphi editor: LMB=rotate, RMB=pan, LMB+RMB=zoom.</summary>
    Classic
}

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

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ControlScheme ControlScheme { get; set; } = ControlScheme.Default;

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
