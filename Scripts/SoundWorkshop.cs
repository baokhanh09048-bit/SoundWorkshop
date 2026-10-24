using Godot;

namespace MySfxMod;

/// <summary>Edits event-level sounds and exports every locally configured binding.</summary>
public sealed partial class SoundWorkshop : SoundEditorOverlay
{
    private static SoundWorkshop? _open;
    private readonly LineEdit _search = new() { PlaceholderText = "搜索音效名称" };
    private readonly Button _boundOnly = new() { Text = "仅已绑定", ToggleMode = true };
    private readonly ItemList _items = new() { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
    private readonly Label _selection = new() { Text = "选择一个音效" };
    private readonly Label _file = new() { Text = "尚未绑定音频" };
    private readonly Label _status = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart, Text = "这里管理卡牌、遗物和怪物以外的音效。游戏运行中检测到的新音效会自动出现在列表里。" };
    private readonly SpinBox _volume = new() { MinValue = 0.05, MaxValue = 5, Step = 0.01, Value = 1, Suffix = "×" };
    private readonly SoundPreviewButton _preview = new();
    private readonly FileDialog _chooser = new() { Access = FileDialog.AccessEnum.Filesystem, FileMode = FileDialog.FileModeEnum.OpenFiles, Filters = ["*.wav, *.mp3 ; WAV/MP3"] };
    private readonly FileDialog _packChooser = new() { Access = FileDialog.AccessEnum.Filesystem, FileMode = FileDialog.FileModeEnum.OpenFile, Filters = ["*.zip ; 音效包 ZIP"], Title = "导入音效包并替换个人方案", ForceNative = false, UseNativeDialog = false };
    private readonly LineEdit _packName = new() { Text = "我的音效包", PlaceholderText = "音效包名称" };
    private readonly LineEdit _packAuthor = new() { Text = "玩家", PlaceholderText = "作者" };
    private readonly List<string> _visible = [];
    private string _key = "";
    private long _recentRevision = -1;
    private double _refreshElapsed;
    private readonly VBoxContainer _editPage = new() { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
    private readonly VBoxContainer _packPage = new() { SizeFlagsVertical = Control.SizeFlags.ExpandFill, Visible = false };
    private readonly ScrollContainer _settingsScroll = new() { SizeFlagsVertical = Control.SizeFlags.ExpandFill, Visible = false };
    private readonly VBoxContainer _settingsPage = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
    private readonly VBoxContainer _details = new();
    private readonly CenterContainer _empty = new() { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
    private readonly Label _count = new();
    private readonly Label _bindingState = new();
    private readonly Button _importAudio = new() { Text = "添加音频" };
    private readonly Button _playlistEdit = new() { Text = "编辑列表" };
    private readonly Button _reset = new() { Text = "恢复默认" };
    private readonly Button _editTab = new() { Text = "音效编辑", ToggleMode = true, ButtonPressed = true };
    private readonly Button _bgmTab = new() { Text = "BGM", ToggleMode = true };
    private readonly Button _packTab = new() { Text = "音效包", ToggleMode = true };
    private readonly Button _settingsTab = new() { Text = "播放设置", ToggleMode = true };
    private readonly VBoxContainer _bgmPage = new() { SizeFlagsVertical = Control.SizeFlags.ExpandFill, Visible = false };
    private readonly ItemList _bgmSlots = new() { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
    private readonly Label _bgmSelection = new() { Text = "选择一个 BGM 场景" };
    private readonly Label _bgmScope = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Label _bgmFile = new() { Text = "尚未绑定音乐", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Label _bgmBindingState = new();
    private readonly SpinBox _bgmVolume = new() { MinValue = 0.05, MaxValue = 5, Step = 0.01, Value = 1, Suffix = "×" };
    private readonly SoundPreviewButton _bgmPreview = new();
    private readonly FileDialog _bgmChooser = new() { Access = FileDialog.AccessEnum.Filesystem, FileMode = FileDialog.FileModeEnum.OpenFiles, Filters = ["*.wav, *.mp3 ; WAV/MP3"] };
    private readonly Button _bgmImport = new() { Text = "添加音乐" };
    private readonly Button _bgmPlaylistEdit = new() { Text = "编辑列表" };
    private readonly Button _bgmReset = new() { Text = "恢复原曲" };
    private readonly List<string> _bgmKeys = BgmRuntime.Tracks.Select(track => track.Key).ToList();
    private string _bgmKey = BgmRuntime.MainMenuKey;
    private readonly Button _replaceOriginal = new() { Text = "替换原声", ToggleMode = true };
    private readonly Button _playTogether = new() { Text = "同时播放", ToggleMode = true };
    private readonly CheckButton _enablePacks = new() { Text = "启用已安装的音效包" };
    private readonly SpinBox _masterVolume = new() { MinValue = 0.25, MaxValue = 2, Step = 0.05, Value = 1, Suffix = "×" };
    private readonly Button _initialize = new() { Text = "一键初始化" };
    private Window? _dropWindow;
    private readonly ConfirmationDialog _initializeConfirm = new()
    {
        Title = "初始化音效工坊",
        DialogText = "将解绑全部个人音效并删除已导入的音频资源，同时恢复播放设置。\n\n导出文件和备份不会删除，已安装音效包会停用。",
        OkButtonText = "全部解绑并清理",
        CancelButtonText = "取消"
    };
    private bool _syncingSettings;

    public static void Open()
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        if (_open is { } existing && GodotObject.IsInstanceValid(existing)) { existing.GrabFocus(); return; }
        var workshop = new SoundWorkshop();
        _open = workshop;
        SoundWorkshopLauncher.SetButtonVisible(false);
        tree.Root.AddChild(workshop);
    }

    public SoundWorkshop()
    {
        Title = "音效工坊";
        PreferredSize = new Vector2(1060, 720);
        Closed += () => { _open = null; SoundWorkshopLauncher.SetButtonVisible(true); };
        SoundStore.Changed += RefreshList;
        SoundStore.Changed += RefreshBgmPage;
        SoundSettings.Changed += OnSettingsChanged;
        TreeExiting += () => { SoundStore.Changed -= RefreshList; SoundStore.Changed -= RefreshBgmPage; SoundSettings.Changed -= OnSettingsChanged; };
        var frame = new VBoxContainer { AnchorRight = 1, AnchorBottom = 1, OffsetLeft = 26, OffsetTop = 0, OffsetRight = -26, OffsetBottom = -20 };
        Body.AddChild(frame);
        var tabs = new HBoxContainer();
        var group = new ButtonGroup();
        _editTab.ButtonGroup = _bgmTab.ButtonGroup = _packTab.ButtonGroup = _settingsTab.ButtonGroup = group;
        _editTab.CustomMinimumSize = _bgmTab.CustomMinimumSize = _packTab.CustomMinimumSize = _settingsTab.CustomMinimumSize = new Vector2(140, 44);
        tabs.AddChild(_editTab);
        tabs.AddChild(_bgmTab);
        tabs.AddChild(_packTab);
        tabs.AddChild(_settingsTab);
        _editTab.Pressed += () => SwitchPage(0);
        _bgmTab.Pressed += () => SwitchPage(1);
        _packTab.Pressed += () => SwitchPage(2);
        _settingsTab.Pressed += () => SwitchPage(3);
        frame.AddChild(tabs);
        frame.AddChild(_editPage);
        frame.AddChild(_bgmPage);
        frame.AddChild(_packPage);
        _settingsScroll.AddChild(_settingsPage);
        frame.AddChild(_settingsScroll);
        BuildEditor();
        BuildBgmPage();
        BuildPacks();
        BuildSettings();
        _status.AddThemeFontSizeOverride("font_size", 16);
        _status.AddThemeColorOverride("font_color", WorkshopStyle.Bound);
        _status.CustomMinimumSize = new Vector2(0, 24);
        _status.MaxLinesVisible = 2;
        frame.AddChild(_status);

        AddChild(_chooser);
        _chooser.ForceNative = false;
        _chooser.UseNativeDialog = false;
        void AddAudio(string[] sources)
        {
            if (sources.Length == 0) return;
            SoundSettings.RememberAudioDirectory(sources[^1]);
            try
            {
                SoundStore.AddFiles(SoundRuntime.SoundKind(_key), _key, sources, (float)_volume.Value);
                RefreshList();
                RefreshSelection();
                _status.Text = "已添加到列表，下一次触发即生效。";
            }
            catch (Exception e) { ShowError("导入失败：" + e.Message); }
        }
        _chooser.FileSelected += source => AddAudio([source]);
        _chooser.FilesSelected += AddAudio;
        AddChild(_bgmChooser);
        _bgmChooser.ForceNative = false;
        _bgmChooser.UseNativeDialog = false;
        void AddMusic(string[] sources)
        {
            if (sources.Length == 0) return;
            SoundSettings.RememberAudioDirectory(sources[^1]);
            try
            {
                SoundStore.AddFiles("bgm", _bgmKey, sources, (float)_bgmVolume.Value);
                BgmRuntime.RefreshCurrent();
                RefreshBgmPage();
                _status.Text = "已添加；该场景将按列表模式播放音乐。";
            }
            catch (Exception e) { ShowError("导入失败：" + e.Message); }
        }
        _bgmChooser.FileSelected += source => AddMusic([source]);
        _bgmChooser.FilesSelected += AddMusic;
        AddChild(_packChooser);
        _packChooser.FileSelected += source =>
        {
            ImportPack(source);
        };
        AddChild(_initializeConfirm);
        _initializeConfirm.Confirmed += InitializeEverything;
        RefreshList();
        RefreshBgmPage();
    }

    public override void _Ready()
    {
        base._Ready();
        _dropWindow = GetWindow();
        if (_dropWindow != null) _dropWindow.FilesDropped += OnFilesDropped;
    }

    public override void _ExitTree()
    {
        if (_dropWindow != null) _dropWindow.FilesDropped -= OnFilesDropped;
        base._ExitTree();
    }

    private void OnFilesDropped(string[] files)
    {
        if (!_packPage.Visible) return;
        var zips = files.Where(file => Path.GetExtension(file).Equals(".zip", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (zips.Length == 1) ImportPack(zips[0]);
        else if (zips.Length > 1) ShowError("一次请拖入一个 ZIP 音效包。");
        else if (files.Length > 0) ShowError("这里只接受 ZIP 音效包。");
    }

    private void ImportPack(string source)
    {
        _preview.Stop();
        SoundSettings.RememberPackDirectory(source);
        try
        {
            var result = SoundStore.Import(source);
            _packName.Text = result.Name;
            _status.Text = $"已导入 {result.Count} 项音效，即时生效。旧方案已备份。";
            _status.TooltipText = result.Backup;
        }
        catch (Exception e) { ShowError("导入失败，原方案未替换：" + e.Message); }
    }

    private void BuildEditor()
    {
        var columns = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 22);
        _editPage.AddChild(columns);
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(350, 0) };
        columns.AddChild(left);
        var listHeading = new HBoxContainer();
        var listTitle = WorkshopStyle.Label("其他音效", 20);
        listTitle.AutowrapMode = TextServer.AutowrapMode.Off;
        listHeading.AddChild(listTitle);
        _count.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _count.HorizontalAlignment = HorizontalAlignment.Right;
        _count.AddThemeFontSizeOverride("font_size", 16);
        _count.AddThemeColorOverride("font_color", WorkshopStyle.Muted);
        listHeading.AddChild(_count);
        left.AddChild(listHeading);
        _search.PlaceholderText = "搜索音效…";
        _search.ClearButtonEnabled = true;
        _search.TextChanged += _ => RefreshList();
        var searchRow = new HBoxContainer();
        _search.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        searchRow.AddChild(_search);
        _boundOnly.AddThemeFontSizeOverride("font_size", 17);
        _boundOnly.TooltipText = "只显示当前生效的自定义音效，可与搜索词同时筛选";
        _boundOnly.Toggled += _ => RefreshList();
        searchRow.AddChild(_boundOnly);
        left.AddChild(searchRow);
        _items.CustomMinimumSize = new Vector2(350, 0);
        _items.AddThemeFontSizeOverride("font_size", 18);
        _items.ItemSelected += index => { _key = _visible[(int)index]; RefreshSelection(); };
        left.AddChild(_items);
        left.AddChild(WorkshopStyle.Label("最近播放优先 · 自动更新", 15, true));

        var card = WorkshopStyle.Card(columns);
        card.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        var emptyText = new VBoxContainer { CustomMinimumSize = new Vector2(330, 0) };
        var emptyTitle = WorkshopStyle.Label("从左侧选择一个音效", 24);
        emptyTitle.HorizontalAlignment = HorizontalAlignment.Center;
        emptyText.AddChild(emptyTitle);
        var emptyHint = WorkshopStyle.Label("选择后可绑定音频、调整音量和试听", 17, true);
        emptyHint.HorizontalAlignment = HorizontalAlignment.Center;
        emptyText.AddChild(emptyHint);
        _empty.AddChild(emptyText);
        card.AddChild(_empty);
        card.AddChild(_details);
        _details.AddThemeConstantOverride("separation", 20);
        var eyebrow = new HBoxContainer();
        var detailLabel = WorkshopStyle.Label("当前音效", 16, true);
        detailLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        eyebrow.AddChild(detailLabel);
        _bindingState.AddThemeFontSizeOverride("font_size", 16);
        eyebrow.AddChild(_bindingState);
        _details.AddChild(eyebrow);
        _selection.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _selection.AddThemeFontSizeOverride("font_size", 28);
        _selection.AddThemeColorOverride("font_color", WorkshopStyle.Gold);
        _details.AddChild(_selection);
        _file.AddThemeFontSizeOverride("font_size", 18);
        _file.AddThemeColorOverride("font_color", WorkshopStyle.Muted);
        _details.AddChild(_file);
        var actions = new HBoxContainer();
        WorkshopStyle.Primary(_importAudio);
        _importAudio.TooltipText = "支持 WAV / MP3，选择后自动保存";
        _importAudio.Pressed += () =>
        {
            if (_key.Length == 0) return;
            SetChooserDirectory(_chooser, SoundSettings.LastAudioDirectory);
            _chooser.PopupCentered();
        };
        actions.AddChild(_importAudio);
        _playlistEdit.Pressed += () =>
        {
            _preview.Stop();
            SoundPlaylistDialog.Open(this, SoundRuntime.SoundKind(_key), _key, EventLabel(_key), (float)_volume.Value,
                () => { RefreshList(); RefreshSelection(); });
        };
        actions.AddChild(_playlistEdit);
        actions.AddChild(_preview);
        _preview.StatusChanged += message => _status.Text = message;
        _details.AddChild(actions);
        _details.AddChild(new HSeparator());
        var volumeRow = new HBoxContainer();
        var volumeLabel = WorkshopStyle.Label("音量", 20);
        volumeLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        volumeRow.AddChild(volumeLabel);
        _volume.CustomMinimumSize = new Vector2(140, 0);
        _volume.TooltipText = "1.0 × 为标准音量；修改后自动保存";
        _volume.ValueChanged += value =>
        {
            if (_key.Length > 0) SoundStore.SetVolume(SoundRuntime.SoundKind(_key), _key, (float)value);
            _preview.SetVolume((float)value);
        };
        volumeRow.AddChild(_volume);
        _details.AddChild(volumeRow);
        var secondary = new HBoxContainer();
        _reset.AddThemeFontSizeOverride("font_size", 17);
        _reset.TooltipText = "移除此项个人绑定；如有已启用的音效包，则回到音效包设置";
        _reset.Pressed += () => { if (_key.Length == 0) return; SoundStore.Remove(SoundRuntime.SoundKind(_key), _key); RefreshList(); _status.Text = "已恢复默认。"; };
        secondary.AddChild(_reset);
        _details.AddChild(secondary);
    }

    private void BuildBgmPage()
    {
        var intro = WorkshopStyle.Label("每个首领可独立设置整场音乐，包括原版共用音乐或没有专属音乐的首领；原版的阶段变化不单独设置。", 17, true);
        intro.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _bgmPage.AddChild(intro);

        var columns = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 20);
        _bgmPage.AddChild(columns);

        var left = WorkshopStyle.Card(columns);
        left.GetParent<Control>().CustomMinimumSize = new Vector2(310, 0);
        left.AddChild(WorkshopStyle.Label("场景与首领音乐", 24));
        _bgmSlots.CustomMinimumSize = new Vector2(320, 180);
        _bgmSlots.AddThemeFontSizeOverride("font_size", 19);
        _bgmSlots.ItemSelected += index =>
        {
            _bgmPreview.Stop();
            _bgmKey = _bgmKeys[(int)index];
            RefreshBgmSelection();
            _bgmSlots.EnsureCurrentIsVisible();
        };
        left.AddChild(_bgmSlots);
        left.AddChild(WorkshopStyle.Label($"{BgmRuntime.Tracks.Count(t => t.EncounterType != null)} 个独立首领 · WAV / MP3 · 单文件上限 100 MB", 15, true));

        var detail = WorkshopStyle.Card(columns);
        detail.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        detail.AddChild(WorkshopStyle.Label("当前场景", 16, true));
        _bgmBindingState.AddThemeFontSizeOverride("font_size", 16);
        detail.AddChild(_bgmBindingState);
        _bgmSelection.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _bgmSelection.AddThemeFontSizeOverride("font_size", 28);
        _bgmSelection.AddThemeColorOverride("font_color", WorkshopStyle.Gold);
        detail.AddChild(_bgmSelection);
        _bgmScope.AddThemeFontSizeOverride("font_size", 16);
        _bgmScope.AddThemeColorOverride("font_color", WorkshopStyle.Muted);
        detail.AddChild(_bgmScope);
        _bgmFile.AddThemeFontSizeOverride("font_size", 18);
        _bgmFile.AddThemeColorOverride("font_color", WorkshopStyle.Muted);
        detail.AddChild(_bgmFile);

        var actions = new HBoxContainer();
        WorkshopStyle.Primary(_bgmImport);
        _bgmImport.TooltipText = "将一个或多个 WAV/MP3 添加到该曲目的播放列表";
        _bgmImport.Pressed += () =>
        {
            SetChooserDirectory(_bgmChooser, SoundSettings.LastAudioDirectory);
            _bgmChooser.PopupCentered();
        };
        actions.AddChild(_bgmImport);
        _bgmPlaylistEdit.Pressed += () =>
        {
            _bgmPreview.Stop();
            SoundPlaylistDialog.Open(this, "bgm", _bgmKey, BgmLabel(_bgmKey), (float)_bgmVolume.Value, RefreshBgmPage);
        };
        actions.AddChild(_bgmPlaylistEdit);
        actions.AddChild(_bgmPreview);
        _bgmPreview.StatusChanged += message => _status.Text = message;
        detail.AddChild(actions);
        detail.AddChild(new HSeparator());

        var volumeRow = new HBoxContainer();
        var volumeLabel = WorkshopStyle.Label("该曲音量", 20);
        volumeLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        volumeRow.AddChild(volumeLabel);
        _bgmVolume.CustomMinimumSize = new Vector2(140, 0);
        _bgmVolume.TooltipText = "在游戏 BGM 音量和音效工坊总音量基础上调整";
        _bgmVolume.ValueChanged += value =>
        {
            if (_bgmKey.Length == 0) return;
            SoundStore.SetVolume("bgm", _bgmKey, (float)value);
            BgmRuntime.RefreshCurrent();
            _bgmPreview.SetVolume((float)value);
        };
        volumeRow.AddChild(_bgmVolume);
        detail.AddChild(volumeRow);

        _bgmReset.TooltipText = "移除此场景的个人音乐绑定，恢复游戏原曲或已安装音效包的设置";
        _bgmReset.Pressed += () =>
        {
            if (_bgmKey.Length == 0) return;
            var removedGeneric = _bgmKey == BgmRuntime.LegacyRunKey;
            SoundStore.Remove("bgm", _bgmKey);
            BgmRuntime.RefreshCurrent();
            RefreshBgmPage();
            _status.Text = removedGeneric ? "已移除旧版通用音乐。" : "已恢复该场景的原曲。";
        };
        detail.AddChild(_bgmReset);
        detail.AddChild(WorkshopStyle.Label("BGM 始终替换原曲，不受“同时播放”设置影响。", 16, true));
    }

    private static string BgmLabel(string key) => BgmRuntime.LabelFor(key);

    private void RefreshBgmPage()
    {
        if (!GodotObject.IsInstanceValid(_bgmSlots)) return;
        _bgmKeys.Clear();
        if (SoundStore.TryGet("bgm", BgmRuntime.LegacyRunKey, out _, out _))
            _bgmKeys.Add(BgmRuntime.LegacyRunKey);
        _bgmKeys.AddRange(BgmRuntime.Tracks.Select(track => track.Key));
        _bgmKeys.AddRange(BgmRuntime.LegacyBossTracks.Where(track =>
            SoundStore.TryGet("bgm", track.Key, out _, out _)).Select(track => track.Key));
        _bgmSlots.Clear();
        foreach (var key in _bgmKeys)
        {
            var bound = SoundStore.TryGet("bgm", key, out _, out _);
            var inherited = !bound && BgmRuntime.TryGetBinding(key, out _, out _, out _);
            _bgmSlots.AddItem(BgmLabel(key) + (bound ? "  ●" : ""));
            var index = _bgmSlots.ItemCount - 1;
            var state = bound ? "已单独绑定音乐列表" : inherited ? "继承已有音乐设置，可为此首领单独覆盖" : "使用游戏原曲";
            _bgmSlots.SetItemTooltip(index, BgmRuntime.ScopeFor(key) + "\n" + state);
            if (bound) _bgmSlots.SetItemCustomFgColor(index, WorkshopStyle.Bound);
        }
        var selected = _bgmKeys.IndexOf(_bgmKey);
        if (selected < 0) selected = 0;
        _bgmKey = _bgmKeys[selected];
        _bgmSlots.Select(selected);
        _bgmSlots.EnsureCurrentIsVisible();
        RefreshBgmSelection();
    }

    private void RefreshBgmSelection()
    {
        if (_bgmKey.Length == 0) return;
        _bgmSelection.Text = BgmLabel(_bgmKey);
        _bgmScope.Text = BgmRuntime.ScopeFor(_bgmKey);
        _bgmImport.Disabled = false;
        var hasLocalBinding = SoundStore.LocalBindings.Any(b => b.Kind == "bgm" && b.Key == _bgmKey);
        _bgmReset.Disabled = !hasLocalBinding;
        if (BgmRuntime.TryGetBinding(_bgmKey, out var binding, out var path, out var inherited))
        {
            _bgmFile.Text = inherited ? "继承：" + BgmLabel(binding.Key) + " · 可单独绑定" : binding.Summary;
            _bgmPlaylistEdit.Text = inherited ? "新建列表" : "编辑列表";
            _bgmFile.TooltipText = Path.GetFileName(path);
            _bgmBindingState.Text = inherited ? "继承已有音乐" : "● 已绑定";
            _bgmBindingState.AddThemeColorOverride("font_color", inherited ? WorkshopStyle.Muted : WorkshopStyle.Bound);
            _bgmVolume.SetValueNoSignal(binding.Volume);
            _bgmVolume.Editable = !inherited;
            _bgmVolume.TooltipText = inherited ? "请先单独选择音乐；继承列表的音量在原项目里调整" : "在游戏 BGM 音量和音效工坊总音量基础上调整";
            _bgmPreview.ConfigurePlaylist("bgm", binding.Key, binding.Volume, continuous: true, isBgm: true);
        }
        else
        {
            _bgmFile.Text = "当前使用游戏原曲";
            _bgmFile.TooltipText = "";
            _bgmBindingState.Text = "使用原曲";
            _bgmBindingState.AddThemeColorOverride("font_color", WorkshopStyle.Muted);
            _bgmVolume.SetValueNoSignal(1);
            _bgmVolume.Editable = true;
            _bgmPlaylistEdit.Text = "编辑列表";
            _bgmPreview.Configure(null, 1, loop: true, isBgm: true);
        }
    }

    private void BuildPacks()
    {
        var intro = WorkshopStyle.Label("导入别人的方案，或分享你的声音。", 18, true);
        _packPage.AddChild(intro);
        var columns = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 20);
        _packPage.AddChild(columns);
        var import = WorkshopStyle.Card(columns);
        import.GetParent<Control>().SizeFlagsStretchRatio = 1;
        import.AddChild(WorkshopStyle.Label("导入音效包", 26));
        import.AddChild(WorkshopStyle.Label("选择 ZIP，即可应用整套音效。", 18, true));
        var dropZone = new PanelContainer { CustomMinimumSize = new Vector2(0, 92), MouseFilter = Control.MouseFilterEnum.Ignore };
        dropZone.AddThemeStyleboxOverride("panel", WorkshopStyle.Box("18272d", "8b7a55", 16, 8));
        var dropText = WorkshopStyle.Label("将 ZIP 拖入此窗口\n（需停留在“音效包”页面）", 18, true);
        dropText.HorizontalAlignment = HorizontalAlignment.Center;
        dropText.VerticalAlignment = VerticalAlignment.Center;
        dropZone.AddChild(dropText);
        import.AddChild(dropZone);
        var importButton = new Button { Text = "选择 ZIP" };
        WorkshopStyle.Primary(importButton);
        importButton.Pressed += () =>
        {
            SetChooserDirectory(_packChooser, SoundSettings.LastPackDirectory);
            _packChooser.PopupCentered();
        };
        import.AddChild(importButton);
        import.AddChild(WorkshopStyle.Label("替换个人绑定 · 自动备份旧方案", 16, true));
        var backup = new Button { Text = "查看备份", TooltipText = "打开备份目录；重新导入其中的 ZIP 即可恢复旧方案" };
        backup.AddThemeFontSizeOverride("font_size", 17);
        backup.Pressed += () => { Directory.CreateDirectory(SoundStore.BackupDirectory); OS.ShellOpen(SoundStore.BackupDirectory); };
        import.AddChild(backup);
        var export = WorkshopStyle.Card(columns);
        export.GetParent<Control>().SizeFlagsStretchRatio = 1;
        export.AddChild(WorkshopStyle.Label("分享我的音效", 26));
        export.AddChild(WorkshopStyle.Label("包含卡牌、遗物、怪物、其他音效和 BGM。", 18, true));
        export.AddChild(WorkshopStyle.Label("音效包名称", 16, true));
        export.AddChild(_packName);
        export.AddChild(WorkshopStyle.Label("作者", 16, true));
        export.AddChild(_packAuthor);
        var exportButton = new Button { Text = "导出 ZIP" };
        WorkshopStyle.Primary(exportButton);
        exportButton.Pressed += () =>
        {
            try
            {
                var output = SoundStore.Export(_packName.Text.Trim(), _packAuthor.Text.Trim());
                _status.Text = "已导出音效包，可在导出目录中查看并分享。";
                _status.TooltipText = output;
            }
            catch (Exception e) { ShowError("导出失败：" + e.Message); }
        };
        export.AddChild(exportButton);
        AddButton(export, "查看导出文件", () => { Directory.CreateDirectory(SoundStore.ExportDirectory); OS.ShellOpen(SoundStore.ExportDirectory); });
    }

    private void BuildSettings()
    {
        _settingsPage.AddChild(WorkshopStyle.Label("决定绑定音效触发时，游戏原声如何播放。BGM 分类始终替换原曲。", 18, true));

        var playback = WorkshopStyle.Card(_settingsPage);
        playback.AddChild(WorkshopStyle.Label("播放方式", 26));
        playback.AddChild(WorkshopStyle.Label("此设置对卡牌、遗物、怪物和其他音效统一生效。", 17, true));
        var choices = new HBoxContainer();
        var group = new ButtonGroup { AllowUnpress = false };
        _replaceOriginal.ButtonGroup = _playTogether.ButtonGroup = group;
        _replaceOriginal.CustomMinimumSize = _playTogether.CustomMinimumSize = new Vector2(220, 58);
        _replaceOriginal.TooltipText = "播放自定义音效时停止对应的游戏原声";
        _playTogether.TooltipText = "自定义音效与对应的游戏原声一起播放";
        _replaceOriginal.Pressed += () => SoundSettings.SetPlayAlongsideOriginal(false);
        _playTogether.Pressed += () => SoundSettings.SetPlayAlongsideOriginal(true);
        choices.AddChild(_replaceOriginal);
        choices.AddChild(_playTogether);
        playback.AddChild(choices);
        playback.AddChild(new HSeparator());
        var masterRow = new HBoxContainer();
        var masterLabel = WorkshopStyle.Label("总音量 · 1.0 × 为标准", 20);
        masterLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        masterRow.AddChild(masterLabel);
        _masterVolume.CustomMinimumSize = new Vector2(150, 0);
        _masterVolume.TooltipText = "在每项单独音量的基础上统一调整；修改后立即生效";
        _masterVolume.ValueChanged += value =>
        {
            if (!_syncingSettings) SoundSettings.SetMasterVolume((float)value);
        };
        masterRow.AddChild(_masterVolume);
        playback.AddChild(masterRow);

        var packs = WorkshopStyle.Card(_settingsPage);
        packs.AddChild(WorkshopStyle.Label("已安装音效包", 24));
        _enablePacks.TooltipText = "关闭后只使用本机个人绑定；已安装的共享音效包暂不参与播放，也不会被删除";
        _enablePacks.Toggled += enabled =>
        {
            if (!_syncingSettings) SoundSettings.SetEnableSoundPacks(enabled);
        };
        packs.AddChild(_enablePacks);
        packs.AddChild(WorkshopStyle.Label("关闭后只使用本机的个人绑定；已安装的共享音效包不会参与播放，也不会被删除。个人绑定优先于共享音效包。", 16, true));

        _initialize.CustomMinimumSize = new Vector2(220, 48);
        _initialize.TooltipText = "清空全部个人绑定和已导入资源，并恢复播放设置";
        _initialize.Pressed += () => _initializeConfirm.PopupCentered(new Vector2I(620, 260));
        _settingsPage.AddChild(_initialize);
        _settingsPage.AddChild(WorkshopStyle.Label("清空个人绑定与音频资源；保留导出文件和备份，不卸载音效包。", 16, true));
        SyncSettings();
    }

    private void InitializeEverything()
    {
        _preview.Stop();
        var removed = SoundStore.ClearPersonalLibrary();
        SoundSettings.ResetPlayback(disableSoundPacks: true);
        _key = "";
        RefreshList();
        _status.Text = $"初始化完成：已清理 {removed} 项个人绑定，并停用已安装音效包。";
    }

    private void OnSettingsChanged()
    {
        SoundStore.Refresh();
        SyncSettings();
        _preview.SetVolume((float)_volume.Value);
        RefreshList();
        RefreshBgmPage();
    }

    private void SyncSettings()
    {
        _syncingSettings = true;
        _replaceOriginal.ButtonPressed = !SoundSettings.PlayAlongsideOriginal;
        _playTogether.ButtonPressed = SoundSettings.PlayAlongsideOriginal;
        _enablePacks.ButtonPressed = SoundSettings.EnableSoundPacks;
        _masterVolume.SetValueNoSignal(SoundSettings.MasterVolume);
        _syncingSettings = false;
    }

    private void SwitchPage(int page)
    {
        _editPage.Visible = page == 0;
        _bgmPage.Visible = page == 1;
        _packPage.Visible = page == 2;
        _settingsScroll.Visible = page == 3;
        _preview.Stop();
        _bgmPreview.Stop();
        if (page == 1) RefreshBgmPage();
        _status.Text = "";
    }

    private void ShowError(string message)
    {
        _status.Text = message;
        _status.TooltipText = message;
    }
    private static void AddButton(Node parent, string text, Action onPress)
    {
        var button = new Button { Text = text };
        button.Pressed += onPress;
        parent.AddChild(button);
    }

    private static void SetChooserDirectory(FileDialog chooser, string directory)
    {
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory)) chooser.CurrentDir = directory;
    }

    public override void _Process(double delta)
    {
        _refreshElapsed += delta;
        if (_refreshElapsed < 0.25) return;
        _refreshElapsed = 0;
        if (_recentRevision != SoundRuntime.RecentRevision) RebuildList(false);
    }

    private void RefreshList() => RebuildList(true);

    private void RebuildList(bool refreshSelection)
    {
        _visible.Clear();
        _items.Clear();
        var filter = _search.Text.Trim();
        var known = SoundRuntime.KnownEvents
            .Concat(SoundRuntime.KnownDebugSamples)
            .Concat(SoundStore.LocalBindings.Where(b => b.Kind is "event" or "debug").Select(b => b.Key))
            .Concat(SoundStore.ActiveBindings.Where(b => b.Kind is "event" or "debug").Select(b => b.Key))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(EventLabel, StringComparer.OrdinalIgnoreCase);
        var all = SoundRuntime.RecentSounds.Concat(known)
            .Distinct(StringComparer.Ordinal);
        foreach (var path in all)
        {
            var bound = SoundStore.TryGet(SoundRuntime.SoundKind(path), path, out _, out _);
            if (_boundOnly.ButtonPressed && !bound) continue;
            var label = EventLabel(path) + (bound ? "  ●" : "");
            if (filter.Length > 0 && !label.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            _visible.Add(path);
            _items.AddItem(label);
            var index = _items.ItemCount - 1;
            _items.SetItemTooltip(index, EventLabel(path) + (bound ? "\n已绑定自定义音频" : "\n使用默认音效"));
            if (bound) _items.SetItemCustomFgColor(index, WorkshopStyle.Bound);
        }
        _count.Text = $"{_visible.Count} 项";
        var selected = _visible.IndexOf(_key);
        if (selected >= 0) _items.Select(selected);
        else { _key = ""; refreshSelection = true; }
        _recentRevision = SoundRuntime.RecentRevision;
        if (refreshSelection) RefreshSelection();
    }

    private static string EventLabel(string path)
    {
        if (SoundRuntime.IsDebugKey(path)) return SoundRuntime.DebugLabel(path);
        var leaf = path.Split('/').LastOrDefault() ?? path;
        return leaf.Replace('_', ' ').Replace('-', ' ');
    }

    private void RefreshSelection()
    {
        _details.Visible = _key.Length > 0;
        _empty.Visible = _key.Length == 0;
        _importAudio.Disabled = _key.Length == 0;
        _playlistEdit.Disabled = _key.Length == 0;
        var kind = SoundRuntime.SoundKind(_key);
        _reset.Disabled = !SoundStore.LocalBindings.Any(b => b.Kind == kind && b.Key == _key);
        _selection.Text = _key.Length == 0 ? "选择一个音效" : EventLabel(_key);
        if (_key.Length > 0 && SoundStore.TryGet(kind, _key, out var binding, out var path))
        {
            _file.Text = binding.Summary;
            _file.TooltipText = Path.GetFileName(path);
            _bindingState.Text = "● 已绑定";
            _bindingState.AddThemeColorOverride("font_color", WorkshopStyle.Bound);
            _volume.SetValueNoSignal(binding.Volume);
            _preview.ConfigurePlaylist(kind, _key, binding.Volume);
        }
        else
        {
            _file.Text = "选择音频，为它换一个声音。";
            _file.TooltipText = "";
            _bindingState.Text = "默认音效";
            _bindingState.AddThemeColorOverride("font_color", WorkshopStyle.Muted);
            _volume.SetValueNoSignal(1);
            _preview.Configure(null, 1);
        }
        _status.Text = "";
    }
}
