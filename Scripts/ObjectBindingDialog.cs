using Godot;
using MegaCrit.Sts2.Core.Models;

namespace MySfxMod;

/// <summary>Compact binder reached from an official card, relic, or monster inspection view.</summary>
public sealed partial class ObjectBindingDialog : SoundEditorOverlay
{
    public static event Action? BindingsChanged;
    private static ObjectBindingDialog? _open;
    private readonly List<(string Kind, string Key, string Label)> _slots = [];
    private readonly OptionButton _slotPicker = new();
    private readonly Label _file = new() { Text = "未绑定" };
    private readonly Label _status = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly SpinBox _volume = new() { MinValue = 0.05, MaxValue = 5, Step = 0.01, Value = 1, Suffix = "×" };
    private readonly SoundPreviewButton _preview = new();
    private readonly FileDialog _chooser = new() { Access = FileDialog.AccessEnum.Filesystem, FileMode = FileDialog.FileModeEnum.OpenFiles, Filters = ["*.wav, *.mp3 ; WAV/MP3"] };
    private int _index;

    public static void OpenCard(CardModel card) => Show(card.Title,
        [("card", card.Id.ToString(), "打出时的音效")], "打出卡牌时额外播放；原有卡牌效果音仍按事件级设置处理。");

    public static void OpenRelic(RelicModel relic) => Show(relic.Title.GetFormattedText(),
        [("relic", relic.Id.ToString(), "获得时的音效")], "获得遗物时播放自定义音效。");

    public static void OpenMonster(MonsterModel monster, IEnumerable<BestiaryMonsterMove> moves)
    {
        var slots = new List<(string Kind, string Key, string Label)>
        {
            ("monster_hit", monster.Id.ToString(), "受击音效 · 该怪物"),
            ("monster_death", monster.Id.ToString(), "死亡音效 · 该怪物")
        };
        foreach (var move in moves)
        {
            if (move.animId is "hurt" or "die") continue;
            var key = SoundRuntime.GetMonsterActionBindingKey(monster, move);
            if (slots.All(existing => existing.Key != key))
                slots.Add(("monster_action", key, "动作 · " + move.displayName));
        }
        Show(monster.Title.GetFormattedText(), slots, "动作直接按图鉴名称绑定，包括没有官方音效字段的动作。每次触发从列表选一个声音；原声是否同时播放由音效工坊的播放设置决定。");
    }

    private static void Show(string title, IReadOnlyList<(string Kind, string Key, string Label)> slots, string explanation)
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        if (_open is { } existing && GodotObject.IsInstanceValid(existing)) { existing.GrabFocus(); return; }
        var dialog = new ObjectBindingDialog(title, slots, explanation);
        _open = dialog;
        tree.Root.AddChild(dialog);
    }

    private ObjectBindingDialog(string title, IReadOnlyList<(string Kind, string Key, string Label)> slots, string explanation)
    {
        Title = "绑定音效 · " + title;
        PreferredSize = new Vector2(800, 470);
        Closed += () => _open = null;
        SoundStore.Changed += Refresh;
        TreeExiting += () => SoundStore.Changed -= Refresh;
        _slots.AddRange(slots);
        var frame = new VBoxContainer { AnchorRight = 1, AnchorBottom = 1, OffsetLeft = 18, OffsetTop = 18, OffsetRight = -18, OffsetBottom = -18 };
        Body.AddChild(frame);
        frame.AddChild(new Label { Text = title });
        foreach (var slot in _slots)
        {
            _slotPicker.AddItem(slot.Label);
        }
        _slotPicker.ItemSelected += index => { _index = (int)index; Refresh(); };
        frame.AddChild(_slotPicker);
        frame.AddChild(_file);
        var volumeRow = new HBoxContainer();
        volumeRow.AddChild(new Label { Text = "音量倍率" });
        volumeRow.AddChild(_volume);
        _volume.ValueChanged += value =>
        {
            if (_slots.Count > 0) SoundStore.SetVolume(Current.Kind, Current.Key, (float)value);
            _preview.SetVolume((float)value);
        };
        frame.AddChild(volumeRow);
        var actions = new HBoxContainer();
        frame.AddChild(actions);
        AddButton(actions, "添加 WAV/MP3", () =>
        {
            if (!string.IsNullOrEmpty(SoundSettings.LastAudioDirectory)) _chooser.CurrentDir = SoundSettings.LastAudioDirectory;
            _chooser.PopupCentered();
        });
        AddButton(actions, "编辑列表", () =>
        {
            _preview.Stop();
            SoundPlaylistDialog.Open(this, Current.Kind, Current.Key, title, (float)_volume.Value,
                () => { Refresh(); BindingsChanged?.Invoke(); });
        });
        actions.AddChild(_preview);
        _preview.StatusChanged += message => _status.Text = message;
        AddButton(actions, "恢复默认", () =>
        {
            SoundStore.Remove(Current.Kind, Current.Key); Refresh(); BindingsChanged?.Invoke(); _status.Text = "已恢复默认。";
        });
        frame.AddChild(new Label { Text = explanation, AutowrapMode = TextServer.AutowrapMode.WordSmart });
        frame.AddChild(_status);
        AddChild(_chooser);
        _chooser.ForceNative = false;
        _chooser.UseNativeDialog = false;
        void AddSources(string[] sources)
        {
            if (sources.Length == 0) return;
            SoundSettings.RememberAudioDirectory(sources[^1]);
            try { SoundStore.AddFiles(Current.Kind, Current.Key, sources, (float)_volume.Value); Refresh(); BindingsChanged?.Invoke(); _status.Text = "已添加到列表，下一次触发即生效。"; }
            catch (Exception e) { _status.Text = "导入失败：" + e.Message; }
        }
        _chooser.FileSelected += source => AddSources([source]);
        _chooser.FilesSelected += AddSources;
        Refresh();
    }

    private (string Kind, string Key, string Label) Current => _slots[_index];

    private void Refresh()
    {
        if (_slots.Count == 0) return;
        if (SoundStore.TryGet(Current.Kind, Current.Key, out var binding, out var path))
        {
            _file.Text = binding.Summary;
            _file.TooltipText = string.Join("\n", binding.AudioClips.Select(clip => clip.Name));
            _volume.SetValueNoSignal(binding.Volume);
            _preview.ConfigurePlaylist(Current.Kind, Current.Key, binding.Volume);
        }
        else
        {
            _file.Text = "尚未绑定自定义音频";
            _volume.SetValueNoSignal(1);
            _preview.Configure(null, 1);
        }
        _status.Text = "";
    }

    private static void AddButton(Node parent, string text, Action action)
    {
        var button = new Button { Text = text };
        button.Pressed += action;
        parent.AddChild(button);
    }
}
