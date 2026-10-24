using Godot;

namespace MySfxMod;

/// <summary>Editors stay in the main game viewport: no OS window focus transfer.</summary>
public partial class SoundEditorOverlay : CanvasLayer
{
    private static int _openCount;
    private static bool _previousEmbeddedWindows;
    private readonly Control _screen = new();
    private readonly Panel _panel = new();
    private readonly Label _heading = new();
    private readonly Button _close = new() { Text = "×", CustomMinimumSize = new Vector2(44, 40) };
    protected Control Body { get; } = new();
    protected Vector2 PreferredSize { get; set; } = new(800, 470);
    protected string Title { set => _heading.Text = value; }
    public event Action? Closed;

    public SoundEditorOverlay()
    {
        Layer = 120;
        ProcessMode = ProcessModeEnum.Always;
        AddChild(_screen);
        _screen.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.65f) };
        _screen.AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _screen.AddChild(_panel);
        var style = WorkshopStyle.Box("142025", "887650", 0, 14);
        style.ShadowColor = new Color(0, 0, 0, 0.6f);
        style.ShadowSize = 18;
        _panel.AddThemeStyleboxOverride("panel", style);
        _panel.Theme = WorkshopStyle.Theme();
        var header = new HBoxContainer { AnchorRight = 1, OffsetLeft = 28, OffsetTop = 16, OffsetRight = -20, OffsetBottom = 62 };
        _panel.AddChild(header);
        _heading.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _heading.AddThemeColorOverride("font_color", new Color("e8cb84"));
        _heading.AddThemeFontSizeOverride("font_size", 26);
        _close.TooltipText = "关闭（Esc）";
        header.AddChild(_heading);
        header.AddChild(_close);
        _close.Pressed += Close;
        _panel.AddChild(Body);
        Body.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        Body.OffsetTop = 76;
    }

    public override void _Ready()
    {
        // Only the embedded file picker uses Window; scope this flag to all open editors.
        var root = GetTree().Root;
        if (_openCount++ == 0) _previousEmbeddedWindows = root.GuiEmbedSubwindows;
        root.GuiEmbedSubwindows = true;
        _screen.Resized += LayoutPanel;
        LayoutPanel();
        GrabFocus();
    }

    private void LayoutPanel()
    {
        // Scale the entire panel on small windows, keeping every control reachable.
        var available = _screen.Size - new Vector2(32, 32);
        var scale = Math.Min(1f, Math.Min(available.X / PreferredSize.X, available.Y / PreferredSize.Y));
        _panel.Size = PreferredSize;
        _panel.Scale = Vector2.One * Math.Max(0.1f, scale);
        _panel.Position = (_screen.Size - _panel.Size * _panel.Scale) / 2;
    }

    public void GrabFocus() => _close.GrabFocus();

    public void Close()
    {
        if (IsQueuedForDeletion()) return;
        Closed?.Invoke();
        QueueFree();
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (input.IsActionPressed("ui_cancel")) Close();
        GetViewport().SetInputAsHandled();
    }

    public override void _ExitTree()
    {
        if (--_openCount == 0) GetTree().Root.GuiEmbedSubwindows = _previousEmbeddedWindows;
    }
}
