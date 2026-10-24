using Godot;

namespace MySfxMod;

/// <summary>Shared playlist editor for compendium bindings, other effects and music.</summary>
public sealed partial class SoundPlaylistDialog : SoundEditorOverlay
{
    private readonly string _kind;
    private readonly string _key;
    private readonly float _initialVolume;
    private readonly Action _changed;
    private readonly OptionButton _mode = new();
    private readonly GridContainer _rows = new() { Columns = 7, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
    private readonly Label _summary = new();
    private readonly Label _hint = new();
    private readonly Label _status = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Button _previewList = new() { Text = "试听列表" };
    private readonly SoundPreviewButton _preview = new();
    private readonly List<Label> _probabilities = [];
    private readonly List<SpinBox> _weights = [];
    private readonly FileDialog _chooser = new()
    {
        Access = FileDialog.AccessEnum.Filesystem, FileMode = FileDialog.FileModeEnum.OpenFiles,
        Filters = ["*.wav, *.mp3 ; WAV/MP3"], ForceNative = false, UseNativeDialog = false
    };

    public static void Open(Node parent, string kind, string key, string label, float volume, Action changed)
    {
        if (string.IsNullOrEmpty(key) || parent.GetTree() is not { } tree) return;
        tree.Root.AddChild(new SoundPlaylistDialog(kind, key, label, volume, changed));
    }

    public SoundPlaylistDialog(string kind, string key, string label, float volume, Action changed)
    {
        _kind = kind; _key = key; _initialVolume = volume; _changed = changed;
        Title = "音频列表 · " + label;
        Layer = 122;
        PreferredSize = new Vector2(980, 660);
        var frame = new VBoxContainer { AnchorRight = 1, AnchorBottom = 1, OffsetLeft = 24, OffsetTop = 8, OffsetRight = -24, OffsetBottom = -20 };
        Body.AddChild(frame);
        var toolbar = new HBoxContainer();
        var add = new Button { Text = "＋ 添加 WAV / MP3" };
        WorkshopStyle.Primary(add);
        add.Pressed += () =>
        {
            var directory = SoundSettings.LastAudioDirectory;
            if (!string.IsNullOrEmpty(directory)) _chooser.CurrentDir = directory;
            _chooser.PopupCentered();
        };
        toolbar.AddChild(add);
        var modeLabel = WorkshopStyle.Label("播放方式", 18);
        modeLabel.AutowrapMode = TextServer.AutowrapMode.Off;
        toolbar.AddChild(modeLabel);
        _mode.AddItem("顺序播放"); _mode.AddItem("随机播放");
        _mode.ItemSelected += index => Apply(() => SoundStore.SetMode(_kind, _key, index == 0 ? "sequential" : "random"));
        toolbar.AddChild(_mode);
        _summary.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _summary.HorizontalAlignment = HorizontalAlignment.Right;
        _summary.AddThemeColorOverride("font_color", WorkshopStyle.Muted);
        toolbar.AddChild(_summary);
        frame.AddChild(toolbar);
        _hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _hint.AddThemeFontSizeOverride("font_size", 17);
        _hint.AddThemeColorOverride("font_color", WorkshopStyle.Muted);
        frame.AddChild(_hint);
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        frame.AddChild(scroll);
        _rows.AddThemeConstantOverride("h_separation", 12);
        _rows.AddThemeConstantOverride("v_separation", 6);
        scroll.AddChild(_rows);
        var footer = new HBoxContainer();
        _previewList.Pressed += () =>
        {
            if (!SoundStore.TryGet(_kind, _key, out var binding, out _)) return;
            _preview.ConfigurePlaylist(_kind, _key, binding.Volume, continuous: true, isBgm: _kind == "bgm");
            _preview.EmitSignal(Button.SignalName.Pressed);
        };
        footer.AddChild(_previewList);
        footer.AddChild(_preview);
        var footerHint = WorkshopStyle.Label("修改后自动保存 · 可一次选择多个文件", 16, true);
        footerHint.AutowrapMode = TextServer.AutowrapMode.Off;
        footer.AddChild(footerHint);
        frame.AddChild(footer);
        _preview.StatusChanged += message => _status.Text = message;
        _status.AddThemeFontSizeOverride("font_size", 16);
        _status.AddThemeColorOverride("font_color", WorkshopStyle.Bound);
        frame.AddChild(_status);
        AddChild(_chooser);
        _chooser.FilesSelected += AddSources;
        Closed += _preview.Stop;
        Refresh();
    }

    private void AddSources(string[] sources)
    {
        if (sources.Length == 0) return;
        SoundSettings.RememberAudioDirectory(sources[^1]);
        Apply(() => SoundStore.AddFiles(_kind, _key, sources, _initialVolume));
    }

    private void Apply(Action edit)
    {
        try
        {
            edit();
            SoundStore.NotifyPlaylistChanged();
            _changed();
            Refresh();
            _status.Text = "已保存。";
        }
        catch (Exception e) { _status.Text = "无法保存：" + e.Message; RefreshProbabilities(); }
    }

    private void Refresh()
    {
        _preview.Configure(null, 1);
        foreach (var child in _rows.GetChildren()) { _rows.RemoveChild(child); child.QueueFree(); }
        _probabilities.Clear(); _weights.Clear();
        var playlist = SoundStore.GetPlaylist(_kind, _key);
        SoundStore.TryGet(_kind, _key, out var binding, out _);
        _mode.Select(binding?.Mode == "random" ? 1 : 0);
        _mode.Disabled = playlist.Count == 0;
        _previewList.Disabled = playlist.Count == 0;
        _summary.Text = $"{playlist.Count} 个音频";
        _hint.Text = binding?.Mode == "random"
            ? "概率 = 该项权重 ÷ 总权重。权重 1、2、7 对应 10%、20%、70%；0 表示不参与随机抽取。"
            : _kind == "bgm" ? "一首结束后播放下一首，末尾回到第一首。可用 ↑ ↓ 调整顺序。"
            : "每次触发播放一项，按列表顺序轮换，末尾回到第一项。可用 ↑ ↓ 调整顺序。";
        foreach (var header in new[] { "#", "音频", "权重", "概率", "顺序", "试听", "移除" })
        {
            var heading = WorkshopStyle.Label(header, 16, true);
            heading.AutowrapMode = TextServer.AutowrapMode.Off;
            _rows.AddChild(heading);
        }
        for (var i = 0; i < playlist.Count; i++)
        {
            var index = i;
            var item = playlist[i];
            var number = WorkshopStyle.Label((i + 1).ToString(), 18);
            number.AutowrapMode = TextServer.AutowrapMode.Off;
            number.CustomMinimumSize = new Vector2(24, 0);
            _rows.AddChild(number);
            var name = new Label
            {
                Text = string.IsNullOrEmpty(item.Clip.Name) ? Path.GetFileName(item.Path) : item.Clip.Name,
                TooltipText = item.Clip.Name, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(270, 0)
            };
            name.AddThemeFontSizeOverride("font_size", 18);
            _rows.AddChild(name);
            var weight = new SpinBox { MinValue = 0, MaxValue = 1000000, Step = 0.1, Value = item.Clip.Weight,
                Editable = binding?.Mode == "random", CustomMinimumSize = new Vector2(112, 0) };
            weight.ValueChanged += value =>
            {
                try
                {
                    SoundStore.SetWeight(_kind, _key, index, value);
                    _preview.Stop();
                    RefreshProbabilities();
                    SoundStore.NotifyPlaylistChanged();
                    _changed();
                    _status.Text = "权重已保存。";
                }
                catch (Exception e) { RefreshProbabilities(); _status.Text = "无法保存：" + e.Message; }
            };
            _weights.Add(weight); _rows.AddChild(weight);
            var probability = WorkshopStyle.Label("", 18);
            probability.CustomMinimumSize = new Vector2(82, 0);
            _probabilities.Add(probability); _rows.AddChild(probability);
            var order = new HBoxContainer();
            var up = new Button { Text = "↑", Disabled = i == 0 };
            var down = new Button { Text = "↓", Disabled = i == playlist.Count - 1 };
            up.Pressed += () => Apply(() => SoundStore.MoveClip(_kind, _key, index, index - 1));
            down.Pressed += () => Apply(() => SoundStore.MoveClip(_kind, _key, index, index + 1));
            order.AddChild(up); order.AddChild(down); _rows.AddChild(order);
            var listen = new Button { Text = "▶", TooltipText = "试听此音频", Disabled = !File.Exists(item.Path) };
            listen.Pressed += () =>
            {
                SoundStore.TryGet(_kind, _key, out var current, out _);
                _preview.Configure(item.Path, current?.Volume ?? 1, isBgm: _kind == "bgm");
                _preview.EmitSignal(Button.SignalName.Pressed);
            };
            _rows.AddChild(listen);
            var remove = new Button { Text = "×", TooltipText = playlist.Count == 1 ? "移除最后一项并恢复默认声音" : "从列表移除此项" };
            remove.Pressed += () => Apply(() => SoundStore.RemoveClip(_kind, _key, index));
            _rows.AddChild(remove);
        }
        RefreshProbabilities();
        if (playlist.Count == 0) _status.Text = "还没有音频，点击添加即可建立列表。";
    }

    private void RefreshProbabilities()
    {
        if (!SoundStore.TryGet(_kind, _key, out var binding, out _)) return;
        var clips = binding.AudioClips;
        var total = clips.Sum(c => c.Weight);
        for (var i = 0; i < Math.Min(clips.Count, _probabilities.Count); i++)
        {
            _weights[i].SetValueNoSignal(clips[i].Weight);
            _probabilities[i].Text = binding.Mode == "random" ? $"{clips[i].Weight / total:P1}" : "—";
        }
    }
}
