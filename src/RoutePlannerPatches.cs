using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using MegaCrit.Sts2.Core.Runs;

namespace RoutePlanner;

[HarmonyPatch]
public static class RoutePlannerPatches
{
    [HarmonyPatch(typeof(NMapScreen), "SetMap")]
    [HarmonyPostfix]
    public static void OnSetMap(NMapScreen __instance, ActMap map)
    {
        ModLogger.Info($"Harmony: NMapScreen.SetMap called, ActMap={map?.GetType().Name ?? "null"}");
        if (RoutePlannerInstance.Instance == null)
        {
            ModLogger.Warn("Harmony: RoutePlannerInstance.Instance is null, skipping");
            return;
        }

        var runState = __instance.GetType()
            .GetField("_runState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
            .GetValue(__instance) as RunState;

        if (runState != null)
        {
            // At this point, GenerateMap() has already:
            // 1. Set State.Map = map          (line 557)
            // 2. Called RemoveStaleVisitedMapCoords() (line 558)
            // So CurrentMapPoint is valid for both single-player and multiplayer.
            // If it happens to be null (edge case), RouteDP falls back to StartingMapPoint.
            RoutePlannerInstance.Instance.OnMapScreenReady(__instance, runState);
        }
    }

    /// <summary>
    /// Hook NMapScreen.Open() to recalculate routes from updated player position.
    /// SetMap is only called on initial act entry; subsequent map opens (after leaving
    /// a room) only call Open(), so we must recalculate here to pick up the new starting point.
    /// </summary>
    [HarmonyPatch(typeof(NMapScreen), "Open")]
    [HarmonyPostfix]
    public static void OnMapScreenOpen(NMapScreen __instance)
    {
        var inst = RoutePlannerInstance.Instance;
        if (inst == null) return;

        ModLogger.Info("Harmony: NMapScreen.Open called — recalculating routes for current position");
        inst.MarkDirty();
    }

    [HarmonyPatch(typeof(NMapScreen), "_ExitTree")]
    [HarmonyPrefix]
    public static void OnMapScreenExit()
    {
        ModLogger.Info("Harmony: NMapScreen._ExitTree called");
        RoutePlannerInstance.Instance?.OnMapScreenExit();
    }

    /// <summary>
    /// Add "RoutePlanner MOD配置" section to the General tab of the game settings screen.
    /// Styled to match built-in settings items like SendFeedback.
    /// </summary>
    [HarmonyPatch(typeof(NSettingsScreen), "_Ready")]
    [HarmonyPostfix]
    public static void OnSettingsScreenReady(NSettingsScreen __instance)
    {
        try
        {
            var generalPanel = __instance.GetNode<NSettingsPanel>("%GeneralSettings");
            var content = generalPanel.Content;

            // Divider — matching SendFeedbackDivider: cream ColorRect, 2px tall
            var divider = new ColorRect { Name = "RoutePlannerDivider" };
            divider.CustomMinimumSize = new Vector2(0, 2);
            divider.Color = new Color(0.9098f, 0.8627f, 0.7451f, 0.251f);
            divider.MouseFilter = Control.MouseFilterEnum.Ignore;
            content.AddChild(divider);

            // MarginContainer — matching SendFeedback: 12px horizontal margins, 64px height
            var margin = new MarginContainer { Name = "RoutePlannerSettings" };
            margin.CustomMinimumSize = new Vector2(0, 64);
            margin.AddThemeConstantOverride("margin_left", 12);
            margin.AddThemeConstantOverride("margin_top", 0);
            margin.AddThemeConstantOverride("margin_right", 12);
            margin.AddThemeConstantOverride("margin_bottom", 0);

            // Label — RichTextLabel matching built-in settings labels: 28px cream
            var label = new RichTextLabel { Name = "Label" };
            label.BbcodeEnabled = true;
            label.Text = "[color=#FFF6E2]RoutePlanner MOD 配置[/color]";
            label.FitContent = true;
            label.ScrollActive = false;
            label.MouseFilter = Control.MouseFilterEnum.Ignore;
            label.AddThemeFontSizeOverride("normal_font_size", 28);
            label.AddThemeFontSizeOverride("bold_font_size", 28);
            label.AddThemeFontSizeOverride("mono_font_size", 28);
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            label.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            margin.AddChild(label);

            // Button — matching FeedbackButton: 320×64, shrink-end, event_button.png + HSV shader + green-outlined text
            var btn = BuildSettingsButton(__instance);
            margin.AddChild(btn);

            content.AddChild(margin);

            ModLogger.Info("RoutePlanner MOD 配置 section added to General settings panel");
        }
        catch
        {
            ModLogger.Error("Failed to add RoutePlanner MOD 配置 section");
        }
    }

    private static Control BuildSettingsButton(Node settingsScreen)
    {
        // Exact match of FeedbackButton structure and styling:
        // Control > TextureRect (event_button.png + HSV shader) + Label (cream + green outline)

        var btn = new Control { Name = "RoutePlannerButton" };
        btn.CustomMinimumSize = new Vector2(320, 64);
        btn.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        btn.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        btn.FocusMode = Control.FocusModeEnum.All;
        btn.MouseFilter = Control.MouseFilterEnum.Stop;
        btn.PivotOffset = new Vector2(160, 32);

        // Background — reward_skip_button.png + HSV shader, matching settings_screen.tscn FeedbackButton
        var img = new TextureRect { Name = "Image" };
        img.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        img.CustomMinimumSize = new Vector2(64, 64);
        img.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        img.StretchMode = TextureRect.StretchModeEnum.Scale;
        img.MouseFilter = Control.MouseFilterEnum.Ignore;
        var tex = ResourceLoader.Load<Texture2D>("res://images/ui/reward_screen/reward_skip_button.png");
        var shader = ResourceLoader.Load<Shader>("res://shaders/hsv.gdshader");
        if (tex != null && shader != null)
        {
            img.Texture = tex;
            var mat = new ShaderMaterial();
            mat.ResourceLocalToScene = true;
            mat.Shader = shader;
            mat.SetShaderParameter("h", 0.82f);
            mat.SetShaderParameter("s", 1.4f);
            mat.SetShaderParameter("v", 0.8f);
            img.Material = mat;
        }
        btn.AddChild(img);

        // Label — matching Feedback button: cream + green outline, Kreon Bold font
        var label = new Label { Name = "Label" };
        label.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        label.Text = I18n.Tr("基础分数设置");
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.AddThemeColorOverride("font_color", new Color(0.91f, 0.8636f, 0.7462f, 1f)); // cream
        label.AddThemeColorOverride("font_outline_color", new Color(0.1274f, 0.26f, 0.1407f, 1f)); // green
        label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.25f));
        label.AddThemeConstantOverride("outline_size", 12);
        label.AddThemeConstantOverride("shadow_outline_size", 0);
        label.AddThemeConstantOverride("shadow_offset_x", 4);
        label.AddThemeConstantOverride("shadow_offset_y", 3);
        label.AddThemeFontSizeOverride("font_size", 28);
        var font = ResourceLoader.Load<FontVariation>("res://themes/kreon_bold_glyph_space_two.tres");
        if (font != null)
        {
            label.AddThemeFontOverride("font", font);
            ModLogger.Info("BuildSettingsButton: font loaded successfully");
        }
        else
        {
            ModLogger.Warn("BuildSettingsButton: font load failed — using default font");
        }
        btn.AddChild(label);

        // Hover/press scale animations (matching NSettingsButton)
        btn.MouseEntered += () =>
        {
            var tween = btn.CreateTween();
            tween.SetParallel();
            tween.TweenProperty(btn, "scale", new Vector2(1.05f, 1.05f), 0.05);
        };
        btn.MouseExited += () =>
        {
            var tween = btn.CreateTween();
            tween.SetParallel();
            tween.TweenProperty(btn, "scale", Vector2.One, 0.5).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Expo);
        };
        btn.GuiInput += (evt) =>
        {
            if (evt is InputEventMouseButton mb)
            {
                if (mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                {
                    var tween = btn.CreateTween();
                    tween.SetParallel();
                    tween.TweenProperty(btn, "scale", new Vector2(0.95f, 0.95f), 0.25).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Expo);
                }
                else if (!mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                {
                    var tween = btn.CreateTween();
                    tween.SetParallel();
                    tween.TweenProperty(btn, "scale", Vector2.One, 0.05);
                    BaseScoreEditorPopup.Open(null, settingsScreen);
                }
            }
        };

        return btn;
    }
}
