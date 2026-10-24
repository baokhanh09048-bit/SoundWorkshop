using Godot;

namespace MySfxMod;

/// <summary>A viewport-level shortcut that survives main-menu, map and combat scene changes.</summary>
public sealed partial class SoundWorkshopLauncher : CanvasLayer
{
    private static SoundWorkshopLauncher? _instance;
    private readonly Button _button = new()
    {
        Name = "SoundWorkshopButton",
        Text = "♫",
        TooltipText = "音效工坊",
        FocusMode = Control.FocusModeEnum.None,
        MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        AnchorLeft = 1,
        AnchorRight = 1,
        OffsetLeft = -98,
        OffsetRight = -24,
        OffsetTop = 106,
        OffsetBottom = 164
    };

    public static void Initialize()
    {
        if (_instance is { } current && GodotObject.IsInstanceValid(current)) return;
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        _instance = new SoundWorkshopLauncher { Name = "SoundWorkshopLauncher", Layer = 30 };
        tree.Root.AddChild(_instance);
    }

    public static void SetButtonVisible(bool visible)
    {
        if (_instance is { } current && GodotObject.IsInstanceValid(current)) current._button.Visible = visible;
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        var viewport = new Control
        {
            Name = "ViewportAnchor",
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        viewport.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(viewport);
        _button.Theme = WorkshopStyle.Theme();
        _button.AddThemeFontSizeOverride("font_size", 30);
        _button.AddThemeColorOverride("font_color", WorkshopStyle.Gold);
        _button.AddThemeColorOverride("font_hover_color", new Color("f3d99f"));
        _button.Pressed += SoundWorkshop.Open;
        viewport.AddChild(_button);
    }
}
