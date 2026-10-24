using System.Text.Json;
using Godot;

namespace MySfxMod;

public sealed class SoundPreferencesData
{
    public int Format { get; set; } = 1;
    public bool PlayAlongsideOriginal { get; set; }
    public bool EnableSoundPacks { get; set; } = true;
    public float MasterVolume { get; set; } = 1f;
    public string LastAudioDirectory { get; set; } = "";
    public string LastPackDirectory { get; set; } = "";
}

public static class SoundSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private static readonly string Home = ProjectSettings.GlobalizePath("user://MySfxMod");
    private static readonly string SettingsPath = Path.Combine(Home, "settings.json");
    private static SoundPreferencesData _data = new();

    public static bool PlayAlongsideOriginal => _data.PlayAlongsideOriginal;
    public static bool EnableSoundPacks => _data.EnableSoundPacks;
    public static float MasterVolume => _data.MasterVolume;
    public static string LastAudioDirectory => ExistingDirectory(_data.LastAudioDirectory);
    public static string LastPackDirectory => ExistingDirectory(_data.LastPackDirectory);
    public static event Action? Changed;

    public static void Initialize()
    {
        _data = new SoundPreferencesData();
        if (File.Exists(SettingsPath))
        {
            try
            {
                var loaded = JsonSerializer.Deserialize<SoundPreferencesData>(File.ReadAllText(SettingsPath), JsonOptions);
                if (loaded?.Format == 1)
                {
                    loaded.MasterVolume = Math.Clamp(loaded.MasterVolume, 0.25f, 2f);
                    _data = loaded;
                }
            }
            catch (Exception e) { GD.PrintErr($"[MySfxMod] Cannot read playback settings: {e}"); }
            return;
        }

        MigrateLegacySettings();
        Save();
    }

    public static void SetPlayAlongsideOriginal(bool value)
    {
        if (_data.PlayAlongsideOriginal == value) return;
        _data.PlayAlongsideOriginal = value;
        SaveAndNotify();
    }

    public static void SetEnableSoundPacks(bool value)
    {
        if (_data.EnableSoundPacks == value) return;
        _data.EnableSoundPacks = value;
        SaveAndNotify();
    }

    public static void SetMasterVolume(float value)
    {
        value = Math.Clamp(value, 0.25f, 2f);
        if (Math.Abs(_data.MasterVolume - value) < 0.001f) return;
        _data.MasterVolume = value;
        SaveAndNotify();
    }

    public static void ResetPlayback(bool disableSoundPacks = false)
    {
        _data = new SoundPreferencesData
        {
            EnableSoundPacks = !disableSoundPacks,
            LastAudioDirectory = _data.LastAudioDirectory,
            LastPackDirectory = _data.LastPackDirectory
        };
        SaveAndNotify();
    }

    public static void RememberAudioDirectory(string filePath) => RememberDirectory(filePath, directory => _data.LastAudioDirectory = directory);

    public static void RememberPackDirectory(string filePath) => RememberDirectory(filePath, directory => _data.LastPackDirectory = directory);

    private static void RememberDirectory(string filePath, Action<string> setter)
    {
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;
            setter(directory);
            Save();
        }
        catch (Exception e) { GD.PrintErr($"[MySfxMod] Cannot remember file dialog directory: {e}"); }
    }

    private static string ExistingDirectory(string path) =>
        !string.IsNullOrWhiteSpace(path) && Directory.Exists(path) ? path : "";

    private static void MigrateLegacySettings()
    {
        var legacy = Path.Combine(ProjectSettings.GlobalizePath("user://"), "mod_configs", "MySfxMod.cfg");
        if (!File.Exists(legacy)) return;
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(legacy));
            if (json.RootElement.TryGetProperty("PlayAlongsideOriginal", out var alongside) && bool.TryParse(alongside.GetString(), out var playBoth))
                _data.PlayAlongsideOriginal = playBoth;
            if (json.RootElement.TryGetProperty("EnableSoundPacks", out var packs) && bool.TryParse(packs.GetString(), out var enablePacks))
                _data.EnableSoundPacks = enablePacks;
        }
        catch (Exception e) { GD.PrintErr($"[MySfxMod] Cannot migrate legacy playback settings: {e}"); }
    }

    private static void SaveAndNotify()
    {
        Save();
        Changed?.Invoke();
    }

    private static void Save()
    {
        Directory.CreateDirectory(Home);
        var pending = SettingsPath + ".tmp";
        try
        {
            File.WriteAllText(pending, JsonSerializer.Serialize(_data, JsonOptions));
            File.Move(pending, SettingsPath, true);
        }
        finally { if (File.Exists(pending)) File.Delete(pending); }
    }
}
