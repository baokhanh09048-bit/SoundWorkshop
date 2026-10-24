using System.Reflection;
using System.Threading;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Audio;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Screens.Bestiary;

namespace MySfxMod;

public static class SoundRuntime
{
    private sealed class MonsterActionContext(MonsterModel monster, string bindingKey)
    {
        public MonsterModel Monster { get; } = monster;
        public string BindingKey { get; } = bindingKey;
        public bool CustomPlayed { get; set; }
    }
    private static readonly AsyncLocal<MonsterActionContext?> CurrentAction = new();
    private static readonly LinkedList<string> Recent = new();
    private static readonly Dictionary<string, LinkedListNode<string>> RecentNodes = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Queue<string>> RecentMonster = new(StringComparer.Ordinal);
    private static MonsterModel? _bestiaryPreviewMonster;
    private static string? _bestiaryPreviewAnim;
    private static string? _bestiaryPreviewMoveKey;
    private static long _bestiaryPreviewUntil;
    public static IEnumerable<string> RecentEvents => Recent.Where(path => path.StartsWith("event:/sfx/", StringComparison.Ordinal)).ToArray();
    public static IEnumerable<string> RecentSounds => Recent.ToArray();
    public static long RecentRevision { get; private set; }
    public static IEnumerable<string> RecentMonsterEvents(string monsterId) =>
        RecentMonster.TryGetValue(monsterId, out var events) ? events.Reverse() : [];
    private static readonly Dictionary<string, Queue<string>> RecentMonsterMoves = new(StringComparer.Ordinal);
    public static IEnumerable<string> RecentMonsterMoveEvents(MonsterModel monster, BestiaryMonsterMove move)
    {
        var key = monster.Id + "|" + GetMonsterMoveKey(move);
        return RecentMonsterMoves.TryGetValue(key, out var events) ? events.Reverse() : [];
    }
    public static IEnumerable<string> KnownEvents => typeof(FmodSfx).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => field.GetRawConstantValue() as string)
            .OfType<string>().Distinct(StringComparer.Ordinal);

    public static IEnumerable<string> KnownDebugSamples => KnownDebugFileNames()
        .Select(DebugKey)
        .Distinct(StringComparer.Ordinal);

    public static void Remember(string path)
    {
        if (!path.StartsWith("event:/sfx/", StringComparison.Ordinal)) return;
        RememberKey(path);
    }

    public static void RememberDebugSample(string streamName)
    {
        var fileName = NormalizeDebugFileName(streamName);
        if (fileName.Length == 0) return;
        RememberKey(DebugKey(fileName));
    }

    private static void RememberKey(string path)
    {
        if (RecentNodes.Remove(path, out var node)) Recent.Remove(node);
        RecentNodes[path] = Recent.AddFirst(path);
        RecentRevision++;
    }

    public static bool IsDebugKey(string key) => key.StartsWith("debug_audio/", StringComparison.Ordinal);

    public static string DebugKey(string streamName) => "debug_audio/" + NormalizeDebugFileName(streamName);

    public static string DebugFileName(string key) => IsDebugKey(key) ? key["debug_audio/".Length..] : key;

    public static string SoundKind(string key) => IsDebugKey(key) ? "debug" : "event";

    public static string DebugLabel(string keyOrFile)
    {
        var fileName = Path.GetFileNameWithoutExtension(DebugFileName(keyOrFile)).ToLowerInvariant();
        return fileName.Replace('_', ' ').Replace('-', ' ');
    }

    private static string NormalizeDebugFileName(string streamName)
    {
        var value = streamName.Replace('\\', '/').Trim();
        const string prefix = "res://debug_audio/";
        if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) value = value[prefix.Length..];
        if (value.StartsWith("debug_audio/", StringComparison.OrdinalIgnoreCase)) value = value["debug_audio/".Length..];
        return value.Contains('/') ? Path.GetFileName(value) : value;
    }

    private static IEnumerable<string> KnownDebugFileNames()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "lightning_orb_evoke.mp3", "lightning_orb_passive.mp3", "lightning_orb_channel.mp3",
            "frost_orb_evoke.mp3", "frost_orb_passive.mp3", "frost_orb_channel.mp3",
            "dark_orb_evoke.mp3", "dark_orb_passive.mp3", "dark_orb_channel.mp3",
            "plasma_orb_evoke.mp3", "plasma_orb_passive.mp3", "plasma_orb_channel.mp3"
        };
        foreach (var field in typeof(TmpSfx).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.IsLiteral && field.FieldType == typeof(string) && field.GetRawConstantValue() is string value && IsDebugFileName(value))
                files.Add(value);
        }
        if (typeof(TmpSfx).GetField("assetPaths", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is IEnumerable<string> paths)
            foreach (var path in paths.Select(NormalizeDebugFileName).Where(IsDebugFileName)) files.Add(path);
        return files;
    }

    private static bool IsDebugFileName(string value) =>
        value.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) || value.EndsWith(".wav", StringComparison.OrdinalIgnoreCase);

    private static void RememberMonsterEvent(MonsterModel monster, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("event:/", StringComparison.Ordinal)) return;
        var id = monster.Id.ToString();
        if (!RecentMonster.TryGetValue(id, out var events))
        {
            events = new Queue<string>();
            RecentMonster[id] = events;
        }
        if (events.Contains(path, StringComparer.Ordinal)) return;
        events.Enqueue(path);
        while (events.Count > 24) events.Dequeue();
    }

    private static void RememberMonsterMoveEvent(MonsterModel monster, string path)
    {
        if (string.IsNullOrWhiteSpace(_bestiaryPreviewMoveKey) || string.IsNullOrWhiteSpace(path)) return;
        var key = monster.Id + "|" + _bestiaryPreviewMoveKey;
        if (!RecentMonsterMoves.TryGetValue(key, out var events))
        {
            events = new Queue<string>();
            RecentMonsterMoves[key] = events;
        }
        if (events.Contains(path, StringComparer.Ordinal)) return;
        events.Enqueue(path);
        while (events.Count > 12) events.Dequeue();
    }

    public static string GetMonsterMoveKey(BestiaryMonsterMove move) =>
        move.displayName + "|" + (move.stateId ?? move.animId ?? (move.action != null ? "action" : move.nonStateMove != null ? "nonstate" : "move"));

    public static string GetMonsterActionBindingKey(MonsterModel monster, BestiaryMonsterMove move) =>
        monster.Id + "|" + GetMonsterActionToken(move);

    private static string GetMonsterActionToken(BestiaryMonsterMove move) =>
        move.stateId ?? move.animId ?? move.displayName;

    public static void BeginCombatMove(MonsterModel monster, MoveState move)
    {
        var context = new MonsterActionContext(monster, monster.Id + "|" + move.StateId);
        CurrentAction.Value = context;
        PlayActionBinding(context);
    }

    public static void EndActionContext() => CurrentAction.Value = null;

    public static bool PlayEvent(string path, float originalVolume)
    {
        Remember(path);
        var action = CurrentAction.Value;
        if (action != null && SoundStore.TryGet("monster_action", action.BindingKey, out _, out _))
            return SoundSettings.PlayAlongsideOriginal;
        if (_bestiaryPreviewMonster != null && System.Environment.TickCount64 <= _bestiaryPreviewUntil)
        {
            RememberMonsterEvent(_bestiaryPreviewMonster, path);
            RememberMonsterMoveEvent(_bestiaryPreviewMonster, path);
        }
        if (_bestiaryPreviewMonster != null && System.Environment.TickCount64 <= _bestiaryPreviewUntil)
        {
            if (_bestiaryPreviewAnim == "hurt" && path == _bestiaryPreviewMonster.TakeDamageSfx &&
                SoundStore.TryGet("monster_hit", _bestiaryPreviewMonster.Id.ToString(), out _, out _))
                return PlayMonsterBinding("monster_hit", _bestiaryPreviewMonster);
            if (_bestiaryPreviewAnim == "die" && path == _bestiaryPreviewMonster.DeathSfx &&
                SoundStore.TryGet("monster_death", _bestiaryPreviewMonster.Id.ToString(), out _, out _))
                return PlayMonsterBinding("monster_death", _bestiaryPreviewMonster);
        }
        if (SoundStore.TrySelect("event", path, out var binding, out var file))
        {
            return !PlayFile(file, originalVolume * binding.Volume) || SoundSettings.PlayAlongsideOriginal;
        }
        return true;
    }

    public static bool PlayDebugSample(string streamName, float originalVolume, PitchVariance variance, ref int result)
    {
        RememberDebugSample(streamName);
        var key = DebugKey(streamName);
        var action = CurrentAction.Value;
        if (action != null && SoundStore.TryGet("monster_action", action.BindingKey, out _, out _))
            return SoundSettings.PlayAlongsideOriginal;
        if (!SoundStore.TrySelect("debug", key, out var binding, out var file)) return true;
        if (!PlayFile(file, originalVolume * binding.Volume, DebugPitchScale(variance))) return true;
        result = 0;
        return SoundSettings.PlayAlongsideOriginal;
    }

    private static float DebugPitchScale(PitchVariance variance)
    {
        var amount = variance switch
        {
            PitchVariance.Small => 0.02f,
            PitchVariance.Medium => 0.05f,
            PitchVariance.Large => 0.1f,
            PitchVariance.TooMuch => 0.2f,
            _ => 0f
        };
        return amount == 0f ? 1f : 1f + (float)(GD.Randf() * 2f - 1f) * amount;
    }

    public static void PlayCue(string kind, string key, string legacyName)
    {
        if (SoundStore.TrySelect(kind, key, out var binding, out var file))
        {
            if (PlayFile(file, binding.Volume)) return;
        }
    }

    public static bool PlayMonsterBinding(string kind, MonsterModel? monster)
    {
        if (monster == null || !SoundStore.TrySelect(kind, monster.Id.ToString(), out var binding, out var file)) return true;
        return !PlayFile(file, binding.Volume) || SoundSettings.PlayAlongsideOriginal;
    }

    public static void BeginBestiaryPreview(IEnumerable<NCreature> creatures, BestiaryMonsterMove move)
    {
        var monster = creatures.FirstOrDefault()?.Entity.Monster;
        _bestiaryPreviewAnim = move.animId;
        _bestiaryPreviewMoveKey = GetMonsterMoveKey(move);
        _bestiaryPreviewMonster = monster;
        if (monster != null)
        {
            var context = new MonsterActionContext(monster, GetMonsterActionBindingKey(monster, move));
            CurrentAction.Value = context;
            PlayActionBinding(context);
        }
        _bestiaryPreviewUntil = System.Environment.TickCount64 + 3000;
    }

    private static void PlayActionBinding(MonsterActionContext context)
    {
        if (SoundStore.TrySelect("monster_action", context.BindingKey, out var binding, out var file))
            context.CustomPlayed = PlayFile(file, binding.Volume);
    }

    public static void EndBestiaryPreview()
    {
        // Bestiary state/action moves start an async task. Keep the preview context
        // briefly after PlayMoveAnim returns so its delayed SFX can be attributed.
        _bestiaryPreviewUntil = Math.Max(_bestiaryPreviewUntil, System.Environment.TickCount64 + 1500);
        EndActionContext();
    }

    public static bool PlayFile(string path, float volume, float pitchScale = 1f) =>
        Engine.GetMainLoop() is SceneTree tree && SoundPlayback.Start(tree.Root, path, volume, pitchScale) != null;
}

[HarmonyPatch(typeof(NAudioManager), nameof(NAudioManager.PlayOneShot), new[] { typeof(string), typeof(float) })]
public static class SimpleEventPatch
{
    private static bool Prefix(string path, float volume) => SoundRuntime.PlayEvent(path, volume);
}

[HarmonyPatch(typeof(NAudioManager), nameof(NAudioManager.PlayOneShot), new[] { typeof(string), typeof(Dictionary<string, float>), typeof(float) })]
public static class ParameterEventPatch
{
    private static bool Prefix(string path, float volume) => SoundRuntime.PlayEvent(path, volume);
}

[HarmonyPatch(typeof(NDebugAudioManager), nameof(NDebugAudioManager.Play), new[] { typeof(string), typeof(float), typeof(PitchVariance) })]
public static class DebugSamplePatch
{
    private static bool Prefix(string streamName, float volume, PitchVariance variance, ref int __result) =>
        SoundRuntime.PlayDebugSample(streamName, volume, variance, ref __result);
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.OnPlayWrapper))]
public static class CardCuePatch
{
    private static void Prefix(CardModel __instance) => SoundRuntime.PlayCue("card", __instance.Id.ToString(), __instance.GetType().Name);
}

[HarmonyPatch]
public static class RelicCuePatch
{
    private static IEnumerable<MethodBase> TargetMethods() => new MethodBase[] { typeof(RelicModel).GetMethod("AfterObtained", BindingFlags.Instance | BindingFlags.Public)! }
        .Concat(typeof(RelicModel).Assembly.GetTypes()
        .Where(t => t.IsSubclassOf(typeof(RelicModel)))
        .Select(t => t.GetMethod("AfterObtained", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        .OfType<MethodBase>());

    private static void Prefix(RelicModel __instance) => SoundRuntime.PlayCue("relic", __instance.Id.ToString(), __instance.GetType().Name);
}

[HarmonyPatch(typeof(SfxCmd), nameof(SfxCmd.PlayDamage), new[] { typeof(MonsterModel), typeof(int) })]
public static class MonsterHitSoundPatch
{
    private static bool Prefix(MonsterModel? monster) => SoundRuntime.PlayMonsterBinding("monster_hit", monster);
}

[HarmonyPatch(typeof(SfxCmd), nameof(SfxCmd.PlayDeath), new[] { typeof(MonsterModel) })]
public static class MonsterDeathSoundPatch
{
    private static bool Prefix(MonsterModel? monster) => SoundRuntime.PlayMonsterBinding("monster_death", monster);
}

[HarmonyPatch(typeof(NBestiary), "PlayMoveAnim")]
public static class BestiaryPreviewSoundPatch
{
    private static void Prefix(IEnumerable<NCreature> creatures, BestiaryMonsterMove move) => SoundRuntime.BeginBestiaryPreview(creatures, move);
    private static void Postfix() => SoundRuntime.EndBestiaryPreview();
}

[HarmonyPatch(typeof(MonsterModel), nameof(MonsterModel.PerformMove))]
public static class MonsterActionSoundPatch
{
    private static void Prefix(MonsterModel __instance) => SoundRuntime.BeginCombatMove(__instance, __instance.NextMove);
    private static void Postfix() => SoundRuntime.EndActionContext();
}
