using Godot;

namespace MySfxMod;

internal static class WorkshopStyle
{
    internal static readonly Color Gold = new("e2c58d");
    internal static readonly Color Text = new("e9ece7");
    internal static readonly Color Muted = new("9eafad");
    internal static readonly Color Bound = new("92cfba");

    internal static StyleBoxFlat Box(string background, string border = "35464b", int padding = 12, int radius = 8)
    {
        var box = new StyleBoxFlat { BgColor = new Color(background), BorderColor = new Color(border), ContentMarginLeft = padding, ContentMarginRight = padding, ContentMarginTop = padding, ContentMarginBottom = padding };
        box.SetBorderWidthAll(1);
        box.SetCornerRadiusAll(radius);
        return box;
    }

    internal static Theme Theme()
    {
        var theme = new Theme { DefaultFontSize = 20 };
        foreach (var type in new[] { "Label", "Button", "LineEdit", "ItemList" }) theme.SetColor("font_color", type, Text);
        theme.SetStylebox("normal", "Button", Box("26373b", padding: 12));
        theme.SetStylebox("hover", "Button", Box("344b4e", "819b91"));
        theme.SetStylebox("pressed", "Button", Box("3b5150", "bfa674"));
        theme.SetStylebox("disabled", "Button", Box("1b272b", "29383d"));
        var focus = Box("00000000", "e2c58d");
        focus.SetBorderWidthAll(2);
        theme.SetStylebox("focus", "Button", focus);
        theme.SetColor("font_disabled_color", "Button", new Color("657471"));
        theme.SetStylebox("normal", "LineEdit", Box("111d22"));
        theme.SetStylebox("focus", "LineEdit", Box("15232a", "bfa674"));
        theme.SetColor("font_placeholder_color", "LineEdit", Muted);
        theme.SetStylebox("panel", "ItemList", Box("142127", "293c42", 8));
        theme.SetStylebox("selected", "ItemList", Box("3b4c49", "9b8966", 8, 5));
        theme.SetStylebox("selected_focus", "ItemList", Box("455950", "d0b580", 8, 5));
        theme.SetStylebox("cursor", "ItemList", new StyleBoxEmpty());
        theme.SetStylebox("cursor_unfocused", "ItemList", new StyleBoxEmpty());
        theme.SetConstant("v_separation", "ItemList", 12);
        theme.SetConstant("h_separation", "HBoxContainer", 12);
        theme.SetConstant("separation", "HBoxContainer", 12);
        theme.SetConstant("separation", "VBoxContainer", 12);
        return theme;
    }

    internal static Label Label(string text, int size = 18, bool muted = false)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", muted ? Muted : Text);
        return label;
    }

    internal static void Primary(Button button)
    {
        button.AddThemeStyleboxOverride("normal", Box("bba16d", "d4b984"));
        button.AddThemeStyleboxOverride("hover", Box("ddc18b", "f0d9a6"));
        button.AddThemeStyleboxOverride("pressed", Box("a38b5b", "d4b984"));
        button.AddThemeColorOverride("font_color", new Color("182329"));
        button.AddThemeColorOverride("font_hover_color", new Color("182329"));
        button.AddThemeColorOverride("font_pressed_color", new Color("182329"));
    }

    internal static VBoxContainer Card(Node parent)
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        panel.AddThemeStyleboxOverride("panel", Box("1e2e33", "35474b", 24, 10));
        parent.AddChild(panel);
        var content = new VBoxContainer();
        panel.AddChild(content);
        return content;
    }
}
