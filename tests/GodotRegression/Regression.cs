using System.Reflection;
using System.IO.Compression;
using System.Text.Json;
using Godot;
using MySfxMod;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;

public partial class RunMusicProxyProbe : Node
{
    public int StopCalls { get; private set; }
    public string? NativeMusic { get; private set; }
    public void stop_music() => StopCalls++;
    public void update_music(string music) => NativeMusic = music;
    public void update_global_parameter(string parameter, int value) { }
}

public partial class Regression : Node
{
    private int _checks;
    private void Check(bool ok, string name)
    {
        if (!ok) throw new Exception(name);
        _checks++;
        GD.Print("PASS " + name);
    }
    private static T Field<T>(object o, string name) => (T)o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(o)!;
    private static void Call(object o, string name) => o.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(o, null);
    private static void MigrateBgm() => typeof(SoundStore).GetMethod("MigrateLegacyBgmBindings", BindingFlags.NonPublic | BindingFlags.Static)!
        .Invoke(null, [BgmRuntime.MainMenuKey, BgmRuntime.LegacyRunKeys]);
    private async Task Delay(double seconds) => await ToSignal(GetTree().CreateTimer(seconds, true), SceneTreeTimer.SignalName.Timeout);

    public override async void _Ready()
    {
        if (OS.GetCmdlineUserArgs().Contains("--startup-check"))
        {
            try { await StartupCheck(); GD.Print($"STARTUP PASS {_checks} checks"); GetTree().Quit(); }
            catch (Exception e) { GD.PrintErr("STARTUP FAIL " + e); GetTree().Quit(1); }
            return;
        }
        if (OS.GetCmdlineUserArgs().Contains("--visual-check"))
        {
            try { await VisualCheck(); GetTree().Quit(); }
            catch (Exception e) { GD.PrintErr(e); GetTree().Quit(1); }
            return;
        }
        try { await Run(); GD.Print($"RESULT PASS {_checks} checks"); GetTree().Quit(); }
        catch (Exception e) { GD.PrintErr("RESULT FAIL " + e); GetTree().Quit(1); }
    }

    private async Task StartupCheck()
    {
        await Delay(0.05);
        Check(ProjectSettings.GlobalizePath("user://").Contains("SoundWorkshopRegression"), "isolated startup user data");
        src.Scripts.Entry.Init();
        await Delay(0.1);
        var patchTypes = typeof(BgmRuntime).Assembly.GetTypes().Where(type =>
            type.IsDefined(typeof(HarmonyLib.HarmonyPatch), false)).ToArray();
        foreach (var type in patchTypes)
        {
            var callbacks = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method => method.Name is "Prefix" or "Postfix").ToHashSet();
            Check(HarmonyLib.Harmony.GetAllPatchedMethods().Any(method =>
            {
                var info = HarmonyLib.Harmony.GetPatchInfo(method);
                return info.Prefixes.Concat(info.Postfixes).Any(patch =>
                    patch.owner == "sts2.llxs.MySfxMod" && callbacks.Contains(patch.PatchMethod));
            }), "initializer installs " + type.Name);
        }
        Check(GetTree().Root.GetNodeOrNull<CanvasLayer>("SoundWorkshopLauncher") != null,
            "initializer installs persistent workshop shortcut");
    }

    private async Task VisualCheck()
    {
        await Delay(0.1);
        SoundSettings.Initialize();
        SoundSettings.SetEnableSoundPacks(false);
        SoundStore.Initialize();
        SoundRuntime.Remember("event:/sfx/ui_click");
        SoundRuntime.Remember("event:/sfx/block_gain");
        SoundWorkshop.Open();
        var workshop = (SoundWorkshop)typeof(SoundWorkshop).GetField("_open", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var target = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "artifacts", "sound-workshop-ui"));
        Directory.CreateDirectory(target);
        async Task Capture(string name)
        {
            await Delay(0.3);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var picture = GetViewport().GetTexture().GetImage();
            picture.SavePng(Path.Combine(target, name + ".png"));
        }
        await Capture("empty");
        Field<ItemList>(workshop, "_items").Select(0);
        Field<ItemList>(workshop, "_items").EmitSignal(ItemList.SignalName.ItemSelected, 0L);
        await Capture("editor");
        var list = Field<ItemList>(workshop, "_items");
        Check(list.Size.Y > 350, "editor list has room for multiple rows");
        GetTree().Root.Size = new Vector2I(900, 600);
        await Capture("editor-small");
        GetTree().Root.Size = new Vector2I(1280, 800);
        var clip = ProjectSettings.GlobalizePath("user://tone.wav");
        SoundStore.Set("event", "event:/sfx/block_gain", clip, 1);
        Call(workshop, "RefreshList");
        await Capture("editor-bound");
        Field<Button>(workshop, "_boundOnly").ButtonPressed = true;
        await Capture("editor-bound-filter");
        GetTree().Root.Size = new Vector2I(900, 600);
        await Capture("editor-bound-filter-small");
        GetTree().Root.Size = new Vector2I(1280, 800);
        Field<Button>(workshop, "_boundOnly").ButtonPressed = false;
        Field<Button>(workshop, "_packTab").ButtonPressed = true;
        Field<Button>(workshop, "_packTab").EmitSignal(Button.SignalName.Pressed);
        await Capture("packs");
        GetTree().Root.Size = new Vector2I(900, 600);
        await Capture("packs-small");
        GetTree().Root.Size = new Vector2I(1280, 800);
        Field<Button>(workshop, "_settingsTab").ButtonPressed = true;
        Field<Button>(workshop, "_settingsTab").EmitSignal(Button.SignalName.Pressed);
        await Capture("settings");
        GetTree().Root.Size = new Vector2I(900, 600);
        await Capture("settings-small");
        GetTree().Root.Size = new Vector2I(1280, 800);
        Field<Button>(workshop, "_bgmTab").ButtonPressed = true;
        Field<Button>(workshop, "_bgmTab").EmitSignal(Button.SignalName.Pressed);
        Check(Field<ItemList>(workshop, "_bgmSlots").ItemCount == 20, "BGM page presents every scene and 12 independent bosses");
        await Capture("bgm");
        var bgmSlots = Field<ItemList>(workshop, "_bgmSlots");
        foreach (var (key, name) in new[] { ("boss/QUEEN_BOSS", "bgm-queen"), ("boss/AEONGLASS_BOSS", "bgm-aeonglass"), ("boss/LAGAVULIN_MATRIARCH_BOSS", "bgm-matriarch") })
        {
            var index = BgmRuntime.Tracks.ToList().FindIndex(track => track.Key == key);
            bgmSlots.Select(index);
            bgmSlots.EmitSignal(ItemList.SignalName.ItemSelected, (long)index);
            await Capture(name);
            GetTree().Root.Size = new Vector2I(900, 600);
            await Capture(name + "-small");
            GetTree().Root.Size = new Vector2I(1280, 800);
        }
        var demos = new[] { "Sword Slash.wav", "Heavy Strike.wav", "Echo.wav" }.Select(name =>
        {
            var path = ProjectSettings.GlobalizePath("user://" + name); File.Copy(clip, path, true); return path;
        }).ToArray();
        SoundStore.Set("event", "event:/sfx/attack_fire", demos[0], 1);
        SoundStore.AddFiles("event", "event:/sfx/attack_fire", demos[1..]);
        SoundStore.SetMode("event", "event:/sfx/attack_fire", "random");
        SoundStore.SetWeight("event", "event:/sfx/attack_fire", 1, 2);
        SoundStore.SetWeight("event", "event:/sfx/attack_fire", 2, 7);
        var playlistDialog = new SoundPlaylistDialog("event", "event:/sfx/attack_fire", "attack fire", 1, () => { });
        GetTree().Root.AddChild(playlistDialog);
        await Capture("playlist-random");
        Field<OptionButton>(playlistDialog, "_mode").EmitSignal(OptionButton.SignalName.ItemSelected, 0L);
        GetTree().Root.Size = new Vector2I(900, 600);
        await Capture("playlist-sequential-small");
        playlistDialog.Close();
        workshop.Close();
        await Delay(0.1);
        GD.Print("VISUAL CHECK saved to " + target);
    }

    private async Task Run()
    {
        await Delay(0.05);
        Check(ProjectSettings.GlobalizePath("user://").Contains("SoundWorkshopRegression"), "isolated user data");
        SoundSettings.Initialize();
        SoundSettings.ResetPlayback();
        Check(!SoundSettings.PlayAlongsideOriginal && SoundSettings.EnableSoundPacks && SoundSettings.MasterVolume == 1,
            "playback defaults replace original, enable packs and use 1.0 master volume");
        var rememberedDir = ProjectSettings.GlobalizePath("user://remembered-audio");
        Directory.CreateDirectory(rememberedDir);
        SoundSettings.RememberAudioDirectory(Path.Combine(rememberedDir, "sample.wav"));
        SoundSettings.Initialize();
        Check(Path.GetFullPath(SoundSettings.LastAudioDirectory) == Path.GetFullPath(rememberedDir), "audio picker remembers its last directory across reload");
        SoundSettings.SetEnableSoundPacks(false);
        SoundStore.Initialize();
        foreach (var existing in SoundStore.LocalBindings.ToArray()) SoundStore.Remove(existing.Kind, existing.Key);
        MigrateBgm();
        Check(!BgmRuntime.Tracks.Any(track => SoundStore.TryGet("bgm", track.Key, out _, out _)),
            "fresh library leaves every official music event unbound");
        var clip = ProjectSettings.GlobalizePath("user://tone.wav");
        using (var writer = new BinaryWriter(File.Create(clip)))
        {
            const int frames = 44100 * 3;
            writer.Write("RIFF"u8); writer.Write(36 + frames * 2); writer.Write("WAVEfmt "u8);
            writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(44100);
            writer.Write(88200); writer.Write((short)2); writer.Write((short)16);
            writer.Write("data"u8); writer.Write(frames * 2);
            for (int i = 0; i < frames; i++) writer.Write((short)(Math.Sin(i * Math.Tau * 440 / 44100) * 200));
        }
        SoundStore.Set("card", "regression_bound", clip, 2.45f);
        SoundStore.Set("debug", SoundRuntime.DebugKey("lightning_orb_evoke.mp3"), clip, 1.15f);
        SoundStore.Set("bgm", BgmRuntime.MainMenuKey, clip, 1.3f);
        SoundRuntime.RememberDebugSample("lightning_orb_evoke.mp3");
        Check(SoundRuntime.RecentSounds.First() == "debug_audio/lightning_orb_evoke.mp3", "debug audio samples enter recent list");
        Check(SoundRuntime.DebugLabel("debug_audio/lightning_orb_evoke.mp3") == "lightning orb evoke", "orb debug sample uses the unified English label style");
        Check(SoundStore.TryGet("debug", "debug_audio/lightning_orb_evoke.mp3", out var debugBinding, out _) && Math.Abs(debugBinding.Volume - 1.15f) < 0.001f,
            "debug audio bindings use the same import and volume store");
        Check(SoundStore.TryGet("bgm", BgmRuntime.MainMenuKey, out var bgmBinding, out _) && Math.Abs(bgmBinding.Volume - 1.3f) < 0.001f,
            "menu BGM uses the shared personal audio store");
        SoundStore.Remove("card", "regression_empty");
        var config = ProjectSettings.GlobalizePath("user://MySfxMod/sounds.json");
        var before = File.ReadAllText(config);
        var preferences = ProjectSettings.GlobalizePath("user://MySfxMod/settings.json");
        SoundSettings.SetPlayAlongsideOriginal(true);
        SoundSettings.SetMasterVolume(1.35f);
        Check(File.ReadAllText(preferences).Contains("\"PlayAlongsideOriginal\": true"), "playback mode saved outside BaseLib");
        SoundSettings.Initialize();
        Check(SoundSettings.PlayAlongsideOriginal && !SoundSettings.EnableSoundPacks && Math.Abs(SoundSettings.MasterVolume - 1.35f) < 0.001f,
            "playback settings and master volume persist after reload");
        SoundSettings.ResetPlayback();
        Check(!SoundSettings.PlayAlongsideOriginal && SoundSettings.EnableSoundPacks && SoundSettings.MasterVolume == 1 && File.ReadAllText(config) == before,
            "playback reset restores defaults without touching bindings");
        SoundSettings.SetEnableSoundPacks(false);
        var slots = new List<(string Kind, string Key, string Label)>
        {
            ("card", "regression_bound", "已绑定"), ("card", "regression_empty", "未绑定")
        };
        var dialog = (ObjectBindingDialog)Activator.CreateInstance(typeof(ObjectBindingDialog), BindingFlags.NonPublic | BindingFlags.Instance,
            null, new object[] { "回归测试", slots, "验证默认音量、窗口焦点与完整试听。" }, null)!;
        bool wasEmbedded = GetTree().Root.GuiEmbedSubwindows;
        GetTree().Root.AddChild(dialog);
        await Delay(0.2);
        Check(dialog.GetViewport() == GetTree().Root, "editor uses main viewport, no secondary window");
        var volume = Field<SpinBox>(dialog, "_volume");
        var picker = Field<OptionButton>(dialog, "_slotPicker");
        var preview = Field<SoundPreviewButton>(dialog, "_preview");
        Check(Math.Abs(volume.Value - 2.45) < 0.0001, "legacy fractional volume preserved");
        picker.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
        Check(volume.Value == 1, "unbound slot is exactly 1.0");
        Check(preview.Disabled, "unbound preview disabled");
        picker.EmitSignal(OptionButton.SignalName.ItemSelected, 0L);
        Check(File.ReadAllText(config) == before, "opening/switching UI never writes settings");
        volume.Value = 1.25;
        Check(SoundStore.TryGet("card", "regression_bound", out var binding, out _) && binding.Volume == 1.25f, "user volume edit saved");
        SoundWorkshop.Open();
        var workshop = (SoundWorkshop)typeof(SoundWorkshop).GetField("_open", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        Field<Button>(workshop, "_settingsTab").EmitSignal(Button.SignalName.Pressed);
        Check(Field<ScrollContainer>(workshop, "_settingsScroll").Visible && Field<Button>(workshop, "_replaceOriginal").ButtonPressed,
            "workshop exposes playback settings and selected mode");
        Field<Button>(workshop, "_bgmTab").EmitSignal(Button.SignalName.Pressed);
        var bgmSlots = Field<ItemList>(workshop, "_bgmSlots");
        Check(bgmSlots.ItemCount == 20 && bgmSlots.GetItemText(0).Contains("主菜单") && bgmSlots.GetItemText(8).Contains("首领"),
            "BGM page shows Chinese menu, act and boss names");
        Field<Button>(workshop, "_settingsTab").EmitSignal(Button.SignalName.Pressed);
        var masterVolume = Field<SpinBox>(workshop, "_masterVolume");
        masterVolume.Value = 1.2;
        Check(Math.Abs(SoundSettings.MasterVolume - 1.2f) < 0.001f, "workshop master volume applies immediately");
        SoundSettings.SetMasterVolume(1);
        Field<Button>(workshop, "_playTogether").EmitSignal(Button.SignalName.Pressed);
        Check(SoundSettings.PlayAlongsideOriginal, "workshop playback mode applies immediately");
        Field<Button>(workshop, "_editTab").EmitSignal(Button.SignalName.Pressed);
        var onlyBound = Field<Button>(workshop, "_boundOnly");
        var search = Field<LineEdit>(workshop, "_search");
        SoundStore.Set("event", "event:/sfx/filter_bound_alpha", clip, 1);
        SoundStore.Set("event", "event:/sfx/filter_bound_beta", clip, 1);
        SoundRuntime.Remember("event:/sfx/filter_bound_alpha");
        SoundRuntime.Remember("event:/sfx/filter_unbound");
        SoundRuntime.Remember("event:/sfx/filter_bound_beta");
        onlyBound.ButtonPressed = true;
        var filtered = Field<List<string>>(workshop, "_visible");
        Check(filtered.Contains("event:/sfx/filter_bound_alpha") && filtered.Contains("event:/sfx/filter_bound_beta") &&
              !filtered.Contains("event:/sfx/filter_unbound") && filtered.All(key => SoundStore.TryGet(SoundRuntime.SoundKind(key), key, out _, out _)),
            "bound-only filter hides unbound sounds and retains every active matching binding");
        Check(filtered.IndexOf("event:/sfx/filter_bound_beta") < filtered.IndexOf("event:/sfx/filter_bound_alpha"),
            "bound-only filtering preserves most recently played order");
        search.Text = "filter bound alpha";
        search.EmitSignal(LineEdit.SignalName.TextChanged, search.Text);
        Check(filtered.SequenceEqual(new[] { "event:/sfx/filter_bound_alpha" }), "search text and bound-only toggle apply together");
        Field<ItemList>(workshop, "_items").EmitSignal(ItemList.SignalName.ItemSelected, 0L);
        Field<Button>(workshop, "_reset").EmitSignal(Button.SignalName.Pressed);
        Check(filtered.Count == 0 && Field<string>(workshop, "_key") == "" && !Field<VBoxContainer>(workshop, "_details").Visible,
            "unbinding a filtered selection removes it and clears the stale editor");
        search.Text = "";
        search.EmitSignal(LineEdit.SignalName.TextChanged, search.Text);
        onlyBound.ButtonPressed = false;
        Check(filtered.Contains("event:/sfx/filter_unbound"), "turning off bound-only restores unbound sounds");
        SoundStore.Remove("event", "event:/sfx/filter_bound_beta");
        Call(workshop, "RefreshList");
        var workVolume = Field<SpinBox>(workshop, "_volume");
        workVolume.SetValueNoSignal(4);
        Call(workshop, "RefreshSelection");
        Check(workVolume.Value == 1, "workshop resets unbound volume");
        SoundRuntime.Remember("event:/sfx/regression_z");
        SoundRuntime.Remember("event:/sfx/regression_a");
        SoundRuntime.Remember("event:/sfx/regression_z");
        await Delay(0.4);
        var visible = Field<List<string>>(workshop, "_visible");
        Check(visible.IndexOf("event:/sfx/regression_z") < visible.IndexOf("event:/sfx/regression_a"), "repeat sound moves ahead of older sound");
        Check(visible.Count(v => v == "event:/sfx/regression_z") == 1, "recent sounds deduplicated");
        var regressionIndex = visible.IndexOf("event:/sfx/regression_z");
        Field<ItemList>(workshop, "_items").EmitSignal(ItemList.SignalName.ItemSelected, (long)regressionIndex);
        SoundStore.Set("event", "event:/sfx/regression_z", clip, 1);
        Call(workshop, "RefreshSelection");
        var workPreview = Field<SoundPreviewButton>(workshop, "_preview");
        workPreview.EmitSignal(Button.SignalName.Pressed);
        SoundRuntime.Remember("event:/sfx/regression_b");
        await Delay(0.4);
        Check(Field<string>(workshop, "_key") == "event:/sfx/regression_z" && workPreview.Text == "停止试听", "live reordering preserves selected item and preview");
        for (int i = 0; i < 45; i++) SoundRuntime.Remember("event:/sfx/regression_" + i);
        Check(SoundRuntime.RecentEvents.Contains("event:/sfx/regression_z"), "older detected sound retains temporal order past forty items");
        workshop.Close();
        await Delay(0.1);
        Check(GetTree().Root.GuiEmbedSubwindows, "nested editor close preserves picker embedding");
        var chooser = Field<FileDialog>(dialog, "_chooser");
        chooser.PopupCentered();
        await Delay(0.1);
        Check(chooser.IsEmbedded(), "file chooser embedded");
        chooser.Hide();

        // Capture actual rendered samples, including the tail, while the scene is paused.
        var capture = new AudioEffectCapture { BufferLength = 5 };
        AudioServer.AddBusEffect(0, capture);
        GetTree().Paused = true;
        preview.EmitSignal(Button.SignalName.Pressed);
        await Delay(0.25);
        var player = preview.GetChildren().OfType<AudioStreamPlayer>().Single();
        Check(player.Playing, "preview starts while scene paused");
        Check(Math.Abs(player.VolumeLinear - 3.4375) < 0.001, "1.25x uses the louder calibrated base gain");
        Check(Math.Abs(SoundPlayback.EffectiveGain(1, isBgm: true) - 2) < 0.001, "BGM 1.0x uses the raised default gain");
        await Delay(1.4);
        Check(player.Playing && player.GetPlaybackPosition() > 1.2, "preview survives beyond focus fade interval");
        capture.ClearBuffer();
        await Delay(0.6);
        var samples = capture.GetBuffer(capture.GetFramesAvailable());
        Check(samples.Length > 0 && samples.Any(s => Math.Abs(s.X) > 0.001), "audible samples remain in clip tail");
        await Delay(1.0);
        Check(preview.Text == "试听" && !GodotObject.IsInstanceValid(player), "preview finishes and releases player");
        preview.EmitSignal(Button.SignalName.Pressed);
        await Delay(0.1);
        preview.EmitSignal(Button.SignalName.Pressed);
        await Delay(0.1);
        Check(!preview.GetChildren().OfType<AudioStreamPlayer>().Any(), "stop button releases player");
        preview.EmitSignal(Button.SignalName.Pressed);
        await Delay(0.1);
        picker.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
        await Delay(0.1);
        Check(!preview.GetChildren().OfType<AudioStreamPlayer>().Any(), "switching slots stops stale preview");
        GetTree().Paused = false;
        AudioServer.RemoveBusEffect(0, 0);
        dialog.Close();
        await Delay(0.1);
        Check(GetTree().Root.GuiEmbedSubwindows == wasEmbedded, "closing all editors restores viewport flag");
        var zipPath = SoundStore.Export("Regression", "Test");
        using var zip = ZipFile.OpenRead(zipPath);
        var packManifest = zip.Entries.Single(e => e.FullName.EndsWith(".json") && !e.FullName.EndsWith("soundpack.json"));
        using var manifestReader = new StreamReader(packManifest.Open());
        var manifestText = await manifestReader.ReadToEndAsync();
        Check(manifestText.Contains("\"min_version\": \"0.8.0\"", StringComparison.Ordinal), "exported sound packs declare the playlist compatibility minimum");
        Check(zip.Entries.Any(e => e.FullName.EndsWith("soundpack.json")) && zip.Entries.Any(e => e.FullName.EndsWith(".wav")), "export includes bindings and audio");
        var exportedPack = zip.Entries.Single(e => e.FullName.EndsWith("soundpack.json"));
        using (var exportedReader = new StreamReader(exportedPack.Open()))
            Check((await exportedReader.ReadToEndAsync()).Contains("\"Kind\": \"bgm\"", StringComparison.Ordinal), "export includes BGM bindings");
        SoundStore.Set("card", "only_before_import", clip, 0.65f);
        var imported = SoundStore.Import(zipPath);
        Check(!SoundStore.TryGet("card", "only_before_import", out _, out _), "ZIP replaces entire personal library");
        Check(SoundStore.TryGet("card", "regression_bound", out var importedBinding, out var importedFile) && importedBinding.Volume == 1.25f && File.Exists(importedFile), "ZIP restores bindings, audio and volume immediately");
        Check(SoundStore.TryGet("bgm", BgmRuntime.MainMenuKey, out var importedBgm, out var importedBgmFile) && importedBgm.Volume == 1.3f && File.Exists(importedBgmFile),
            "ZIP restores menu BGM binding and audio");
        Check(File.Exists(imported.Backup), "import creates restorable ZIP backup");
        SoundStore.Import(imported.Backup);
        Check(SoundStore.TryGet("card", "only_before_import", out var restored, out _) && restored.Volume == 0.65f, "backup ZIP restores previous library");
        var unchanged = File.ReadAllText(config);
        var assetDir = ProjectSettings.GlobalizePath("user://MySfxMod/assets");
        var assetCount = Directory.GetFiles(assetDir).Length;
        var badPath = ProjectSettings.GlobalizePath("user://broken.zip");
        void MakeBad(string type)
        {
            using var archive = new ZipArchive(File.Create(badPath), ZipArchiveMode.Create);
            var sounds = new List<SoundBinding> { new() { Kind = "card", Key = "bad", File = "assets/clip.wav", Volume = 1 } };
            if (type == "duplicate") sounds.Add(sounds[0]);
            if (type == "traversal") sounds[0].File = "assets/../escape.wav";
            using (var writer = new StreamWriter(archive.CreateEntry("Pack/soundpack.json").Open()))
                writer.Write(JsonSerializer.Serialize(new SoundPackData { Sounds = sounds }));
            if (type != "missing")
            {
                if (type == "corrupt") { using var writer = new StreamWriter(archive.CreateEntry("Pack/assets/clip.wav").Open()); writer.Write("invalid wav"); }
                else archive.CreateEntryFromFile(clip, "Pack/assets/clip.wav");
            }
        }
        foreach (var type in new[] { "missing", "duplicate", "traversal", "corrupt" })
        {
            MakeBad(type);
            var rejected = false;
            try { SoundStore.Import(badPath); } catch (InvalidDataException) { rejected = true; }
            Check(rejected && File.ReadAllText(config) == unchanged && Directory.GetFiles(assetDir).Length == assetCount,
                "invalid ZIP " + type + " leaves library and resources intact");
        }
        SoundWorkshop.Open();
        var importWorkshop = (SoundWorkshop)typeof(SoundWorkshop).GetField("_open", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var packChooser = Field<FileDialog>(importWorkshop, "_packChooser");
        packChooser.PopupCentered();
        await Delay(0.1);
        Check(packChooser.IsEmbedded(), "ZIP picker stays inside game viewport");
        packChooser.Hide();
        packChooser.EmitSignal(FileDialog.SignalName.FileSelected, zipPath);
        Check(Field<Label>(importWorkshop, "_status").Text.Contains("即时生效"), "ZIP picker applies library and displays success");
        Check(!SoundStore.TryGet("card", "only_before_import", out _, out _), "UI import performs full replacement");
        Field<Button>(importWorkshop, "_settingsTab").EmitSignal(Button.SignalName.Pressed);
        var countBeforeInitialize = SoundStore.LocalBindings.Count;
        Field<Button>(importWorkshop, "_initialize").EmitSignal(Button.SignalName.Pressed);
        await Delay(0.1);
        var confirmation = Field<ConfirmationDialog>(importWorkshop, "_initializeConfirm");
        Check(confirmation.Visible && SoundStore.LocalBindings.Count == countBeforeInitialize,
            "one-click initialization asks before destructive cleanup");
        confirmation.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
        Check(SoundStore.LocalBindings.Count == 0 && Directory.GetFiles(assetDir).Length == 0,
            "one-click initialization removes every personal binding and copied audio file");
        Check(!SoundSettings.PlayAlongsideOriginal && !SoundSettings.EnableSoundPacks && SoundSettings.MasterVolume == 1,
            "one-click initialization restores playback defaults and disables installed packs");
        Check(File.Exists(zipPath) && File.Exists(imported.Backup), "initialization preserves exports and backups");
        Check(Field<Label>(importWorkshop, "_status").Text.Contains("初始化完成"), "initialization reports cleanup result");
        importWorkshop.Close();
        await Delay(0.1);

        SoundWorkshopLauncher.Initialize();
        SoundWorkshopLauncher.Initialize();
        await Delay(0.1);
        var launcher = GetTree().Root.GetNode<CanvasLayer>("SoundWorkshopLauncher");
        var launcherButton = launcher.GetNode<Button>("ViewportAnchor/SoundWorkshopButton");
        Check(launcher.GetParent() == GetTree().Root && launcherButton.Text == "♫" && launcherButton.OffsetTop == 106,
            "workshop shortcut lives on the persistent viewport layer");
        Check(GetTree().Root.GetChildren().Count(n => n.Name == "SoundWorkshopLauncher") == 1,
            "persistent workshop shortcut is never duplicated");
        var transientScene = new Control { Name = "SimulatedMapOrCombat" };
        GetTree().Root.AddChild(transientScene);
        transientScene.Free();
        Check(GodotObject.IsInstanceValid(launcher) && launcherButton.Visible,
            "workshop shortcut survives map and combat scene replacement");
        SoundWorkshop.Open();
        Check(!launcherButton.Visible, "persistent shortcut hides while workshop is open");
        var finalWorkshop = (SoundWorkshop)typeof(SoundWorkshop).GetField("_open", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        finalWorkshop.Close();
        await Delay(0.1);
        Check(launcherButton.Visible, "persistent shortcut returns when workshop closes");
        launcher.Free();

        SoundStore.Set("bgm", "event:/music/act1_a1_v1", clip, 1f);
        var runController = new NRunMusicController();
        var runProxy = new RunMusicProxyProbe();
        typeof(NRunMusicController).GetField("_proxy", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(runController, runProxy);
        typeof(NRunMusicController).GetField("_currentTrack", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(runController, "event:/music/act1_a1_v1");
        BgmRuntime.OnRunMusicUpdated(runController);
        await Delay(0.1);
        var activeBgm = (AudioStreamPlayer?)typeof(BgmRuntime).GetField("_player", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
        Check(runProxy.StopCalls > 0 && GodotObject.IsInstanceValid(activeBgm) && activeBgm!.Playing,
            "run BGM binding stops the native proxy and starts a looping replacement player");
        BgmRuntime.OnRunMusicStopped();
        await Delay(0.05);
        runController.Free();
        runProxy.Free();
        SoundStore.ClearPersonalLibrary();

        // A 0.7.0 installation stored one generic run binding. Keep it as a fallback.
        var legacyAsset = Path.Combine(assetDir, "legacy.wav");
        File.Copy(clip, legacyAsset, true);
        File.WriteAllText(config, JsonSerializer.Serialize(new SoundPackData
        {
            Sounds = [new SoundBinding { Kind = "bgm", Key = "run", File = "assets/legacy.wav", Volume = 0.9f }]
        }));
        SoundStore.Initialize();
        MigrateBgm();
        Check(SoundStore.LocalBindings.Count(b => b.Kind == "bgm") == 1 &&
              SoundStore.TryGet("bgm", BgmRuntime.LegacyRunKey, out _, out _) &&
              BgmRuntime.RunKeys.All(key => !SoundStore.TryGet("bgm", key, out _, out _)) &&
              BgmRuntime.TryGetBinding(BgmRuntime.RunKeys[0], out _, out _, out var inherited) && inherited,
            "legacy generic run BGM works as one inherited binding without marking every track bound");
        var legacyController = new NRunMusicController();
        var legacyProxy = new RunMusicProxyProbe();
        typeof(NRunMusicController).GetField("_proxy", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(legacyController, legacyProxy);
        typeof(NRunMusicController).GetField("_currentTrack", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(legacyController, BgmRuntime.RunKeys[0]);
        BgmRuntime.OnRunMusicUpdated(legacyController);
        await Delay(0.1);
        var legacyPlayer = (AudioStreamPlayer?)typeof(BgmRuntime).GetField("_player", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
        Check(legacyProxy.StopCalls > 0 && GodotObject.IsInstanceValid(legacyPlayer) && legacyPlayer!.Playing,
            "inherited run music actually replaces the native track");
        BgmRuntime.OnRunMusicStopped();
        legacyController.Free();
        legacyProxy.Free();
        SoundWorkshop.Open();
        var legacyWorkshop = (SoundWorkshop)typeof(SoundWorkshop).GetField("_open", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        Field<Button>(legacyWorkshop, "_bgmTab").EmitSignal(Button.SignalName.Pressed);
        var legacySlots = Field<ItemList>(legacyWorkshop, "_bgmSlots");
        Check(legacySlots.ItemCount == 21 && legacySlots.GetItemText(0).Contains("旧版通用") &&
              !legacySlots.GetItemText(2).Contains("●"), "legacy fallback is a single visible row; official tracks remain unbound");
        legacyWorkshop.Close();
        await Delay(0.1);
        SoundStore.ClearPersonalLibrary();

        // Repair the exact 0.7.1 expansion while keeping an untouched backup.
        File.Copy(clip, legacyAsset, true);
        File.WriteAllText(config, JsonSerializer.Serialize(new SoundPackData
        {
            Sounds = BgmRuntime.LegacyRunKeys.Select(key => new SoundBinding
                { Kind = "bgm", Key = key, File = "assets/legacy.wav", Volume = 0.9f }).ToList()
        }));
        var backupsBefore = Directory.GetFiles(SoundStore.BackupDirectory, "BGM迁移前_*.json").Length;
        SoundStore.Initialize();
        MigrateBgm();
        Check(SoundStore.LocalBindings.Count(b => b.Kind == "bgm") == 1 &&
              SoundStore.TryGet("bgm", BgmRuntime.LegacyRunKey, out _, out _) &&
              Directory.GetFiles(SoundStore.BackupDirectory, "BGM迁移前_*.json").Length == backupsBefore + 1,
            "0.7.1 expanded BGM bindings collapse to one fallback with a backup");
        MigrateBgm();
        Check(Directory.GetFiles(SoundStore.BackupDirectory, "BGM迁移前_*.json").Length == backupsBefore + 1,
            "BGM migration is idempotent on later launches");
        SoundStore.ClearPersonalLibrary();

        // A deliberately customized event must never be erased by the repair.
        File.Copy(clip, legacyAsset, true);
        File.WriteAllText(config, JsonSerializer.Serialize(new SoundPackData
        {
            Sounds = BgmRuntime.LegacyRunKeys.Select((key, index) => new SoundBinding
                { Kind = "bgm", Key = key, File = "assets/legacy.wav", Volume = index == 0 ? 1.1f : 0.9f }).ToList()
        }));
        SoundStore.Initialize();
        MigrateBgm();
        Check(SoundStore.LocalBindings.Count(b => b.Kind == "bgm") == BgmRuntime.LegacyRunKeys.Count &&
              !SoundStore.TryGet("bgm", BgmRuntime.LegacyRunKey, out _, out _),
            "migration leaves individually adjusted event bindings untouched");
        SoundStore.ClearPersonalLibrary();
        await RunPlaylists(clip, config, assetDir);
        await RunBossMusic(clip);
    }

    private async Task RunBossMusic(string clip)
    {
        SoundStore.ClearPersonalLibrary();
        BgmRuntime.OnRunMusicStopped();
        BgmRuntime.Initialize();
        var controller = new NRunMusicController();
        var proxy = new RunMusicProxyProbe();
        typeof(NRunMusicController).GetField("_proxy", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(controller, proxy);
        typeof(NRunMusicController).GetField("_currentTrack", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(controller, "event:/music/act1_b1_v1");
        string? ActivePath() => (string?)typeof(BgmRuntime).GetField("_activePath", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
        AudioStreamPlayer? Player() => (AudioStreamPlayer?)typeof(BgmRuntime).GetField("_player", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
        CombatState StateFor(BgmTrack track)
        {
            // Only the native encounter type is needed here; do not register canonical models in this fixture.
            var encounterType = typeof(EncounterModel).Assembly.GetType("MegaCrit.Sts2.Core.Models.Encounters." + track.EncounterType)!;
            var encounter = (EncounterModel)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(encounterType);
            var state = new CombatState();
            typeof(CombatState).GetField("_encounter", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(state, encounter);
            return state;
        }
        void Raise(string eventName, object? argument) =>
            ((Delegate?)typeof(CombatManager).GetField(eventName, BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(CombatManager.Instance))?.DynamicInvoke([argument]);
        var bosses = BgmRuntime.Tracks.Where(track => track.EncounterType != null).ToArray();
        Check(bosses.Length == 12 && bosses.All(track => typeof(EncounterModel).Assembly.GetType(
            "MegaCrit.Sts2.Core.Models.Encounters." + track.EncounterType) != null), "every installed boss has an independent music slot");
        foreach (var boss in bosses)
        {
            SoundStore.Set("bgm", boss.Key, clip, 1);
            SoundStore.TryGet("bgm", boss.Key, out _, out var expected);
            BgmRuntime.OnRunMusicUpdated(controller);
            var state = StateFor(boss);
            Raise("CombatSetUp", state);
            if (boss.NativeEvent != null)
                Check(!BgmRuntime.AllowEncounterMusic(controller, boss.NativeEvent), "native BGM is replaced for " + boss.EncounterType);
            // Native stable and beta both update the run track at combat start.
            BgmRuntime.EnsureRunMusic(controller);
            Check(ActivePath() == expected && GodotObject.IsInstanceValid(Player()) && Player()!.Playing,
                "combat events start independent playback for " + boss.EncounterType);
            var activePlayer = Player();
            BgmRuntime.EnsureRunMusic(controller);
            BgmRuntime.OnRunMusicUpdated(controller);
            Check(Player() == activePlayer && ActivePath() == expected, "room and stage updates preserve the boss playlist for " + boss.EncounterType);
            Raise("CombatEnded", null);
            Check(Player() == null, "leaving the boss stops its independent playback for " + boss.EncounterType);
        }
        var queen = bosses.Single(track => track.EncounterType == "QueenBoss");
        var aeonglass = bosses.Single(track => track.EncounterType == "AeonglassBoss");
        SoundStore.TryGet("bgm", queen.Key, out _, out var queenFile);
        SoundStore.TryGet("bgm", aeonglass.Key, out _, out var aeonglassFile);
        Check(queenFile != aeonglassFile, "queen and aeonglass retain different audio resources despite sharing native music");
        BgmRuntime.OnRunMusicUpdated(controller);
        Raise("CombatSetUp", StateFor(queen));
        Check(!BgmRuntime.AllowEncounterMusic(controller, "event:/music/a_future_shared_native_boss_track") && ActivePath() == queenFile,
            "boss identity selects its binding even when the native music path is not in the catalog");
        Raise("CombatEnded", null);
        var oldKey = queen.NativeEvent!;
        SoundStore.Set("bgm", oldKey, clip, 0.8f);
        Check(BgmRuntime.TryGetBinding(queen.Key, out var direct, out _, out var inherited) && !inherited && direct.Key == queen.Key,
            "independent boss binding overrides the old shared event binding");
        SoundStore.Remove("bgm", queen.Key);
        Check(BgmRuntime.TryGetBinding(queen.Key, out var fallback, out _, out inherited) && inherited && fallback.Key == oldKey &&
            BgmRuntime.TryGetBinding(aeonglass.Key, out direct, out _, out inherited) && !inherited && direct.Key == aeonglass.Key,
            "removing one boss binding inherits the old event without changing the other boss");
        SoundStore.Remove("bgm", oldKey);
        BgmRuntime.OnRunMusicUpdated(controller);
        var queenState = StateFor(queen);
        Raise("CombatSetUp", queenState);
        Check(BgmRuntime.AllowEncounterMusic(controller, oldKey), "unbound queen leaves native music intact instead of using aeonglass audio");
        BgmRuntime.EnsureRunMusic(controller);
        Check(Player() == null, "unbound boss does not start a custom player");
        Raise("CombatEnded", null);

        const string sceneKey = "event:/music/act1_b1_v1";
        SoundStore.Set("bgm", sceneKey, clip, 1);
        SoundStore.TryGet("bgm", sceneKey, out _, out var sceneFile);
        var matriarch = bosses.Single(track => track.EncounterType == "LagavulinMatriarchBoss");
        SoundStore.TryGet("bgm", matriarch.Key, out _, out var matriarchFile);
        BgmRuntime.OnRunMusicUpdated(controller);
        var matriarchState = StateFor(matriarch);
        Raise("CombatSetUp", matriarchState);
        BgmRuntime.EnsureRunMusic(controller);
        Check(ActivePath() == matriarchFile && matriarchFile != sceneFile, "matriarch custom music overrides the scene only during its own combat");
        Raise("CombatEnded", null);
        Check(ActivePath() == sceneFile, "ending matriarch combat restores the configured scene playlist");

        var zipPath = SoundStore.Export("BossMusicRegression", "Test");
        using (var zip = ZipFile.OpenRead(zipPath))
        using (var reader = new StreamReader(zip.Entries.Single(entry => entry.FullName.EndsWith(".json") && !entry.FullName.EndsWith("soundpack.json")).Open()))
            Check((await reader.ReadToEndAsync()).Contains("\"min_version\": \"0.8.2\""), "boss audio packs require the version that supports independent boss playback");
        BgmRuntime.OnRunMusicStopped();
        SoundStore.ClearPersonalLibrary();
        SoundStore.Import(zipPath);
        Check(bosses.Where(boss => boss != queen).All(boss => SoundStore.TryGet("bgm", boss.Key, out _, out _)),
            "ZIP sharing preserves independently configured boss slots");
        BgmRuntime.OnRunMusicStopped();
        SoundStore.ClearPersonalLibrary();
        controller.Free(); proxy.Free();
    }

    private async Task RunPlaylists(string clip, string config, string assetDir)
    {
        // Old local data and ZIPs still become one-item lists without losing volume.
        File.Copy(clip, Path.Combine(assetDir, "old.wav"), true);
        File.WriteAllText(config, "{\"Format\":1,\"Sounds\":[{\"Kind\":\"card\",\"Key\":\"old\",\"File\":\"assets/old.wav\",\"Volume\":1.3}]}");
        SoundStore.Initialize();
        Check(SoundStore.TryGet("card", "old", out var legacy, out _) && legacy.AudioClips.Count == 1 && legacy.Volume == 1.3f,
            "format-1 single binding loads as one item with original volume");
        var oldZip = ProjectSettings.GlobalizePath("user://old-sound-pack.zip");
        if (File.Exists(oldZip)) File.Delete(oldZip);
        using (var zip = ZipFile.Open(oldZip, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("Pack/soundpack.json").Open()))
                writer.Write(File.ReadAllText(config));
            zip.CreateEntryFromFile(clip, "Pack/assets/old.wav");
        }
        SoundStore.Import(oldZip);
        Check(SoundStore.TryGet("card", "old", out legacy, out _) && legacy.AudioClips.Count == 1 && legacy.Volume == 1.3f,
            "format-1 ZIP imports without losing the single audio binding");
        SoundStore.AddFiles("card", "old", [clip, clip]);
        var paths = SoundStore.GetPlaylist("card", "old").Select(p => p.Path).ToArray();
        Check(paths.Length == 3 && SoundStore.TryGet("card", "old", out var expanded, out _) && expanded.Volume == 1.3f,
            "adding multiple files extends an existing binding and preserves volume");
        var picks = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            SoundStore.TryGet("card", "old", out _, out _); // Repeated UI reads must not consume anything.
            SoundStore.TrySelect("card", "old", out _, out var path); picks.Add(path);
        }
        Check(picks.SequenceEqual(new[] { paths[0], paths[1], paths[2], paths[0], paths[1] }),
            "ordered triggers cycle exactly once per trigger, independent of UI reads");
        SoundStore.SetVolume("card", "old", 1.2f);
        SoundStore.TrySelect("card", "old", out _, out var afterVolume);
        Check(afterVolume == paths[2], "volume edits do not restart the sequential cursor");
        SoundStore.MoveClip("card", "old", 2, 0);
        SoundStore.TrySelect("card", "old", out _, out var afterMove);
        Check(afterMove == paths[2], "reordering changes the first item of the next cycle");
        var saved = File.ReadAllText(config);
        var count = Directory.GetFiles(assetDir).Length;
        var failed = false;
        try { SoundStore.AddFiles("card", "old", [clip, "missing-file.wav"]); } catch (FileNotFoundException) { failed = true; }
        Check(failed && File.ReadAllText(config) == saved && Directory.GetFiles(assetDir).Length == count,
            "failed multi-file import preserves the list and removes all newly copied files");

        foreach (var kind in new[] { "event", "card" })
        {
            var key = kind == "event" ? "event:/sfx/playlist_regression" : "playlist_card";
            SoundStore.AddFiles(kind, key, [clip, clip]);
            var items = SoundStore.GetPlaylist(kind, key);
            if (kind == "event") SoundRuntime.PlayEvent(key, 1);
            else SoundRuntime.PlayCue(kind, key, "");
            SoundStore.TrySelect(kind, key, out _, out var next);
            Check(next == items[1].Path, kind + " runtime selects and advances the audio playlist");
            SoundStore.Remove(kind, key);
        }

        SoundStore.SetMode("card", "old", "random");
        SoundStore.SetWeight("card", "old", 0, 0);
        SoundStore.SetWeight("card", "old", 1, 1);
        SoundStore.SetWeight("card", "old", 2, 3);
        var weighted = SoundStore.GetPlaylist("card", "old");
        var samples = new int[3];
        for (var i = 0; i < 4000; i++)
        {
            SoundStore.TrySelect("card", "old", out _, out var path);
            samples[weighted.Select(p => p.Path).ToList().IndexOf(path)]++;
        }
        Check(samples[0] == 0 && samples[1] is > 800 and < 1200 && samples[2] is > 2800 and < 3200,
            "weighted random follows 0:1:3 distribution and never selects a zero-weight item");
        var cursor = new SoundPlaylistCursor();
        var clips = weighted.Select(p => p.Clip).ToArray();
        Check(cursor.Select(clips, "random", roll: 0) == 1 && cursor.Select(clips, "random", roll: 0.2499) == 1 &&
              cursor.Select(clips, "random", roll: 0.25) == 2 && cursor.Select(clips, "random", roll: 0.9999) == 2,
            "weighted random respects the distribution boundaries");
        SoundStore.SetWeight("card", "old", 1, 0);
        saved = File.ReadAllText(config);
        failed = false;
        try { SoundStore.SetWeight("card", "old", 2, 0); } catch (InvalidDataException) { failed = true; }
        Check(failed && File.ReadAllText(config) == saved, "all-zero weights are rejected without damaging saved configuration");
        SoundStore.SetWeight("card", "old", 0, 1);
        SoundStore.SetWeight("card", "old", 1, 2);
        SoundStore.SetWeight("card", "old", 2, 7);
        var playlistZip = SoundStore.Export("PlaylistRegression", "Test");
        using (var archive = ZipFile.OpenRead(playlistZip))
            Check(archive.Entries.Count(e => e.FullName.EndsWith(".wav")) == 3, "ZIP export includes every playlist audio resource");
        var expectedNames = SoundStore.GetPlaylist("card", "old").Select(p => p.Clip.Name).ToArray();
        SoundStore.Import(playlistZip);
        Check(SoundStore.TryGet("card", "old", out var restored, out _) && restored.Mode == "random" && restored.Volume == 1.2f &&
              restored.AudioClips.Select(c => c.Weight).SequenceEqual(new double[] { 1, 2, 7 }) &&
              restored.AudioClips.Select(c => c.Name).SequenceEqual(expectedNames) &&
              SoundStore.GetPlaylist("card", "old").All(p => File.Exists(p.Path)),
            "ZIP roundtrip preserves playlist order, names, mode, weights, volume and all audio");
        SoundStore.Initialize();
        Check(SoundStore.TryGet("card", "old", out restored, out _) && restored.AudioClips.Count == 3 && restored.Mode == "random",
            "playlist configuration persists across reload");

        foreach (var corruption in new[] { "missing-second", "corrupt-second", "negative-weight", "unsafe-second" })
        {
            var bad = ProjectSettings.GlobalizePath("user://bad-playlist.zip");
            if (File.Exists(bad)) File.Delete(bad);
            using (var archive = ZipFile.Open(bad, ZipArchiveMode.Create))
            {
                var broken = new SoundBinding { Kind = "card", Key = "broken", Mode = "random", Clips =
                    [new() { File = "assets/first.wav" }, new() { File = "assets/second.wav" }] };
                if (corruption == "negative-weight") broken.Clips[1].Weight = -1;
                if (corruption == "unsafe-second") broken.Clips[1].File = "assets/../escape.wav";
                using (var writer = new StreamWriter(archive.CreateEntry("Pack/soundpack.json").Open()))
                    writer.Write(JsonSerializer.Serialize(new SoundPackData { Sounds = [broken] }));
                archive.CreateEntryFromFile(clip, "Pack/assets/first.wav");
                if (corruption == "corrupt-second")
                { using var writer = new StreamWriter(archive.CreateEntry("Pack/assets/second.wav").Open()); writer.Write("corrupt"); }
                else if (corruption != "missing-second") archive.CreateEntryFromFile(clip, "Pack/assets/second.wav");
            }
            saved = File.ReadAllText(config); count = Directory.GetFiles(assetDir).Length; failed = false;
            try { SoundStore.Import(bad); } catch (InvalidDataException) { failed = true; }
            Check(failed && File.ReadAllText(config) == saved && Directory.GetFiles(assetDir).Length == count,
                "playlist ZIP " + corruption + " rejects the entire package without changing saved data or leaking assets");
        }

        var panel = new SoundPlaylistDialog("card", "old", "Playlist Regression", 1, () => { });
        GetTree().Root.AddChild(panel);
        await Delay(0.1);
        Check(Field<List<Label>>(panel, "_probabilities").Select(l => l.Text).SequenceEqual(new[] { "10.0 %", "20.0 %", "70.0 %" }) ||
              Field<List<Label>>(panel, "_probabilities").Select(l => l.Text.Replace(" ", "")).SequenceEqual(new[] { "10.0%", "20.0%", "70.0%" }),
            "editor displays the normalized probability column for each item");
        Field<List<SpinBox>>(panel, "_weights")[0].Value = 3;
        Check(SoundStore.TryGet("card", "old", out restored, out _) && restored.AudioClips[0].Weight == 3,
            "probability table saves a user weight edit immediately");
        Field<OptionButton>(panel, "_mode").EmitSignal(OptionButton.SignalName.ItemSelected, 0L);
        var beforePreview = SoundStore.GetPlaylist("card", "old");
        SoundStore.TrySelect("card", "old", out _, out var first);
        Field<Button>(panel, "_previewList").EmitSignal(Button.SignalName.Pressed);
        await Delay(0.05);
        SoundStore.TrySelect("card", "old", out _, out var second);
        Check(first == beforePreview[0].Path && second == beforePreview[1].Path,
            "playlist audition uses a private cursor and leaves gameplay order intact");
        panel.Close();
        await Delay(0.1);
        var missing = SoundStore.GetPlaylist("card", "old");
        File.Delete(missing[0].Path);
        SoundStore.TrySelect("card", "old", out _, out var surviving);
        Check(surviving == missing[1].Path, "sequential playback skips a missing list resource");
        foreach (var item in missing) if (File.Exists(item.Path)) File.Delete(item.Path);
        Check(!SoundStore.TryGet("card", "old", out _, out _) && !SoundStore.TrySelect("card", "old", out _, out _),
            "a completely missing playlist falls back without throwing during UI or runtime lookup");
        SoundStore.ClearPersonalLibrary();

        // Exercise real Finished signals, not just selection metadata, for music queues.
        string Tone(string name, double duration)
        {
            var path = ProjectSettings.GlobalizePath("user://" + name);
            using var writer = new BinaryWriter(File.Create(path));
            var frames = (int)(44100 * duration);
            writer.Write("RIFF"u8); writer.Write(36 + frames * 2); writer.Write("WAVEfmt "u8);
            writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(44100);
            writer.Write(88200); writer.Write((short)2); writer.Write((short)16);
            writer.Write("data"u8); writer.Write(frames * 2);
            for (var i = 0; i < frames; i++) writer.Write((short)(Math.Sin(i * Math.Tau * 440 / 44100) * 200));
            return path;
        }
        var a = Tone("playlist-A.wav", 0.6); var b = Tone("playlist-B.wav", 0.9);
        SoundStore.AddFiles("bgm", BgmRuntime.RunKeys[0], [a, b]);
        var musicPaths = SoundStore.GetPlaylist("bgm", BgmRuntime.RunKeys[0]);
        var controller = new NRunMusicController(); var proxy = new RunMusicProxyProbe();
        typeof(NRunMusicController).GetField("_proxy", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(controller, proxy);
        typeof(NRunMusicController).GetField("_currentTrack", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(controller, BgmRuntime.RunKeys[0]);
        string? ActivePath() => (string?)typeof(BgmRuntime).GetField("_activePath", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
        BgmRuntime.OnRunMusicUpdated(controller);
        await Delay(0.1);
        var firstPlayer = (AudioStreamPlayer?)typeof(BgmRuntime).GetField("_player", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
        BgmRuntime.OnRunMusicUpdated(controller); BgmRuntime.RefreshCurrent();
        Check(ActivePath() == musicPaths[0].Path &&
              firstPlayer == (AudioStreamPlayer?)typeof(BgmRuntime).GetField("_player", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null),
            "repeated music updates keep the current playlist item playing without advancing or restarting it");
        async Task WaitForMusic(string expected)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (ActivePath() != expected && timer.Elapsed.TotalSeconds < 3) await Delay(0.03);
        }
        await WaitForMusic(musicPaths[1].Path);
        Check(ActivePath() == musicPaths[1].Path, "BGM advances to the second item on actual audio completion");
        await WaitForMusic(musicPaths[0].Path);
        Check(ActivePath() == musicPaths[0].Path, "BGM wraps to the first item after the last item finishes");
        BgmRuntime.OnRunMusicStopped();
        await Delay(0.7);
        Check(typeof(BgmRuntime).GetField("_player", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null) == null,
            "stopping music cancels the queue and prevents stale completion callbacks from restarting it");
        controller.Free(); proxy.Free();
        SoundStore.ClearPersonalLibrary();
    }
}
