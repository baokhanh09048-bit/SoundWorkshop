using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Bestiary;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;
using MegaCrit.Sts2.Core.Nodes.Screens.InspectScreens;
using MegaCrit.Sts2.Core.Nodes.Screens.RelicCollection;
using MegaCrit.Sts2.Core.Saves;

namespace MySfxMod;

internal static class OfficialBindingButtons
{
    private const string CardButtonName = "MySfxCardBinding";
    private const string RelicButtonName = "MySfxRelicBinding";
    private const string MonsterButtonName = "MySfxMonsterBinding";
    private static readonly FieldInfo RelicListField = typeof(NInspectRelicScreen).GetField("_relics", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo RelicIndexField = typeof(NInspectRelicScreen).GetField("_index", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly Dictionary<NBestiary, MonsterModel?> SelectedMonsters = new();
    private static NInspectCardScreen? _cardScreen;
    private static NInspectRelicScreen? _relicScreen;
    private static readonly Color TextColor = new("#e9dfc8");
    private static readonly Color HoverTextColor = new("#ffd45a");
    private static readonly Color PanelColor = new(0.055f, 0.07f, 0.08f, 0.94f);
    private static readonly Color PanelHoverColor = new(0.11f, 0.12f, 0.13f, 0.97f);
    private static readonly Color BorderColor = new(0.73f, 0.62f, 0.39f, 0.9f);

    static OfficialBindingButtons()
    {
        ObjectBindingDialog.BindingsChanged += RefreshVisible;
        SoundStore.Changed += RefreshVisible;
    }

    private static void RefreshVisible()
    {
        if (_cardScreen != null && GodotObject.IsInstanceValid(_cardScreen))
            SetCardButton(_cardScreen, _cardScreen.GetNodeOrNull<Button>(CardButtonName)?.Visible == true);
        if (_relicScreen != null && GodotObject.IsInstanceValid(_relicScreen))
            SetRelicButton(_relicScreen, _relicScreen.GetNodeOrNull<Button>(RelicButtonName)?.Visible == true);
        foreach (var (bestiary, monster) in SelectedMonsters)
        {
            if (monster == null || !GodotObject.IsInstanceValid(bestiary)) continue;
            var button = bestiary.GetNodeOrNull<Button>(MonsterButtonName);
            if (button == null || !button.Visible) continue;
            button.Text = SoundStore.TryGet("monster_hit", monster.Id.ToString(), out _, out _) ||
                SoundStore.TryGet("monster_death", monster.Id.ToString(), out _, out _) ? "修改怪物音效" : "绑定怪物音效";
        }
    }

    public static void InstallCardButton(NInspectCardScreen screen)
    {
        _cardScreen = screen;
        var button = MakeButton(CardButtonName, "绑定打出音效");
        button.AnchorLeft = 0.5f;
        button.AnchorRight = 0.5f;
        button.AnchorTop = 1;
        button.AnchorBottom = 1;
        button.OffsetLeft = -145;
        button.OffsetRight = 145;
        button.OffsetTop = -156;
        button.OffsetBottom = -104;
        button.TooltipText = "为当前卡牌绑定打出时播放的音效";
        button.Pressed += () =>
        {
            var card = screen.GetNodeOrNull<NCard>("Card")?.Model;
            if (card != null) ObjectBindingDialog.OpenCard(card);
        };
        screen.AddChild(button);
    }

    public static void SetCardButton(NInspectCardScreen screen, bool fromLibrary)
    {
        var button = screen.GetNodeOrNull<Button>(CardButtonName);
        if (button == null) return;
        var card = screen.GetNodeOrNull<NCard>("Card")?.Model;
        button.Visible = fromLibrary && screen.Visible && card != null;
        if (card != null)
            button.Text = SoundStore.TryGet("card", card.Id.ToString(), out _, out _) ? "修改打出音效" : "绑定打出音效";
    }

    public static void InstallRelicButton(NInspectRelicScreen screen)
    {
        _relicScreen = screen;
        var button = MakeButton(RelicButtonName, "绑定遗物音效");
        button.AnchorLeft = 0.5f;
        button.AnchorRight = 0.5f;
        button.AnchorTop = 1;
        button.AnchorBottom = 1;
        button.OffsetLeft = -145;
        button.OffsetRight = 145;
        button.OffsetTop = -104;
        button.OffsetBottom = -52;
        button.TooltipText = "为当前遗物绑定获得时播放的音效";
        button.Pressed += () =>
        {
            var relic = CurrentRelic(screen);
            if (relic != null) ObjectBindingDialog.OpenRelic(relic);
        };
        screen.AddChild(button);
    }

    private static RelicModel? CurrentRelic(NInspectRelicScreen screen)
    {
        if (RelicListField.GetValue(screen) is not IReadOnlyList<RelicModel> relics) return null;
        var index = (int)RelicIndexField.GetValue(screen)!;
        return index >= 0 && index < relics.Count ? relics[index] : null;
    }

    public static void SetRelicButton(NInspectRelicScreen screen, bool fromCollection)
    {
        var button = screen.GetNodeOrNull<Button>(RelicButtonName);
        if (button == null) return;
        var relic = CurrentRelic(screen);
        button.Visible = fromCollection && screen.Visible && relic != null && SaveManager.Instance.IsRelicSeen(relic);
        if (relic != null)
            button.Text = SoundStore.TryGet("relic", relic.Id.ToString(), out _, out _) ? "修改遗物音效" : "绑定遗物音效";
    }

    public static void InstallMonsterButton(NBestiary bestiary)
    {
        var button = MakeButton(MonsterButtonName, "绑定怪物音效");
        button.AnchorLeft = 0;
        button.AnchorRight = 0;
        button.AnchorTop = 0.82f;
        button.AnchorBottom = 0.82f;
        button.OffsetLeft = 58;
        button.OffsetRight = 348;
        button.OffsetTop = -6;
        button.OffsetBottom = 46;
        button.TooltipText = "为当前怪物绑定受击、死亡和招式音效";
        button.Pressed += () =>
        {
            if (!SelectedMonsters.TryGetValue(bestiary, out var monster) || monster == null) return;
            var moves = bestiary.GetNode<Control>("%MoveList").GetChildren().OfType<NBestiaryMoveButton>().Select(b => b.Move).ToArray();
            ObjectBindingDialog.OpenMonster(monster, moves);
        };
        bestiary.AddChild(button);
        bestiary.TreeExited += () => SelectedMonsters.Remove(bestiary);
    }

    private static Button MakeButton(string name, string text)
    {
        var button = new Button
        {
            Name = name,
            Text = text,
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop,
            FocusMode = Control.FocusModeEnum.All,
            CustomMinimumSize = new Vector2(290, 52),
            Alignment = HorizontalAlignment.Center
        };
        button.AddThemeFontSizeOverride("font_size", 20);
        button.AddThemeColorOverride("font_color", TextColor);
        button.AddThemeColorOverride("font_hover_color", HoverTextColor);
        button.AddThemeColorOverride("font_pressed_color", HoverTextColor);
        button.AddThemeColorOverride("font_focus_color", HoverTextColor);
        button.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.7f));
        button.AddThemeConstantOverride("outline_size", 4);
        button.AddThemeStyleboxOverride("normal", MakePanel(PanelColor, BorderColor, 0.65f));
        button.AddThemeStyleboxOverride("hover", MakePanel(PanelHoverColor, new Color("#e1b84e"), 0.9f));
        button.AddThemeStyleboxOverride("pressed", MakePanel(new Color("#1a1712"), new Color("#fff0a6"), 1f));
        button.AddThemeStyleboxOverride("focus", MakePanel(PanelHoverColor, new Color("#e1b84e"), 0.9f));
        return button;
    }

    private static StyleBoxFlat MakePanel(Color background, Color border, float borderAlpha)
    {
        return new StyleBoxFlat
        {
            BgColor = background,
            BorderColor = new Color(border, borderAlpha),
            CornerRadiusTopLeft = 7,
            CornerRadiusTopRight = 7,
            CornerRadiusBottomLeft = 7,
            CornerRadiusBottomRight = 7,
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            ShadowColor = new Color(0, 0, 0, 0.45f),
            ShadowSize = 5,
            ShadowOffset = new Vector2(0, 3)
        };
    }

    public static void SelectMonster(NBestiary bestiary, NBestiaryEntry entry)
    {
        var monster = entry.IsDiscovered && !entry.IsUnderConstruction ? entry.Entry.monsterModel : null;
        SelectedMonsters[bestiary] = monster;
        var button = bestiary.GetNodeOrNull<Button>(MonsterButtonName);
        if (button == null) return;
        button.Visible = monster != null;
        if (monster != null)
            button.Text = SoundStore.TryGet("monster_hit", monster.Id.ToString(), out _, out _) ||
                SoundStore.TryGet("monster_death", monster.Id.ToString(), out _, out _) ? "修改怪物音效" : "绑定怪物音效";
    }
}

[HarmonyPatch(typeof(NInspectCardScreen), "_Ready")]
public static class InspectCardButtonPatch
{
    private static void Postfix(NInspectCardScreen __instance) => OfficialBindingButtons.InstallCardButton(__instance);
}

[HarmonyPatch(typeof(NInspectCardScreen), nameof(NInspectCardScreen.Open))]
public static class InspectCardOpenPatch
{
    private static void Prefix(NInspectCardScreen __instance) => OfficialBindingButtons.SetCardButton(__instance, false);
}

[HarmonyPatch(typeof(NCardLibrary), "ShowCardDetail")]
public static class CardLibraryInspectPatch
{
    private static void Postfix()
    {
        var screen = NGame.Instance?.GetInspectCardScreen();
        if (screen != null) OfficialBindingButtons.SetCardButton(screen, true);
    }
}

[HarmonyPatch(typeof(NInspectCardScreen), "SetCard")]
public static class InspectCardPagePatch
{
    private static void Postfix(NInspectCardScreen __instance)
    {
        var visible = __instance.GetNodeOrNull<Button>("MySfxCardBinding")?.Visible == true;
        OfficialBindingButtons.SetCardButton(__instance, visible);
    }
}

[HarmonyPatch(typeof(NInspectRelicScreen), "_Ready")]
public static class InspectRelicButtonPatch
{
    private static void Postfix(NInspectRelicScreen __instance) => OfficialBindingButtons.InstallRelicButton(__instance);
}

[HarmonyPatch(typeof(NInspectRelicScreen), nameof(NInspectRelicScreen.Open))]
public static class InspectRelicOpenPatch
{
    private static void Prefix(NInspectRelicScreen __instance) => OfficialBindingButtons.SetRelicButton(__instance, false);
}

[HarmonyPatch(typeof(NRelicCollectionCategory), "OnRelicEntryPressed")]
public static class RelicCollectionInspectPatch
{
    private static void Postfix()
    {
        var screen = NGame.Instance?.GetInspectRelicScreen();
        if (screen != null) OfficialBindingButtons.SetRelicButton(screen, true);
    }
}

[HarmonyPatch(typeof(NInspectRelicScreen), "SetRelic")]
public static class InspectRelicPagePatch
{
    private static void Postfix(NInspectRelicScreen __instance)
    {
        var visible = __instance.GetNodeOrNull<Button>("MySfxRelicBinding")?.Visible == true;
        OfficialBindingButtons.SetRelicButton(__instance, visible);
    }
}

[HarmonyPatch(typeof(NBestiary), "_Ready")]
public static class BestiaryButtonPatch
{
    private static void Postfix(NBestiary __instance) => OfficialBindingButtons.InstallMonsterButton(__instance);
}

[HarmonyPatch(typeof(NBestiary), "SelectMonster")]
public static class BestiarySelectionPatch
{
    private static void Postfix(NBestiary __instance, NBestiaryEntry entry) => OfficialBindingButtons.SelectMonster(__instance, entry);
}
