using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Audio;

namespace MySfxMod;

public sealed record BgmTrack(string Key, string Label, bool IsRunMusic, string Scope = "", string? EncounterType = null, string? NativeEvent = null);

/// <summary>Catalogues every official music event and replaces the event currently selected by the game.</summary>
public static class BgmRuntime
{
    public const string MainMenuKey = "event:/music/menu_update";
    public const string LegacyRunKey = "run";

    public static readonly IReadOnlyList<BgmTrack> Tracks =
    [
        new(MainMenuKey, "主菜单", false),
        new("event:/music/act1_a1_v1", "第一幕 · 密林 A", true),
        new("event:/music/act1_a2_v2", "第一幕 · 密林 B", true),
        new("event:/music/act1_b1_v1", "第一幕 · 暗港", true,
            "适用范围：暗港场景。乐加维林族母未单独绑定时沿用此曲；为族母单独绑定后，只替换族母战斗的音乐。"),
        new("event:/music/act2_a1_v2", "第二幕 · 巢穴 A", true),
        new("event:/music/act2_a2_v2", "第二幕 · 巢穴 B", true),
        new("event:/music/act3_a1_v1", "第三幕 · 荣耀 A", true),
        new("event:/music/act3_a2_v1", "第三幕 · 荣耀 B", true),
        new("boss/SOUL_FYSH_BOSS", "首领 · 灵魂异鱼", true, EncounterType: "SoulFyshBoss", NativeEvent: "event:/music/act1_b_boss_soul_fysh"),
        new("boss/WATERFALL_GIANT_BOSS", "首领 · 瀑布巨兽", true, EncounterType: "WaterfallGiantBoss", NativeEvent: "event:/music/act1_b_boss_waterfall_giant"),
        new("boss/LAGAVULIN_MATRIARCH_BOSS", "首领 · 乐加维林族母", true,
            "只替换乐加维林族母的整场战斗。未单独绑定时沿用暗港场景音乐；绑定后不会影响暗港的其他战斗。", "LagavulinMatriarchBoss"),
        new("boss/CEREMONIAL_BEAST_BOSS", "首领 · 仪式兽", true, EncounterType: "CeremonialBeastBoss", NativeEvent: "event:/music/act1_boss_ceremonial_beast"),
        new("boss/THE_KIN_BOSS", "首领 · 同族小队", true, EncounterType: "TheKinBoss", NativeEvent: "event:/music/act1_boss_the_kin"),
        new("boss/VANTOM_BOSS", "首领 · 墨影幻灵", true, EncounterType: "VantomBoss", NativeEvent: "event:/music/act1_boss_vantom"),
        new("boss/KAISER_CRAB_BOSS", "首领 · 帝皇蟹", true, EncounterType: "KaiserCrabBoss", NativeEvent: "event:/music/act2_boss_kaiser_crab"),
        new("boss/KNOWLEDGE_DEMON_BOSS", "首领 · 知识恶魔", true, EncounterType: "KnowledgeDemonBoss", NativeEvent: "event:/music/act2_boss_knowledge_demon"),
        new("boss/THE_INSATIABLE_BOSS", "首领 · 无厌沙虫", true, EncounterType: "TheInsatiableBoss", NativeEvent: "event:/music/act2_boss_the_insatiable"),
        new("boss/QUEEN_BOSS", "首领 · 女王", true,
            "只替换女王的整场战斗，不影响永世沙漏。两者原版音乐共用入口，在这里可分别绑定。", "QueenBoss", "event:/music/act3_boss_queen"),
        new("boss/AEONGLASS_BOSS", "首领 · 永世沙漏", true,
            "只替换永世沙漏的整场战斗，不影响女王。两者原版音乐共用入口，在这里可分别绑定。", "AeonglassBoss", "event:/music/act3_boss_queen"),
        new("boss/TEST_SUBJECT_BOSS", "首领 · 实验体", true, EncounterType: "TestSubjectBoss", NativeEvent: "event:/music/act3_boss_test_subject")
    ];

    public static readonly IReadOnlyList<BgmTrack> LegacyBossTracks = Tracks.Where(t => t.NativeEvent != null)
        .GroupBy(t => t.NativeEvent!, StringComparer.Ordinal)
        .Select(group => new BgmTrack(group.Key, "旧版 · " + string.Join("／", group.Select(t => t.Label[5..])), true,
            "旧版共用绑定，适用于：" + string.Join("、", group.Select(t => t.Label[5..])) + "。各首领的独立绑定优先；移除此项会影响所有仍继承它的首领。"))
        .ToArray();
    private static readonly HashSet<string> KnownKeys = Tracks.Select(t => t.Key).Concat(LegacyBossTracks.Select(t => t.Key)).ToHashSet(StringComparer.Ordinal);
    private static readonly FieldInfo? RunTrackField = AccessTools.Field(typeof(NRunMusicController), "_currentTrack");
    private static readonly FieldInfo? RunProxyField = AccessTools.Field(typeof(NRunMusicController), "_proxy");
    private static AudioStreamPlayer? _player;
    private static string? _activeEvent;
    private static string? _activePath;
    private static string? _activePlaylistStamp;
    private static string? _currentEvent;
    private static string? _baseRunEvent;
    private static BgmTrack? _bossTrack;
    private static string? _bossNativeEvent;
    private static string? _currentNativeEvent;
    private static NRunMusicController? _runController;
    private static bool _currentIsRun;
    private static bool _nativeSuppressed;
    private static bool _restoring;
    private static bool _initialized;
    private static float _gameBgmVolume = 1f;
    private static float _gameMasterVolume = 1f;

    public static float GameVolumeScale => Math.Clamp(_gameBgmVolume * _gameMasterVolume, 0f, 1f);
    public static bool IsKnownKey(string key) => key == LegacyRunKey || KnownKeys.Contains(key);
    private static BgmTrack? TrackFor(string key) => Tracks.Concat(LegacyBossTracks).FirstOrDefault(t => t.Key == key);
    public static string LabelFor(string key) => key == LegacyRunKey ? "旧版通用 · 游戏内音乐" : TrackFor(key)?.Label ?? key;
    public static bool IsBossKey(string key) => Tracks.Any(t => t.Key == key && t.EncounterType != null);
    public static string ScopeFor(string key)
    {
        if (key == LegacyRunKey) return "适用范围：所有没有单独绑定的游戏内音乐，包括场景音乐和首领战；不包括主菜单。";
        var track = TrackFor(key);
        if (track == null) return "";
        if (track.Scope.Length > 0) return track.Scope;
        if (!track.IsRunMusic) return "适用范围：主菜单。";
        return track.Label.StartsWith("首领 · ", StringComparison.Ordinal)
            ? "只替换" + track.Label[5..] + "的整场首领战，不影响其他首领。"
            : "适用范围：" + track.Label + "对应的场景音乐；首领专属音乐在各自项目中设置。";
    }
    public static IReadOnlyList<string> RunKeys => Tracks.Where(t => t.IsRunMusic).Select(t => t.Key).ToArray();
    public static IReadOnlyList<string> LegacyRunKeys => Tracks.Where(t => t.IsRunMusic && t.EncounterType == null).Select(t => t.Key)
        .Concat(LegacyBossTracks.Select(t => t.Key)).ToArray();
    public static bool IsRunKey(string key) => TrackFor(key)?.IsRunMusic == true;

    public static bool TryGetBinding(string key, out SoundBinding binding, out string path, out bool inherited)
    {
        inherited = false;
        if (SoundStore.TryGet("bgm", key, out binding, out path)) return true;
        if (TrackFor(key) is { EncounterType: not null } boss)
        {
            var fallback = boss.NativeEvent ?? "event:/music/act1_b1_v1";
            if (SoundStore.TryGet("bgm", fallback, out binding, out path))
            {
                inherited = true;
                return true;
            }
        }
        if (IsRunKey(key) && SoundStore.TryGet("bgm", LegacyRunKey, out binding, out path))
        {
            inherited = true;
            return true;
        }
        binding = null!;
        path = "";
        return false;
    }

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        SoundStore.MigrateLegacyBgmBindings(MainMenuKey, LegacyRunKeys);
        SoundStore.Changed += RefreshCurrent;
        SoundSettings.Changed += RefreshCurrent;
        CombatManager.Instance.CombatSetUp += state => OnEncounterSelected(state.Encounter);
        CombatManager.Instance.CombatEnded += _ => OnCombatEnded();
    }

    public static void OnEncounterSelected(EncounterModel? encounter)
    {
        if (_bossTrack != null) OnCombatEnded();
        // Resolve the encounter itself, so bosses sharing native audio can be configured independently.
        _bossTrack = encounter == null ? null : Tracks.FirstOrDefault(t =>
            t.EncounterType == encounter.GetType().Name && encounter.GetType().Namespace == "MegaCrit.Sts2.Core.Models.Encounters");
        _bossNativeEvent = _bossTrack?.NativeEvent;
    }

    public static void OnCombatBegan()
    {
        if (_bossTrack == null) return;
        _runController ??= NRunMusicController.Instance;
        _baseRunEvent ??= _runController == null ? null : ReadRunTrack(_runController);
        _currentEvent = _bossTrack.Key;
        _currentNativeEvent = _bossNativeEvent ?? _baseRunEvent;
        _currentIsRun = true;
        TryStartCurrent();
    }

    public static void OnCombatEnded()
    {
        if (_bossTrack == null) return;
        _bossTrack = null;
        _bossNativeEvent = null;
        _currentEvent = _baseRunEvent;
        _currentNativeEvent = _baseRunEvent;
        _currentIsRun = true;
        if (_currentEvent != null && TryStartCurrent()) return;
        var suppressed = _nativeSuppressed;
        StopPlayer();
        if (suppressed) RestoreNativeCurrent();
    }

    public static bool AllowMenuMusic(string music)
    {
        if (_restoring || !IsKnownKey(music) || IsRunKey(music)) return true;
        _currentEvent = music;
        _currentNativeEvent = music;
        _bossTrack = null;
        _bossNativeEvent = null;
        _currentIsRun = false;
        _baseRunEvent = null;
        if (TryStartCurrent()) return false;
        StopPlayer();
        _nativeSuppressed = false;
        return true;
    }

    public static void OnRunMusicUpdated(NRunMusicController controller)
    {
        _runController = controller;
        var music = ReadRunTrack(controller);
        if (string.IsNullOrWhiteSpace(music)) return;
        _baseRunEvent = music;
        _currentEvent = _bossTrack?.Key ?? music;
        _currentNativeEvent = _bossNativeEvent ?? music;
        _currentIsRun = true;
        if (!TryStartCurrent() && _activeEvent != null)
        {
            StopPlayer();
            _nativeSuppressed = false;
        }
    }

    public static bool AllowEncounterMusic(NRunMusicController controller, string music)
    {
        if (_restoring) return true;
        _runController = controller;
        if (_bossTrack == null && !IsKnownKey(music))
        {
            StopPlayer();
            _currentEvent = music;
            _currentNativeEvent = music;
            _currentIsRun = true;
            _nativeSuppressed = false;
            return true;
        }

        _currentEvent = _bossTrack?.Key ?? music;
        if (_bossTrack != null) _bossNativeEvent = music;
        _currentNativeEvent = music;
        _currentIsRun = true;
        if (TryStartCurrent()) return false;
        if (_activeEvent != null) StopPlayer();
        _nativeSuppressed = false;
        return true;
    }

    public static bool AllowRunMusicRestore(NRunMusicController controller)
    {
        _runController = controller;
        if (_restoring) return true;
        _bossTrack = null;
        _bossNativeEvent = null;
        var baseMusic = ReadRunTrack(controller) ?? _baseRunEvent;
        _currentEvent = baseMusic;
        _currentNativeEvent = baseMusic;
        _currentIsRun = true;
        if (baseMusic != null && TryStartCurrent()) return false;
        StopPlayer();
        _nativeSuppressed = false;
        return true;
    }

    public static void EnsureRunMusic(NRunMusicController controller)
    {
        _runController = controller;
        // Both stable and beta call UpdateTrack when combat starts, including encounters
        // without native boss music. CombatBegan exists only on beta; referencing that
        // event prevents the entire initializer from loading on stable.
        if (_bossTrack != null) OnCombatBegan();
        if (_currentIsRun && _activeEvent != null) StopNativeRunMusic(controller);
    }

    public static void OnRunMusicStopped()
    {
        StopPlayer();
        _currentEvent = null;
        _baseRunEvent = null;
        _bossTrack = null;
        _bossNativeEvent = null;
        _currentNativeEvent = null;
        _runController = null;
        _currentIsRun = false;
        _nativeSuppressed = false;
    }

    public static void SetGameBgmVolume(float volume)
    {
        _gameBgmVolume = MathF.Pow(Math.Clamp(volume, 0f, 1f), 2f);
        UpdateActiveVolume();
    }

    public static void SetGameMasterVolume(float volume)
    {
        _gameMasterVolume = MathF.Pow(Math.Clamp(volume, 0f, 1f), 2f);
        UpdateActiveVolume();
    }

    /// <summary>Immediately reapplies the binding for the music that is playing now.</summary>
    public static void RefreshCurrent()
    {
        if (_restoring) return;
        DetectCurrentContext();
        if (_currentEvent != null && TryStartCurrent()) return;
        if (_activeEvent == null) return;
        StopPlayer();
        RestoreNativeCurrent();
    }

    private static void DetectCurrentContext()
    {
        if (_bossTrack != null)
        {
            _currentEvent = _bossTrack.Key;
            _currentNativeEvent = _bossNativeEvent ?? _baseRunEvent;
            _currentIsRun = true;
            return;
        }
        if (_currentEvent != null) return;
        var controller = _runController ?? NRunMusicController.Instance;
        if (controller != null && ReadRunTrack(controller) is { Length: > 0 } runMusic)
        {
            _runController = controller;
            _baseRunEvent = runMusic;
            _currentEvent = runMusic;
            _currentNativeEvent = runMusic;
            _currentIsRun = true;
            return;
        }

        // The workshop is otherwise opened from the persistent main-menu layer.
        _currentEvent = MainMenuKey;
        _currentNativeEvent = MainMenuKey;
        _currentIsRun = false;
    }

    private static bool TryStartCurrent()
    {
        if (_currentEvent == null || !TryGetBinding(_currentEvent, out var binding, out var firstFile, out _)) return false;
        var stamp = binding.Key + Path.GetDirectoryName(firstFile) + SoundStore.PlaylistStamp(binding);
        if (_activeEvent == _currentEvent && _activePlaylistStamp == stamp && GodotObject.IsInstanceValid(_player) && _player!.Playing)
        {
            UpdateActiveVolume(binding.Volume);
            if (_currentIsRun && !StopNativeRunMusic(_runController)) return false;
            _nativeSuppressed = true;
            return true;
        }
        if (!SoundStore.TrySelect("bgm", binding.Key, out binding, out var file)) return false;
        return StartCustom(_currentEvent, binding, file, _currentIsRun, stamp);
    }

    private static bool StartCustom(string music, SoundBinding binding, string file, bool isRun, string stamp)
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return false;
        var multiple = binding.AudioClips.Count > 1;
        var replacement = SoundPlayback.Start(tree.Root, file, binding.Volume, loop: !multiple, isBgm: true);
        if (replacement == null) return false;

        StopPlayer();
        _player = replacement;
        _activeEvent = music;
        _activePath = file;
        _activePlaylistStamp = stamp;
        var stopped = isRun ? StopNativeRunMusic(_runController) : StopNativeMenuMusic();
        if (!stopped)
        {
            StopPlayer();
            Log.Warn($"[MySfxMod] Could not stop native music for {music}; replacement was cancelled.");
            return false;
        }
        _nativeSuppressed = true;
        if (multiple) replacement.Finished += () =>
        {
            if (_player != replacement || _currentEvent != music) return;
            if (!TryStartCurrent()) { StopPlayer(); RestoreNativeCurrent(); }
        };
        Log.Info($"[MySfxMod] Replaced BGM {music} with {Path.GetFileName(file)}.");
        return true;
    }

    private static bool StopNativeMenuMusic()
    {
        if (NAudioManager.Instance is not { } manager) return false;
        manager.StopMusic();
        return true;
    }

    private static bool StopNativeRunMusic(NRunMusicController? controller)
    {
        controller ??= NRunMusicController.Instance;
        if (controller == null) return false;
        try
        {
            if (RunProxyField?.GetValue(controller) is not Node proxy || !GodotObject.IsInstanceValid(proxy)) return false;
            proxy.Call("stop_music");
            return true;
        }
        catch (Exception e)
        {
            Log.Warn($"[MySfxMod] Could not stop native run music: {e.Message}");
            return false;
        }
    }

    private static string? ReadRunTrack(NRunMusicController controller) => RunTrackField?.GetValue(controller) as string;

    private static void RestoreNativeCurrent()
    {
        if (!_nativeSuppressed || _currentEvent == null) return;
        _nativeSuppressed = false;
        _restoring = true;
        try
        {
            if (!_currentIsRun)
            {
                NAudioManager.Instance?.PlayMusic(_currentEvent);
                return;
            }

            var controller = _runController ?? NRunMusicController.Instance;
            if (controller == null) return;
            if (_currentNativeEvent == _baseRunEvent) controller.StopCustomMusic();
            else if (_currentNativeEvent != null) controller.PlayCustomMusic(_currentNativeEvent);
        }
        finally { _restoring = false; }
    }

    private static void UpdateActiveVolume(float? bindingVolume = null)
    {
        if (!GodotObject.IsInstanceValid(_player) || _activeEvent == null) return;
        var volume = bindingVolume;
        if (!volume.HasValue && TryGetBinding(_activeEvent, out var binding, out _, out _)) volume = binding.Volume;
        if (volume.HasValue)
            _player!.VolumeDb = Mathf.LinearToDb(Math.Max(0.0001f, SoundPlayback.EffectiveGain(volume.Value, isBgm: true)));
    }

    private static void StopPlayer()
    {
        if (GodotObject.IsInstanceValid(_player))
        {
            _player!.Stop();
            if (!_player.IsQueuedForDeletion()) _player.QueueFree();
        }
        _player = null;
        _activeEvent = null;
        _activePath = null;
        _activePlaylistStamp = null;
    }
}

[HarmonyPatch(typeof(NAudioManager), nameof(NAudioManager.PlayMusic), new[] { typeof(string) })]
public static class CustomMenuMusicPatch
{
    private static bool Prefix(string music) => BgmRuntime.AllowMenuMusic(music);
}

[HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.UpdateMusic))]
public static class CustomRunMusicPatch
{
    private static void Postfix(NRunMusicController __instance) => BgmRuntime.OnRunMusicUpdated(__instance);
}

[HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.PlayCustomMusic), new[] { typeof(string) })]
public static class CustomEncounterMusicPatch
{
    private static bool Prefix(NRunMusicController __instance, string customMusic) => BgmRuntime.AllowEncounterMusic(__instance, customMusic);
}

[HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.StopCustomMusic))]
public static class RestoreRunMusicPatch
{
    private static bool Prefix(NRunMusicController __instance) => BgmRuntime.AllowRunMusicRestore(__instance);
}

[HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.UpdateTrack), new Type[] { })]
public static class KeepCustomRunMusicPatch
{
    private static void Postfix(NRunMusicController __instance) => BgmRuntime.EnsureRunMusic(__instance);
}

[HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.StopMusic))]
public static class StopCustomRunMusicPatch
{
    private static void Postfix() => BgmRuntime.OnRunMusicStopped();
}

[HarmonyPatch(typeof(NAudioManager), nameof(NAudioManager.SetBgmVol), new[] { typeof(float) })]
public static class CustomBgmVolumePatch
{
    private static void Postfix(float volume) => BgmRuntime.SetGameBgmVolume(volume);
}

[HarmonyPatch(typeof(NAudioManager), nameof(NAudioManager.SetMasterVol), new[] { typeof(float) })]
public static class CustomBgmMasterVolumePatch
{
    private static void Postfix(float volume) => BgmRuntime.SetGameMasterVolume(volume);
}
