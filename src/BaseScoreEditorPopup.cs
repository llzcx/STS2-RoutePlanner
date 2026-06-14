using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

namespace RoutePlanner;

public partial class BaseScoreEditorPopup : Control
{
    private static readonly Color WarmOrange = new(1f, 0.42f, 0.21f);
    private static readonly Color IceBlue = new(0.30f, 0.65f, 1f);
    private static readonly Color Gold = new(0.722f, 0.588f, 0.290f);

    private const float NameColWidth = 120f;
    private const float EditColWidth = 100f;
    private const float RowHeight = 46f;
    private const float PanelCornerRadius = 16f;

    // Editable base score types. Unknown is NOT in this list — it is a computed
    // weighted average displayed as a read-only row below the editable types.
    private static readonly (string typeKey, string i18nKey, Color color)[] _types =
    {
        ("Monster",   "怪物", WarmOrange),
        ("Elite",     "精英", new Color(1f, 0.45f, 0f)),
        ("RestSite",  "休息", new Color(0.3f, 0.85f, 0.3f)),
        ("Shop",      "商店", new Color(0.85f, 0.75f, 0.2f)),
        ("Treasure",  "宝箱", new Color(0.25f, 0.75f, 0.85f)),
        ("Event",     "事件", new Color(0.65f, 0.55f, 0.85f)),
    };

    private readonly Dictionary<string, LineEdit> _dangerEdits = new();
    private readonly Dictionary<string, LineEdit> _rewardEdits = new();
    private Label _unknownDangerLabel;
    private Label _unknownRewardLabel;

    public BaseScoreEditorPopup()
    {
        Name = "BaseScoreEditorPopup";
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsPreset(LayoutPreset.FullRect);
        ZIndex = 200;

        // Dark backdrop
        var backdrop = new ColorRect();
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        backdrop.Color = new Color(0, 0, 0, 0.65f);
        backdrop.GuiInput += (evt) =>
        {
            if (evt is InputEventMouseButton mb && mb.Pressed)
                Close();
        };
        AddChild(backdrop);

        float panelW = 880f;
        float panelH = 780f;
        var borderPanel = new Panel();
        borderPanel.CustomMinimumSize = new Vector2(panelW, panelH);
        borderPanel.MouseFilter = MouseFilterEnum.Ignore;

        var borderStyle = new StyleBoxFlat();
        borderStyle.BgColor = new Color(0.059f, 0.071f, 0.125f, 0.97f); // deep navy fill
        borderStyle.BorderWidthLeft = 0;
        borderStyle.BorderWidthRight = 0;
        borderStyle.BorderWidthTop = 0;
        borderStyle.BorderWidthBottom = 0;
        borderStyle.SetCornerRadiusAll((int)PanelCornerRadius);
        borderPanel.AddThemeStyleboxOverride("panel", borderStyle);
        AddChild(borderPanel);

        // Content area inside the panel
        float contentMargin = 24f;
        var contentArea = new VBoxContainer();
        contentArea.AddThemeConstantOverride("separation", 8);
        contentArea.OffsetLeft = contentMargin;
        contentArea.OffsetRight = -contentMargin;
        contentArea.OffsetTop = contentMargin;
        contentArea.OffsetBottom = -(contentMargin - 8);
        contentArea.SetAnchorsPreset(LayoutPreset.FullRect);
        contentArea.AnchorRight = 1f;
        contentArea.AnchorBottom = 1f;
        contentArea.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        contentArea.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        borderPanel.AddChild(contentArea);

        // Title — large cream text at top of content area
        var titleLabel = new Label { Text = I18n.Tr("基础分数设置") };
        titleLabel.AddThemeColorOverride("font_color", new Color(1f, 0.965f, 0.886f, 1f)); // cream #FFF6E2
        titleLabel.AddThemeFontSizeOverride("font_size", 36);
        titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        contentArea.AddChild(titleLabel);

        // Subtitle
        var subtitle = new Label { Text = I18n.Tr("基础分数说明") };
        subtitle.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.45f));
        subtitle.AddThemeFontSizeOverride("font_size", 15);
        contentArea.AddChild(subtitle);

        // Formula — danger / reward for normal nodes
        var formulaLabel = new Label { Text = I18n.Tr("得分公式") };
        formulaLabel.AddThemeColorOverride("font_color", WarmOrange);
        formulaLabel.AddThemeFontSizeOverride("font_size", 15);
        formulaLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        formulaLabel.CustomMinimumSize = new Vector2(0, 22);
        contentArea.AddChild(formulaLabel);

        // Formula — Unknown node weighted average
        var unkTitle = new Label { Text = I18n.Tr("未知公式标题") };
        unkTitle.AddThemeColorOverride("font_color", IceBlue);
        unkTitle.AddThemeFontSizeOverride("font_size", 15);
        unkTitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        unkTitle.CustomMinimumSize = new Vector2(0, 22);
        contentArea.AddChild(unkTitle);

        var unkFormula = new Label { Text = I18n.Tr("未知公式") };
        unkFormula.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.3f));
        unkFormula.AddThemeFontSizeOverride("font_size", 14);
        unkFormula.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        unkFormula.CustomMinimumSize = new Vector2(0, 20);
        contentArea.AddChild(unkFormula);

        // Note: Unknown uses corrected scores
        var unkNote = new Label { Text = I18n.Tr("未知公式备注") };
        unkNote.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.2f));
        unkNote.AddThemeFontSizeOverride("font_size", 12);
        unkNote.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        unkNote.CustomMinimumSize = new Vector2(0, 16);
        contentArea.AddChild(unkNote);

        // Formula — path total score (how danger and reward combine)
        var pathFormula = new Label { Text = I18n.Tr("路径总分公式") };
        pathFormula.AddThemeColorOverride("font_color", Gold);
        pathFormula.AddThemeFontSizeOverride("font_size", 14);
        pathFormula.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        pathFormula.CustomMinimumSize = new Vector2(0, 20);
        contentArea.AddChild(pathFormula);

        // Danger coefficient explanation
        var coeffNote = new Label { Text = I18n.Tr("危险系数说明") };
        coeffNote.AddThemeColorOverride("font_color", new Color(1f, 0.42f, 0.21f, 0.6f));
        coeffNote.AddThemeFontSizeOverride("font_size", 13);
        coeffNote.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        coeffNote.CustomMinimumSize = new Vector2(0, 18);
        contentArea.AddChild(coeffNote);

        // Variable legend
        var legend = new Label { Text = I18n.Tr("公式变量说明") };
        legend.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.2f));
        legend.AddThemeFontSizeOverride("font_size", 12);
        legend.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        legend.CustomMinimumSize = new Vector2(0, 16);
        contentArea.AddChild(legend);

        // Note: base scores are pre-modifier values
        var note = new Label { Text = I18n.Tr("基础分备注") };
        note.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.25f));
        note.AddThemeFontSizeOverride("font_size", 13);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        note.CustomMinimumSize = new Vector2(0, 16);
        contentArea.AddChild(note);

        // Column headers
        var headerRow = new HBoxContainer();
        headerRow.AddThemeConstantOverride("separation", 8);
        headerRow.CustomMinimumSize = new Vector2(0, 28);

        headerRow.AddChild(MakeHeaderLabel("", NameColWidth)); // spacer
        var dh = MakeHeaderLabel("◆", EditColWidth);
        dh.HorizontalAlignment = HorizontalAlignment.Center;
        dh.AddThemeColorOverride("font_color", WarmOrange);
        headerRow.AddChild(dh);
        var sp = MakeHeaderLabel("", 12);
        sp.CustomMinimumSize = new Vector2(12, 0);
        headerRow.AddChild(sp);
        var rh = MakeHeaderLabel("★", EditColWidth);
        rh.HorizontalAlignment = HorizontalAlignment.Center;
        rh.AddThemeColorOverride("font_color", IceBlue);
        headerRow.AddChild(rh);
        contentArea.AddChild(headerRow);

        // Scrollable type rows
        var scroll = new ScrollContainer();
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Disabled;
        scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        scroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 0);
        scroll.AddChild(rows);

        foreach (var (typeKey, i18nKey, color) in _types)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            row.CustomMinimumSize = new Vector2(0, RowHeight);
            row.Alignment = BoxContainer.AlignmentMode.Center;

            var nameLabel = new Label { Text = I18n.Tr(i18nKey) };
            nameLabel.AddThemeColorOverride("font_color", color);
            nameLabel.AddThemeFontSizeOverride("font_size", 16);
            nameLabel.CustomMinimumSize = new Vector2(NameColWidth, 0);
            nameLabel.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(nameLabel);

            var dEdit = CreateScoreEdit();
            _dangerEdits[typeKey] = dEdit;
            dEdit.TextSubmitted += (text) => OnEditSubmit(typeKey, text, isDanger: true);
            dEdit.FocusExited += () => OnEditFocusExit(typeKey, dEdit.Text, isDanger: true);
            row.AddChild(dEdit);

            // Spacer between columns
            row.AddChild(new Control { CustomMinimumSize = new Vector2(12, 0) });

            var rEdit = CreateScoreEdit();
            _rewardEdits[typeKey] = rEdit;
            rEdit.TextSubmitted += (text) => OnEditSubmit(typeKey, text, isDanger: false);
            rEdit.FocusExited += () => OnEditFocusExit(typeKey, rEdit.Text, isDanger: false);
            row.AddChild(rEdit);

            rows.AddChild(row);
        }

        // Unknown computed row (read-only — weighted average of the types above)
        var unkRow = new HBoxContainer();
        unkRow.AddThemeConstantOverride("separation", 8);
        unkRow.CustomMinimumSize = new Vector2(0, RowHeight);
        unkRow.Alignment = BoxContainer.AlignmentMode.Center;

        var unkNameLabel = new Label { Text = I18n.Tr("未知") };
        unkNameLabel.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.6f));
        unkNameLabel.AddThemeFontSizeOverride("font_size", 16);
        unkNameLabel.CustomMinimumSize = new Vector2(NameColWidth, 0);
        unkNameLabel.VerticalAlignment = VerticalAlignment.Center;
        unkRow.AddChild(unkNameLabel);

        _unknownDangerLabel = new Label { Text = "0" };
        _unknownDangerLabel.AddThemeColorOverride("font_color", WarmOrange);
        _unknownDangerLabel.AddThemeFontSizeOverride("font_size", 16);
        _unknownDangerLabel.CustomMinimumSize = new Vector2(EditColWidth, 0);
        _unknownDangerLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _unknownDangerLabel.VerticalAlignment = VerticalAlignment.Center;
        unkRow.AddChild(_unknownDangerLabel);

        unkRow.AddChild(new Control { CustomMinimumSize = new Vector2(12, 0) });

        _unknownRewardLabel = new Label { Text = "0" };
        _unknownRewardLabel.AddThemeColorOverride("font_color", IceBlue);
        _unknownRewardLabel.AddThemeFontSizeOverride("font_size", 16);
        _unknownRewardLabel.CustomMinimumSize = new Vector2(EditColWidth, 0);
        _unknownRewardLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _unknownRewardLabel.VerticalAlignment = VerticalAlignment.Center;
        unkRow.AddChild(_unknownRewardLabel);

        rows.AddChild(unkRow);

        contentArea.AddChild(scroll);

        // Bottom button row
        var buttonRow = new HBoxContainer();
        buttonRow.AddThemeConstantOverride("separation", 12);
        buttonRow.Alignment = BoxContainer.AlignmentMode.Center;

        // Reset to default button
        var resetBtn = CreateBottomButton(I18n.Tr("重置默认"), new Color(Gold.R, Gold.G, Gold.B, 0.9f));
        resetBtn.Pressed += ResetToDefault;
        buttonRow.AddChild(resetBtn);

        // Close button
        var closeBtn = CreateBottomButton(I18n.Tr("关闭"), new Color(0.91f, 0.864f, 0.746f, 1f)); // cream
        closeBtn.Pressed += Close;
        buttonRow.AddChild(closeBtn);

        contentArea.AddChild(buttonRow);

        // Position the panel centered
        var viewportSize = GetViewportRect().Size;
        borderPanel.Position = new Vector2(
            (viewportSize.X - panelW) / 2f,
            (viewportSize.Y - panelH) / 2f);

        Resized += () =>
        {
            var vs = GetViewportRect().Size;
            borderPanel.Position = new Vector2((vs.X - panelW) / 2f, (vs.Y - panelH) / 2f);
        };

        LoadCurrentScores();
    }

    private static Label MakeHeaderLabel(string text, float width)
    {
        var lbl = new Label { Text = text };
        lbl.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.4f));
        lbl.AddThemeFontSizeOverride("font_size", 14);
        lbl.CustomMinimumSize = new Vector2(width, 0);
        return lbl;
    }

    private static LineEdit CreateScoreEdit()
    {
        var edit = new LineEdit();
        edit.CustomMinimumSize = new Vector2(EditColWidth, 34);
        edit.MouseFilter = MouseFilterEnum.Stop;
        edit.AddThemeFontSizeOverride("font_size", 16);
        edit.AddThemeColorOverride("font_color", Colors.White);
        edit.Set("alignment", (int)HorizontalAlignment.Center);

        var bg = new StyleBoxFlat
        {
            BgColor = new Color(0f, 0f, 0f, 0.35f),
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            BorderColor = new Color(1f, 1f, 1f, 0.1f),
        };
        bg.SetCornerRadiusAll(4);
        edit.AddThemeStyleboxOverride("normal", bg);

        var focusBg = new StyleBoxFlat
        {
            BgColor = new Color(0f, 0f, 0f, 0.5f),
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            BorderColor = new Color(1f, 1f, 1f, 0.15f),
        };
        focusBg.SetCornerRadiusAll(4);
        edit.AddThemeStyleboxOverride("focus", focusBg);

        return edit;
    }

    private void LoadCurrentScores()
    {
        var baseScores = RouteScoringConfig.Current.BaseScores;
        foreach (var (typeKey, _, _) in _types)
        {
            if (_dangerEdits.TryGetValue(typeKey, out var de) &&
                _rewardEdits.TryGetValue(typeKey, out var re))
            {
                if (baseScores.TryGetValue(typeKey, out var entry))
                {
                    de.Text = ((int)entry.Danger).ToString();
                    re.Text = ((int)entry.Reward).ToString();
                }
            }
        }
        UpdateUnknownDisplay();
    }

    private void UpdateUnknownDisplay()
    {
        var config = RouteScoringConfig.Current;
        var weights = config.UnknownHookScoring.BaseOddsWeights;
        double totalWeight = 0, dangerSum = 0, rewardSum = 0;

        foreach (var (typeKey, _, _) in _types)
        {
            if (weights.TryGetValue(typeKey, out var w) && w > 0 &&
                config.BaseScores.TryGetValue(typeKey, out var entry))
            {
                dangerSum += w * entry.Danger;
                rewardSum += w * entry.Reward;
                totalWeight += w;
            }
        }

        if (_unknownDangerLabel == null || _unknownRewardLabel == null) return;
        if (totalWeight > 0)
        {
            _unknownDangerLabel.Text = (dangerSum / totalWeight).ToString("F1");
            _unknownRewardLabel.Text = (rewardSum / totalWeight).ToString("F1");
        }
    }

    private void OnEditSubmit(string typeKey, string text, bool isDanger)
    {
        int value = ParseInt(text);
        ApplyValue(typeKey, isDanger, value);
        RefreshEditText(typeKey, isDanger);
    }

    private void OnEditFocusExit(string typeKey, string text, bool isDanger)
    {
        int value = ParseInt(text);
        ApplyValue(typeKey, isDanger, value);
        RefreshEditText(typeKey, isDanger);
    }

    private void RefreshEditText(string typeKey, bool isDanger)
    {
        var config = RouteScoringConfig.Current;
        if (!config.BaseScores.TryGetValue(typeKey, out var entry)) return;
        if (isDanger && _dangerEdits.TryGetValue(typeKey, out var de))
            de.Text = ((int)entry.Danger).ToString();
        else if (!isDanger && _rewardEdits.TryGetValue(typeKey, out var re))
            re.Text = ((int)entry.Reward).ToString();
    }

    private void ApplyValue(string typeKey, bool isDanger, int value)
    {
        value = Math.Clamp(value, 0, 200);

        var config = RouteScoringConfig.Current;
        if (!config.BaseScores.TryGetValue(typeKey, out var entry))
        {
            entry = new ScoreEntry();
            config.BaseScores[typeKey] = entry;
        }

        if (isDanger)
            entry.Danger = value;
        else
            entry.Reward = value;

        SaveConfig();
        UpdateUnknownDisplay();
        RoutePlannerInstance.Instance?.MarkDirty();
    }

    private static int ParseInt(string text)
    {
        if (int.TryParse(text?.Trim(), out var v)) return v;
        return 0;
    }

    private static void SaveConfig()
    {
        try
        {
            var path = RouteScoringConfig.ConfigPath;
            var dir = Path.GetDirectoryName(path);
            if (dir != null) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(RouteScoringConfig.Current, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch
        {
            ModLogger.Error("Failed to save scoring config");
        }
    }

    public static BaseScoreEditorPopup? Open(BaseScoreEditorPopup? existing, Node? owner = null)
    {
        existing?.QueueFree();
        var popup = new BaseScoreEditorPopup();
        if (owner != null)
            owner.TreeExiting += () => popup.QueueFree();
        var root = Engine.GetMainLoop() as SceneTree;
        var currentScene = root?.CurrentScene;
        if (currentScene != null)
        {
            currentScene.AddChild(popup);
            return popup;
        }
        return null;
    }

    private static Button CreateBottomButton(string text, Color textColor)
    {
        var btn = new Button { Text = text };
        btn.CustomMinimumSize = new Vector2(160, 40);
        btn.AddThemeFontSizeOverride("font_size", 18);
        btn.AddThemeColorOverride("font_color", textColor);

        var btnNormal = new StyleBoxFlat
        {
            BgColor = new Color(0.15f, 0.18f, 0.25f, 0.6f),
            BorderWidthLeft = 0,
            BorderWidthRight = 0,
            BorderWidthTop = 0,
            BorderWidthBottom = 0,
        };
        btnNormal.SetCornerRadiusAll(6);
        btn.AddThemeStyleboxOverride("normal", btnNormal);

        var btnHover = new StyleBoxFlat
        {
            BgColor = new Color(0.22f, 0.26f, 0.35f, 0.8f),
            BorderWidthLeft = 0,
            BorderWidthRight = 0,
            BorderWidthTop = 0,
            BorderWidthBottom = 0,
        };
        btnHover.SetCornerRadiusAll(6);
        btn.AddThemeStyleboxOverride("hover", btnHover);

        var btnPressed = new StyleBoxFlat
        {
            BgColor = new Color(0.1f, 0.12f, 0.18f, 0.9f),
            BorderWidthLeft = 0,
            BorderWidthRight = 0,
            BorderWidthTop = 0,
            BorderWidthBottom = 0,
        };
        btnPressed.SetCornerRadiusAll(6);
        btn.AddThemeStyleboxOverride("pressed", btnPressed);
        btn.AddThemeStyleboxOverride("focus", btnHover);

        return btn;
    }

    private void ResetToDefault()
    {
        var defaults = new Dictionary<string, (double Danger, double Reward)>
        {
            ["Monster"]   = (30, 15),
            ["Elite"]     = (100, 30),
            ["RestSite"]  = (0,   35),
            ["Shop"]      = (0,   45),
            ["Treasure"]  = (0,   50),
            ["Event"]     = (10, 45),
        };

        var config = RouteScoringConfig.Current;
        foreach (var (key, (danger, reward)) in defaults)
        {
            if (!config.BaseScores.TryGetValue(key, out var entry))
            {
                entry = new ScoreEntry();
                config.BaseScores[key] = entry;
            }
            entry.Danger = danger;
            entry.Reward = reward;

            if (_dangerEdits.TryGetValue(key, out var de))
                de.Text = ((int)danger).ToString();
            if (_rewardEdits.TryGetValue(key, out var re))
                re.Text = ((int)reward).ToString();
        }

        SaveConfig();
        UpdateUnknownDisplay();
        RoutePlannerInstance.Instance?.MarkDirty();
    }

    private void Close()
    {
        QueueFree();
    }
}
