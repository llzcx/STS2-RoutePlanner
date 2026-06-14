using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Map;

namespace RoutePlanner;

public partial class UIRoutePlannerPanel : Control
{
    private readonly RoutePlannerInstance _instance;

    // --- Color palette ---
    private static readonly Color DeepSpaceBg = new(0.043f, 0.055f, 0.102f, 0.92f);
    private static readonly Color PanelBorder = new(0.118f, 0.141f, 0.200f, 1f);
    private static readonly Color StarWhite = new(0.784f, 0.816f, 0.878f);
    private static readonly Color Gold = new(0.722f, 0.588f, 0.290f);
    private static readonly Color WarmOrange = new(1f, 0.42f, 0.21f);
    private static readonly Color IceBlue = new(0.30f, 0.65f, 1f);
    private static readonly Color LimeGreen = new(0.29f, 0.87f, 0.50f);
    private static readonly Color SoftPurple = new(0.655f, 0.545f, 0.980f);

    // Dimension checkboxes
    private CheckBox? _dangerCheckBox;
    private CheckBox? _rewardCheckBox;

    // Sliders
    private HSlider? _dangerSlider;
    private HSlider? _rewardSlider;
    private Label? _dangerLabel;
    private Label? _rewardLabel;

    // Weight display
    private VBoxContainer? _weightsList;

    // Route labels
    private Label? _balancedLabel;
    private Label? _highRewardLabel;
    private Label? _safeLabel;
    private Button? _balancedBtn;
    private Button? _highRewardBtn;
    private Button? _safeBtn;
    private Button? _directedBtn;
    private Label? _directedLabel;

    // Action buttons
    private Button? _drawBtn;
    private Button? _clearBtn;
    private Button? _collapseBtn;
    private Button? _langBtn;
    private Button? _autoDrawBtn;

    // Tooltip
    private PanelContainer? _tooltip;
    private Label? _tooltipTitle;
    private Label? _tooltipDesc;
    private Tween? _tooltipTween;

    // I18n
    private readonly List<(Control control, string key)> _i18nRegistry = new();

    // State
    private bool _isCollapsed;
    private int _selectedRoute;

    public UIRoutePlannerPanel(RoutePlannerInstance instance)
    {
        _instance = instance;
        Name = "RoutePlannerPanel";
        BuildUI();
        I18n.LanguageChanged += OnLanguageChanged;
    }

    public void SetSliderValues(double dangerWeight, double rewardWeight)
    {
        if (_dangerSlider != null) _dangerSlider.Value = dangerWeight * 100;
        if (_rewardSlider != null) _rewardSlider.Value = rewardWeight * 100;
    }

    public void RefreshRoutes()
    {
        if (_balancedLabel != null) _balancedLabel.Text = _instance.GetRouteLabel(0);
        if (_directedLabel != null) _directedLabel.Text = _instance.GetRouteLabel(1);
        if (_highRewardLabel != null) _highRewardLabel.Text = _instance.GetRouteLabel(2);
        if (_safeLabel != null) _safeLabel.Text = _instance.GetRouteLabel(3);
        UpdateRouteSelection();
        UpdateDrawButtonState();
    }

    private void OnLanguageChanged()
    {
        RefreshAllText();
        _instance.MarkDirty();
        UpdateLangButtonText();
        UpdateToggleButton(_autoDrawBtn, _instance.AutoDraw);
    }

    private void OnLanguageToggle()
    {
        string next = I18n.CurrentLang == "zh" ? "en" : "zh";
        I18n.SetLanguage(next);
    }

    private void UpdateLangButtonText()
    {
        if (_langBtn != null)
            _langBtn.Text = I18n.CurrentLang == "zh" ? "中" : "EN";
    }

    private void RefreshAllText()
    {
        foreach (var (control, key) in _i18nRegistry)
        {
            if (control is Label label)
                label.Text = I18n.Tr(key);
            else if (control is Button button)
                button.Text = I18n.Tr(key);
        }
        RefreshWeights();
    }

    private Label CreateLocalizedLabel(string key, int fontSize, Color color)
    {
        var label = CreateLabel(I18n.Tr(key), fontSize, color);
        _i18nRegistry.Add((label, key));
        return label;
    }

    private void RegisterButtonI18n(Button button, string key)
    {
        button.Text = I18n.Tr(key);
        _i18nRegistry.Add((button, key));
    }

    private static readonly (string typeKey, string i18nKey, Color color)[] _weightTypes =
    {
        ("Elite",    "精英", new Color(1f, 0.45f, 0f)),
        ("Monster",  "怪物", new Color(0.9f, 0.55f, 0.3f)),
        ("RestSite", "休息", new Color(0.3f, 0.85f, 0.3f)),
        ("Shop",     "商店", new Color(0.85f, 0.75f, 0.2f)),
        ("Treasure", "宝箱", new Color(0.25f, 0.75f, 0.85f)),
        ("Unknown",  "未知", new Color(0.55f, 0.55f, 0.6f)),
    };

    public void RefreshWeights()
    {
        if (_weightsList == null) return;

        while (_weightsList.GetChildCount() > 0)
        {
            var c = _weightsList.GetChild(0);
            _weightsList.RemoveChild(c);
            c.QueueFree();
        }

        var scores = _instance.GetEffectiveTypeScores();
        if (scores.Count == 0) return;

        var priorityOrder = _instance.GetPriorityOrder();
        var orderedTypes = _weightTypes
            .OrderBy(wt =>
            {
                var pt = Enum.Parse<MapPointType>(wt.typeKey);
                var idx = Array.IndexOf(priorityOrder, pt);
                return idx >= 0 ? idx : 99;
            })
            .ToList();

        for (int i = 0; i < orderedTypes.Count; i++)
        {
            var (typeKey, i18nKey, color) = orderedTypes[i];
            if (!scores.TryGetValue(typeKey, out var s)) continue;

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 4);

            // Card content
            var card = new Panel { Name = $"Card_{typeKey}" };
            card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            card.MouseFilter = MouseFilterEnum.Pass;

            var cardStyle = new StyleBoxFlat();
            cardStyle.BgColor = new Color(1f, 1f, 1f, 0.06f);
            cardStyle.SetCornerRadiusAll(6);
            card.AddThemeStyleboxOverride("panel", cardStyle);
            card.CustomMinimumSize = new Vector2(0, 38);

            var content = new HBoxContainer();
            content.AddThemeConstantOverride("separation", 10);
            content.SetAnchorsPreset(LayoutPreset.FullRect);
            content.OffsetLeft = 6;
            content.OffsetRight = -6;

            var nameLabel = new Label { Text = I18n.Tr(i18nKey), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            nameLabel.AddThemeColorOverride("font_color", color);
            nameLabel.AddThemeFontSizeOverride("font_size", 11);
            nameLabel.CustomMinimumSize = new Vector2(40, 0);
            content.AddChild(nameLabel);

            var dLabel = new Label { Text = $"◆{F0(s.danger)}", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            dLabel.AddThemeColorOverride("font_color", WarmOrange);
            dLabel.AddThemeFontSizeOverride("font_size", 10);
            content.AddChild(dLabel);

            var rLabel = new Label { Text = $"★{F0(s.reward)}", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            rLabel.AddThemeColorOverride("font_color", IceBlue);
            rLabel.AddThemeFontSizeOverride("font_size", 10);
            content.AddChild(rLabel);

            // --- Constraint controls (inside card, middle area) ---
            var constraintBox = new HBoxContainer();
            constraintBox.AddThemeConstantOverride("separation", 2);
            content.AddChild(constraintBox);

            var parsedType = Enum.Parse<MapPointType>(typeKey);
            var constraint = _instance.GetConstraint(parsedType);
            string capturedKey = typeKey;

            // Mode cycle button
            var modeBtn = CreateConstraintModeButton(constraint.Mode);
            modeBtn.Pressed += () =>
            {
                var cur = _instance.GetConstraint(parsedType);
                var nextMode = CycleConstraintMode(cur.Mode);

                // Read saved values for the new mode, with sensible defaults if never set
                int savedLower = cur.LowerLimits[(int)nextMode];
                int savedUpper = cur.UpperLimits[(int)nextMode];
                int lower = nextMode switch
                {
                    ConstraintMode.LowerOnly => savedLower > 0 ? savedLower : 1,
                    ConstraintMode.Both => savedLower > 0 ? savedLower : 1,
                    _ => 0,
                };
                int upper = nextMode switch
                {
                    ConstraintMode.UpperOnly => savedUpper > 0 ? savedUpper : 3,
                    ConstraintMode.Both => savedUpper > 0 ? savedUpper : Math.Max(lower + 1, 5),
                    _ => 0,
                };

                // Update mode button symbol immediately (inline, no full rebuild)
                modeBtn.Text = GetConstraintModeSymbol(nextMode);

                // Remove old constraint input children (modeBtn stays at index 0)
                while (constraintBox.GetChildCount() > 1)
                {
                    var old = constraintBox.GetChild(1);
                    constraintBox.RemoveChild(old);
                    old.QueueFree();
                }

                // Add new constraint inputs for the new mode
                if (nextMode != ConstraintMode.None)
                {
                    if (nextMode == ConstraintMode.LowerOnly || nextMode == ConstraintMode.Both)
                    {
                        var newLower = CreateConstraintValueEdit(lower);
                        newLower.TextSubmitted += (text) =>
                        {
                            var c2 = _instance.GetConstraint(parsedType);
                            _instance.OnConstraintChanged(parsedType, c2.Mode, ParseInt(text), c2.UpperLimit);
                        };
                        newLower.FocusExited += () =>
                        {
                            var c2 = _instance.GetConstraint(parsedType);
                            _instance.OnConstraintChanged(parsedType, c2.Mode, ParseInt(newLower.Text), c2.UpperLimit);
                        };
                        constraintBox.AddChild(newLower);
                    }
                    if (nextMode == ConstraintMode.UpperOnly || nextMode == ConstraintMode.Both)
                    {
                        var newUpper = CreateConstraintValueEdit(upper);
                        newUpper.TextSubmitted += (text) =>
                        {
                            var c2 = _instance.GetConstraint(parsedType);
                            _instance.OnConstraintChanged(parsedType, c2.Mode, c2.LowerLimit, ParseInt(text));
                        };
                        newUpper.FocusExited += () =>
                        {
                            var c2 = _instance.GetConstraint(parsedType);
                            _instance.OnConstraintChanged(parsedType, c2.Mode, c2.LowerLimit, ParseInt(newUpper.Text));
                        };
                        constraintBox.AddChild(newUpper);
                    }
                }

                _instance.OnConstraintChanged(parsedType, nextMode, lower, upper);
            };
            constraintBox.AddChild(modeBtn);

            // Constraint value input(s) — only show when mode != None
            if (constraint.Mode != ConstraintMode.None)
            {
                if (constraint.Mode == ConstraintMode.LowerOnly || constraint.Mode == ConstraintMode.Both)
                {
                    var lowerEdit = CreateConstraintValueEdit(constraint.LowerLimit);
                    lowerEdit.TextSubmitted += (text) =>
                    {
                        var c = _instance.GetConstraint(parsedType);
                        _instance.OnConstraintChanged(parsedType, c.Mode, ParseInt(text), c.UpperLimit);
                    };
                    lowerEdit.FocusExited += () =>
                    {
                        var c = _instance.GetConstraint(parsedType);
                        _instance.OnConstraintChanged(parsedType, c.Mode, ParseInt(lowerEdit.Text), c.UpperLimit);
                    };
                    constraintBox.AddChild(lowerEdit);
                }
                if (constraint.Mode == ConstraintMode.UpperOnly || constraint.Mode == ConstraintMode.Both)
                {
                    var upperEdit = CreateConstraintValueEdit(constraint.UpperLimit);
                    upperEdit.TextSubmitted += (text) =>
                    {
                        var c = _instance.GetConstraint(parsedType);
                        _instance.OnConstraintChanged(parsedType, c.Mode, c.LowerLimit, ParseInt(text));
                    };
                    upperEdit.FocusExited += () =>
                    {
                        var c = _instance.GetConstraint(parsedType);
                        _instance.OnConstraintChanged(parsedType, c.Mode, c.LowerLimit, ParseInt(upperEdit.Text));
                    };
                    constraintBox.AddChild(upperEdit);
                }
            }

            card.AddChild(content);
            row.AddChild(card);

            // Up / Down buttons
            var canMoveUp = i > 0;
            var canMoveDown = i < orderedTypes.Count - 1;

            var upBtn = CreateArrowButton("▲", canMoveUp);
            upBtn.Pressed += () => MovePriority(capturedKey, -1);
            row.AddChild(upBtn);

            var downBtn = CreateArrowButton("▼", canMoveDown);
            downBtn.Pressed += () => MovePriority(capturedKey, 1);
            row.AddChild(downBtn);

            _weightsList.AddChild(row);
        }
    }

    private static ConstraintMode CycleConstraintMode(ConstraintMode current) => current switch
    {
        ConstraintMode.None => ConstraintMode.UpperOnly,
        ConstraintMode.UpperOnly => ConstraintMode.LowerOnly,
        ConstraintMode.LowerOnly => ConstraintMode.Both,
        ConstraintMode.Both => ConstraintMode.None,
        _ => ConstraintMode.None,
    };

    private static string GetConstraintModeSymbol(ConstraintMode mode) => mode switch
    {
        ConstraintMode.UpperOnly => "≤",
        ConstraintMode.LowerOnly => "≥",
        ConstraintMode.Both => "⇅",
        _ => "—",
    };

    private static int ParseInt(string text)
    {
        if (int.TryParse(text, out var v) && v >= 0) return v;
        return 0;
    }

    private Button CreateConstraintModeButton(ConstraintMode mode)
    {
        var btn = new Button
        {
            Text = GetConstraintModeSymbol(mode),
            CustomMinimumSize = new Vector2(22, 18),
            TooltipText = I18n.Tr("约束模式提示"),
        };
        btn.AddThemeFontSizeOverride("font_size", 9);
        btn.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.55f));

        var flatStyle = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0) };
        btn.AddThemeStyleboxOverride("normal", flatStyle);
        var hoverStyle = new StyleBoxFlat { BgColor = new Color(1f, 1f, 1f, 0.08f) };
        btn.AddThemeStyleboxOverride("hover", hoverStyle);
        btn.AddThemeStyleboxOverride("pressed", flatStyle);
        btn.AddThemeStyleboxOverride("focus", flatStyle);

        return btn;
    }

    private static LineEdit CreateConstraintValueEdit(int value)
    {
        var edit = new LineEdit
        {
            Text = value > 0 ? value.ToString() : "",
            CustomMinimumSize = new Vector2(26, 18),
            MouseFilter = MouseFilterEnum.Stop,
        };
        edit.AddThemeFontSizeOverride("font_size", 9);
        edit.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.7f));

        var bgStyle = new StyleBoxFlat
        {
            BgColor = new Color(0f, 0f, 0f, 0.25f),
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            BorderColor = new Color(1f, 1f, 1f, 0.1f),
        };
        bgStyle.SetCornerRadiusAll(3);
        edit.AddThemeStyleboxOverride("normal", bgStyle);

        var focusStyle = new StyleBoxFlat
        {
            BgColor = new Color(0f, 0f, 0f, 0.35f),
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            BorderColor = new Color(Gold.R, Gold.G, Gold.B, 0.3f),
        };
        focusStyle.SetCornerRadiusAll(3);
        edit.AddThemeStyleboxOverride("focus", focusStyle);

        return edit;
    }

    private void MovePriority(string typeKey, int direction)
    {
        var order = new List<MapPointType>(_instance.GetPriorityOrder());
        var enumVal = Enum.Parse<MapPointType>(typeKey);
        int idx = order.IndexOf(enumVal);
        if (idx < 0) return;

        int newIdx = idx + direction;
        if (newIdx < 0 || newIdx >= order.Count) return;

        // Play swap animation
        if (idx < _weightsList!.GetChildCount() && newIdx < _weightsList.GetChildCount())
        {
            var rowA = _weightsList.GetChild(idx) as Control;
            var rowB = _weightsList.GetChild(newIdx) as Control;
            if (rowA != null && rowB != null)
            {
                var tween = CreateTween();
                tween.SetParallel();
                // Fade out from center
                tween.TweenProperty(rowA, "modulate:a", 0.2f, 0.12f);
                tween.TweenProperty(rowB, "modulate:a", 0.2f, 0.12f);
                tween.Finished += () =>
                {
                    (order[idx], order[newIdx]) = (order[newIdx], order[idx]);
                    _instance.OnPriorityOrderChanged(order.ToArray());
                };
            }
        }
        else
        {
            (order[idx], order[newIdx]) = (order[newIdx], order[idx]);
            _instance.OnPriorityOrderChanged(order.ToArray());
        }
    }

    private static string F0(double v) => v.ToString("F0");

    private void BuildUI()
    {
        MouseFilter = MouseFilterEnum.Stop;

        // Panel background — deep space with border
        var bg = new Panel();
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        var bgStyle = new StyleBoxFlat();
        bgStyle.BgColor = DeepSpaceBg;
        bgStyle.SetCornerRadiusAll(12);
        bgStyle.BorderWidthLeft = 1;
        bgStyle.BorderWidthRight = 1;
        bgStyle.BorderWidthTop = 1;
        bgStyle.BorderWidthBottom = 1;
        bgStyle.BorderColor = PanelBorder;
        bg.AddThemeStyleboxOverride("panel", bgStyle);
        AddChild(bg);

        // Top accent line: warm-orange → ice-blue gradient hint
        var accentLine = new ColorRect();
        accentLine.SetAnchorsPreset(LayoutPreset.TopWide);
        accentLine.Color = WarmOrange;
        accentLine.CustomMinimumSize = new Vector2(0, 2);
        accentLine.OffsetLeft = 8;
        accentLine.OffsetRight = -8;
        accentLine.OffsetTop = 0;
        AddChild(accentLine);

        var container = new VBoxContainer();
        container.SetAnchorsPreset(LayoutPreset.FullRect);
        container.AddThemeConstantOverride("separation", 6);
        container.OffsetLeft = 8;
        container.OffsetRight = -8;
        container.OffsetTop = 8;
        container.OffsetBottom = -8;
        AddChild(container);

        // --- Header ---
        var header = new HBoxContainer();
        var title = CreateLocalizedLabel("◇ 路线导航仪", 14, StarWhite);
        title.AddThemeFontSizeOverride("font_size", 14);
        header.AddChild(title);
        header.AddChild(new Control { CustomMinimumSize = new Vector2(10, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill });

        // Auto-draw toggle ("自绘")
        _autoDrawBtn = CreateToggleButton();
        UpdateToggleButton(_autoDrawBtn, _instance.AutoDraw);
        _autoDrawBtn.CustomMinimumSize = new Vector2(28, 22);
        _autoDrawBtn.Pressed += OnAutoDrawToggled;
        _autoDrawBtn.TooltipText = I18n.Tr("自动绘制提示");
        header.AddChild(_autoDrawBtn);

        _langBtn = CreateIconButton(I18n.CurrentLang == "zh" ? "中" : "EN", 9);
        _langBtn.CustomMinimumSize = new Vector2(22, 22);
        _langBtn.Pressed += OnLanguageToggle;
        header.AddChild(_langBtn);

        _collapseBtn = CreateIconButton("⊟", 22);
        _collapseBtn.CustomMinimumSize = new Vector2(22, 22);
        _collapseBtn.Pressed += OnCollapsePressed;
        header.AddChild(_collapseBtn);
        container.AddChild(header);

        // --- Content wrapper (collapsible, scrollable) ---
        var scroll = new ScrollContainer { Name = "Content" };
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        scroll.VerticalScrollMode = ScrollContainer.ScrollMode.ShowAlways;
        scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;

        // Slim scrollbar
        var vScroll = scroll.GetVScrollBar();
        vScroll.CustomMinimumSize = new Vector2(4, 0);
        var scrollBg = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0) };
        vScroll.AddThemeStyleboxOverride("scroll", scrollBg);
        var scrollGrabber = new StyleBoxFlat { BgColor = new Color(1f, 1f, 1f, 0.15f) };
        scrollGrabber.SetCornerRadiusAll(2);
        vScroll.AddThemeStyleboxOverride("grabber", scrollGrabber);
        var scrollGrabberHover = new StyleBoxFlat { BgColor = new Color(1f, 1f, 1f, 0.25f) };
        scrollGrabberHover.SetCornerRadiusAll(2);
        vScroll.AddThemeStyleboxOverride("grabber_highlight", scrollGrabberHover);

        var content = new VBoxContainer();
        content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        content.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(content);
        container.AddChild(scroll);

        // --- Weight sliders ---
        content.AddChild(CreateSectionHeader("导航参数"));

        // Danger slider
        var dangerHeader = new HBoxContainer();
        dangerHeader.AddThemeConstantOverride("separation", 2);
        _dangerCheckBox = new CheckBox();
        _dangerCheckBox.ButtonPressed = true;
        _dangerCheckBox.Toggled += OnDangerCheckToggled;
        dangerHeader.AddChild(_dangerCheckBox);
        dangerHeader.AddChild(CreateLocalizedLabel("危险容限", 12, WarmOrange));
        content.AddChild(dangerHeader);

        var dangerBox = new HBoxContainer();
        dangerBox.AddThemeConstantOverride("separation", 4);
        _dangerSlider = CreateSlider(0);
        _dangerSlider.ValueChanged += (v) =>
        {
            _instance.OnWeightChanged(v / 100.0, _rewardSlider?.Value / 100.0 ?? 0.5);
            UpdateSliderLabels();
        };
        dangerBox.AddChild(_dangerSlider);
        _dangerLabel = CreateLabel("0%", 11, WarmOrange);
        _dangerLabel.CustomMinimumSize = new Vector2(38, 0);
        _dangerLabel.HorizontalAlignment = HorizontalAlignment.Right;
        dangerBox.AddChild(_dangerLabel);
        content.AddChild(dangerBox);

        // Reward slider
        var rewardHeader = new HBoxContainer();
        rewardHeader.AddThemeConstantOverride("separation", 2);
        _rewardCheckBox = new CheckBox();
        _rewardCheckBox.ButtonPressed = true;
        _rewardCheckBox.Toggled += OnRewardCheckToggled;
        rewardHeader.AddChild(_rewardCheckBox);
        rewardHeader.AddChild(CreateLocalizedLabel("收益渴求", 12, IceBlue));
        content.AddChild(rewardHeader);

        var rewardBox = new HBoxContainer();
        rewardBox.AddThemeConstantOverride("separation", 4);
        _rewardSlider = CreateSlider(50);
        _rewardSlider.ValueChanged += (v) =>
        {
            _instance.OnWeightChanged(_dangerSlider?.Value / 100.0 ?? 0.5, v / 100.0);
            UpdateSliderLabels();
        };
        rewardBox.AddChild(_rewardSlider);
        _rewardLabel = CreateLabel("50%", 11, IceBlue);
        _rewardLabel.CustomMinimumSize = new Vector2(38, 0);
        _rewardLabel.HorizontalAlignment = HorizontalAlignment.Right;
        rewardBox.AddChild(_rewardLabel);
        content.AddChild(rewardBox);

        // Preset buttons — segmented control style
        var presetBox = new HBoxContainer();
        presetBox.AddThemeConstantOverride("separation", 0);
        var consBtn = CreatePresetButton("保守");
        consBtn.Pressed += () => _instance.OnPresetSelected("conservative");
        RegisterButtonI18n(consBtn, "保守");
        presetBox.AddChild(consBtn);
        var safeBtn = CreatePresetButton("求稳");
        safeBtn.Pressed += () => _instance.OnPresetSelected("safe_reward");
        RegisterButtonI18n(safeBtn, "求稳");
        presetBox.AddChild(safeBtn);
        var balBtn = CreatePresetButton("均衡");
        balBtn.Pressed += () => _instance.OnPresetSelected("balanced");
        RegisterButtonI18n(balBtn, "均衡");
        presetBox.AddChild(balBtn);
        var aggBtn = CreatePresetButton("激进");
        aggBtn.Pressed += () => _instance.OnPresetSelected("aggressive");
        RegisterButtonI18n(aggBtn, "激进");
        presetBox.AddChild(aggBtn);
        var extBtn = CreatePresetButton("极端");
        extBtn.Pressed += () => _instance.OnPresetSelected("extreme");
        RegisterButtonI18n(extBtn, "极端");
        presetBox.AddChild(extBtn);
        content.AddChild(presetBox);

        // --- Node type weights ---
        content.AddChild(CreateSectionHeader("星图数据"));
        _weightsList = new VBoxContainer { Name = "WeightsList" };
        _weightsList.AddThemeConstantOverride("separation", 4);
        content.AddChild(_weightsList);

        // --- Route list ---
        content.AddChild(CreateSectionHeader("航线模式选择"));

        _balancedBtn = CreateRouteButton("◉ 自定义", "自定义_desc", SoftPurple, out _balancedLabel);
        _balancedBtn.Pressed += () => OnRouteButtonPressed(0);
        content.AddChild(_balancedBtn);

        _directedBtn = CreateRouteButton("★ 定向", "定向_desc", WarmOrange, out _directedLabel);
        _directedBtn.Pressed += () => OnRouteButtonPressed(1);
        content.AddChild(_directedBtn);

        _highRewardBtn = CreateRouteButton("◆ 高收益", "高收益_desc", Colors.Gold, out _highRewardLabel);
        _highRewardBtn.Pressed += () => OnRouteButtonPressed(2);
        content.AddChild(_highRewardBtn);

        _safeBtn = CreateRouteButton("● 保守", "保守_desc", LimeGreen, out _safeLabel);
        _safeBtn.Pressed += () => OnRouteButtonPressed(3);
        content.AddChild(_safeBtn);

        // --- Action buttons ---
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 6);
        _drawBtn = CreateActionButton("✦ 绘制航线", WarmOrange, primary: true);
        _drawBtn.Pressed += () => _instance.OnDrawClicked();
        RegisterButtonI18n(_drawBtn, "✦ 绘制航线");
        actions.AddChild(_drawBtn);
        _clearBtn = CreateActionButton("✧ 清除", Gold, primary: false);
        _clearBtn.Pressed += () => _instance.OnClearClicked();
        RegisterButtonI18n(_clearBtn, "✧ 清除");
        actions.AddChild(_clearBtn);
        content.AddChild(actions);

        // --- Tooltip overlay ---
        BuildTooltip();

        // Set panel position and size
        SetAnchorsPreset(LayoutPreset.TopRight);
        OffsetLeft = -420;
        OffsetRight = 0;
        OffsetTop = 90;
        OffsetBottom = 800;
        CustomMinimumSize = new Vector2(400, 640);
    }

    // --- Event handlers (unchanged) ---

    private void OnDangerCheckToggled(bool on)
    {
        if (!on && (_rewardCheckBox == null || !_rewardCheckBox.ButtonPressed))
        {
            _dangerCheckBox!.ButtonPressed = true;
            return;
        }
        NotifyDimensionChanged();
    }

    private void OnRewardCheckToggled(bool on)
    {
        if (!on && (_dangerCheckBox == null || !_dangerCheckBox.ButtonPressed))
        {
            _rewardCheckBox!.ButtonPressed = true;
            return;
        }
        NotifyDimensionChanged();
    }

    private void NotifyDimensionChanged()
    {
        _instance.OnDimensionChanged(
            _dangerCheckBox?.ButtonPressed ?? true,
            _rewardCheckBox?.ButtonPressed ?? false);
    }

    private void UpdateSliderLabels()
    {
        if (_dangerLabel != null && _dangerSlider != null)
            _dangerLabel.Text = $"{(int)_dangerSlider.Value}%";
        if (_rewardLabel != null && _rewardSlider != null)
            _rewardLabel.Text = $"{(int)_rewardSlider.Value}%";
    }

    private void OnRouteButtonPressed(int index)
    {
        _selectedRoute = index;
        _instance.OnRouteSelected(index);
        UpdateRouteSelection();
        UpdateDrawButtonState();
        _instance.TryAutoDraw();
    }

    private void UpdateRouteSelection()
    {
        if (_balancedBtn != null) _balancedBtn.ButtonPressed = _selectedRoute == 0;
        if (_directedBtn != null) _directedBtn.ButtonPressed = _selectedRoute == 1;
        if (_highRewardBtn != null) _highRewardBtn.ButtonPressed = _selectedRoute == 2;
        if (_safeBtn != null) _safeBtn.ButtonPressed = _selectedRoute == 3;
    }

    private void UpdateDrawButtonState()
    {
        if (_drawBtn == null) return;

        if (_instance.AutoDraw)
        {
            _drawBtn.Disabled = true;
            _drawBtn.Text = I18n.Tr("自动绘制中");
            _drawBtn.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.4f));
            return;
        }

        bool satisfied = _instance.IsSelectedRouteSatisfyingConstraints();
        _drawBtn.Disabled = !satisfied;
        if (!satisfied)
        {
            _drawBtn.Text = $"⚠ {I18n.Tr("无符合路线")}";
            _drawBtn.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.4f));
        }
        else
        {
            _drawBtn.Text = I18n.Tr("✦ 绘制航线");
            _drawBtn.AddThemeColorOverride("font_color", StarWhite);
        }
    }

    private void OnAutoDrawToggled()
    {
        _instance.ToggleAutoDraw();
        bool on = _instance.AutoDraw;
        UpdateToggleButton(_autoDrawBtn, on);
        UpdateDrawButtonState();
        if (on) _instance.TryAutoDraw();
    }

    private void UpdateToggleButton(Button? btn, bool active)
    {
        if (btn == null) return;
        btn.Text = active ? I18n.Tr("自动") : I18n.Tr("手动");
        btn.AddThemeColorOverride("font_color", active ? WarmOrange : new Color(1f, 1f, 1f, 1f));
    }

    private static Button CreateToggleButton()
    {
        var btn = new Button();
        btn.AddThemeFontSizeOverride("font_size", 9);
        var flatStyle = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0) };
        btn.AddThemeStyleboxOverride("normal", flatStyle);
        btn.AddThemeStyleboxOverride("hover", flatStyle);
        btn.AddThemeStyleboxOverride("pressed", flatStyle);
        btn.AddThemeStyleboxOverride("focus", flatStyle);
        return btn;
    }

    private void OnCollapsePressed()
    {
        _isCollapsed = !_isCollapsed;
        var content = FindChild("Content", true, false);
        if (content is Control c)
        {
            if (_isCollapsed)
            {
                var tween = CreateTween();
                tween.TweenProperty(c, "modulate:a", 0f, 0.15f);
                tween.SetEase(Tween.EaseType.InOut);
            }
            else
            {
                c.Modulate = new Color(1, 1, 1, 0);
                c.Visible = true;
                var tween = CreateTween();
                tween.TweenProperty(c, "modulate:a", 1f, 0.2f);
                tween.SetEase(Tween.EaseType.InOut);
                tween.Finished += () => c.Modulate = Colors.White;
            }
        }

        // Collapsed: 40px height = offset_top(90) + 40 = 130; pass clicks through
        float targetBottom = _isCollapsed ? 130f : 800f;
        MouseFilter = _isCollapsed ? MouseFilterEnum.Ignore : MouseFilterEnum.Stop;
        CustomMinimumSize = _isCollapsed ? new Vector2(400, 0) : new Vector2(400, 640);
        var panelTween = CreateTween();
        panelTween.TweenProperty(this, "offset_bottom", targetBottom, 0.22f);
        panelTween.SetEase(Tween.EaseType.InOut);

        if (_collapseBtn != null)
            _collapseBtn.Text = _isCollapsed ? "⊞" : "⊟";
    }

    // --- UI Helper Methods ---

    private static Label CreateLabel(string text, int fontSize, Color color)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        return label;
    }

    private static HSlider CreateSlider(double initialValue)
    {
        return new HSlider
        {
            MinValue = 0,
            MaxValue = 100,
            Value = initialValue,
            Step = 1,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
    }

    private static Button CreateIconButton(string text, int fontSize)
    {
        var btn = new Button { Text = text };
        btn.CustomMinimumSize = new Vector2(22, 22);
        btn.AddThemeFontSizeOverride("font_size", fontSize);
        // Flat style
        var flatStyle = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0) };
        btn.AddThemeStyleboxOverride("normal", flatStyle);
        btn.AddThemeStyleboxOverride("hover", flatStyle);
        btn.AddThemeStyleboxOverride("pressed", flatStyle);
        btn.AddThemeStyleboxOverride("focus", flatStyle);
        return btn;
    }

    private static Button CreatePresetButton(string text)
    {
        var btn = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(44, 22),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        btn.AddThemeFontSizeOverride("font_size", 10);

        var normalStyle = new StyleBoxFlat();
        normalStyle.BgColor = new Color(0, 0, 0, 0);
        normalStyle.BorderWidthLeft = 1;
        normalStyle.BorderWidthRight = 1;
        normalStyle.BorderWidthTop = 1;
        normalStyle.BorderWidthBottom = 1;
        normalStyle.BorderColor = new Color(Gold.R, Gold.G, Gold.B, 0.4f);
        normalStyle.SetCornerRadiusAll(0);
        btn.AddThemeStyleboxOverride("normal", normalStyle);

        var hoverStyle = new StyleBoxFlat();
        hoverStyle.BgColor = new Color(Gold.R, Gold.G, Gold.B, 0.12f);
        hoverStyle.BorderWidthLeft = 1;
        hoverStyle.BorderWidthRight = 1;
        hoverStyle.BorderWidthTop = 1;
        hoverStyle.BorderWidthBottom = 1;
        hoverStyle.BorderColor = new Color(Gold.R, Gold.G, Gold.B, 0.5f);
        hoverStyle.SetCornerRadiusAll(0);
        btn.AddThemeStyleboxOverride("hover", hoverStyle);

        var pressedStyle = new StyleBoxFlat();
        pressedStyle.BgColor = new Color(Gold.R, Gold.G, Gold.B, 0.2f);
        pressedStyle.BorderWidthLeft = 1;
        pressedStyle.BorderWidthRight = 1;
        pressedStyle.BorderWidthTop = 1;
        pressedStyle.BorderWidthBottom = 1;
        pressedStyle.BorderColor = Gold;
        pressedStyle.SetCornerRadiusAll(0);
        btn.AddThemeStyleboxOverride("pressed", pressedStyle);

        return btn;
    }

    private Button CreateRouteButton(string labelKey, string descKey, Color accentColor, out Label routeLabel)
    {
        var btn = new Button();
        btn.ToggleMode = true;
        btn.CustomMinimumSize = new Vector2(0, 42);
        btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        btn.AddThemeFontSizeOverride("font_size", 10);

        // Inner HBox for icon + route text
        var row = new HBoxContainer();
        row.MouseFilter = MouseFilterEnum.Ignore;
        row.AddThemeConstantOverride("separation", 4);
        row.SetAnchorsPreset(LayoutPreset.FullRect);
        row.OffsetLeft = 6;
        row.OffsetRight = -6;

        var iconLabel = new Label { Text = I18n.Tr(labelKey) };
        iconLabel.MouseFilter = MouseFilterEnum.Ignore;
        iconLabel.AddThemeColorOverride("font_color", accentColor);
        iconLabel.AddThemeFontSizeOverride("font_size", 10);
        iconLabel.CustomMinimumSize = new Vector2(50, 0);
        iconLabel.VerticalAlignment = VerticalAlignment.Center;
        _i18nRegistry.Add((iconLabel, labelKey));
        row.AddChild(iconLabel);

        routeLabel = new Label
        {
            Text = I18n.Tr("计算中..."),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            ClipContents = false,
            AutowrapMode = TextServer.AutowrapMode.Word,
        };
        routeLabel.MouseFilter = MouseFilterEnum.Ignore;
        routeLabel.AddThemeColorOverride("font_color", accentColor);
        routeLabel.AddThemeFontSizeOverride("font_size", 10);
        routeLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(routeLabel);

        btn.AddChild(row);

        // Normal
        var normalStyle = new StyleBoxFlat();
        normalStyle.BgColor = new Color(0, 0, 0, 0);
        normalStyle.SetCornerRadiusAll(4);
        normalStyle.BorderWidthLeft = 3;
        normalStyle.BorderColor = new Color(0, 0, 0, 0);
        btn.AddThemeStyleboxOverride("normal", normalStyle);

        // Hover
        var hoverStyle = new StyleBoxFlat();
        hoverStyle.BgColor = new Color(1f, 1f, 1f, 0.04f);
        hoverStyle.SetCornerRadiusAll(4);
        hoverStyle.BorderWidthLeft = 3;
        hoverStyle.BorderColor = new Color(0, 0, 0, 0);
        btn.AddThemeStyleboxOverride("hover", hoverStyle);

        // Pressed (selected)
        var pressedStyle = new StyleBoxFlat();
        pressedStyle.BgColor = new Color(1f, 1f, 1f, 0.06f);
        pressedStyle.SetCornerRadiusAll(4);
        pressedStyle.BorderWidthLeft = 3;
        pressedStyle.BorderColor = accentColor;
        btn.AddThemeStyleboxOverride("pressed", pressedStyle);

        // Tooltip hover
        string capturedLabelKey = labelKey;
        btn.MouseEntered += () =>
        {
            ModLogger.Info($"Tooltip: MouseEntered on '{capturedLabelKey}'");
            ShowTooltip(btn, capturedLabelKey, descKey, accentColor);
        };
        btn.MouseExited += () =>
        {
            ModLogger.Info($"Tooltip: MouseExited on '{capturedLabelKey}'");
            HideTooltip();
        };
        btn.TreeExiting += () => HideTooltip();

        return btn;
    }

    private static Button CreateActionButton(string text, Color accentColor, bool primary)
    {
        var btn = new Button { Text = text };
        btn.CustomMinimumSize = new Vector2(0, 30);
        btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        btn.AddThemeFontSizeOverride("font_size", 11);
        btn.AddThemeColorOverride("font_color", StarWhite);

        var normalStyle = new StyleBoxFlat();
        normalStyle.BgColor = primary ? new Color(accentColor.R, accentColor.G, accentColor.B, 0.12f)
                                      : new Color(0, 0, 0, 0);
        normalStyle.BorderWidthLeft = 1;
        normalStyle.BorderWidthRight = 1;
        normalStyle.BorderWidthTop = 1;
        normalStyle.BorderWidthBottom = 1;
        normalStyle.BorderColor = new Color(accentColor.R, accentColor.G, accentColor.B, 0.45f);
        normalStyle.SetCornerRadiusAll(5);
        btn.AddThemeStyleboxOverride("normal", normalStyle);

        var hoverStyle = new StyleBoxFlat();
        hoverStyle.BgColor = new Color(accentColor.R, accentColor.G, accentColor.B, 0.22f);
        hoverStyle.BorderWidthLeft = 1;
        hoverStyle.BorderWidthRight = 1;
        hoverStyle.BorderWidthTop = 1;
        hoverStyle.BorderWidthBottom = 1;
        hoverStyle.BorderColor = accentColor;
        hoverStyle.SetCornerRadiusAll(5);
        btn.AddThemeStyleboxOverride("hover", hoverStyle);

        var pressedStyle = new StyleBoxFlat();
        pressedStyle.BgColor = new Color(accentColor.R, accentColor.G, accentColor.B, 0.18f);
        pressedStyle.BorderWidthLeft = 1;
        pressedStyle.BorderWidthRight = 1;
        pressedStyle.BorderWidthTop = 1;
        pressedStyle.BorderWidthBottom = 1;
        pressedStyle.BorderColor = new Color(accentColor.R, accentColor.G, accentColor.B, 0.6f);
        pressedStyle.SetCornerRadiusAll(5);
        btn.AddThemeStyleboxOverride("pressed", pressedStyle);

        return btn;
    }

    private Control CreateSectionHeader(string i18nKey)
    {
        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 8);

        var leftLine = new ColorRect
        {
            Color = new Color(Gold.R, Gold.G, Gold.B, 0.3f),
            CustomMinimumSize = new Vector2(20, 1),
        };
        leftLine.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        hbox.AddChild(leftLine);

        var label = CreateLocalizedLabel(i18nKey, 12, Gold);
        hbox.AddChild(label);

        var rightLine = new ColorRect
        {
            Color = new Color(Gold.R, Gold.G, Gold.B, 0.25f),
            CustomMinimumSize = new Vector2(0, 1),
        };
        rightLine.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        rightLine.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        hbox.AddChild(rightLine);

        return hbox;
    }

    private static Button CreateArrowButton(string text, bool enabled)
    {
        var btn = new Button { Text = text, Disabled = !enabled };
        btn.CustomMinimumSize = new Vector2(24, 16);
        btn.AddThemeFontSizeOverride("font_size", 10);
        btn.MouseFilter = enabled ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;

        var normalStyle = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0) };
        btn.AddThemeStyleboxOverride("normal", normalStyle);
        btn.AddThemeStyleboxOverride("hover", normalStyle);
        btn.AddThemeStyleboxOverride("pressed", normalStyle);
        btn.AddThemeStyleboxOverride("focus", normalStyle);
        btn.AddThemeStyleboxOverride("disabled", normalStyle);

        btn.AddThemeColorOverride("font_color", enabled ? new Color(1f, 1f, 1f, 0.6f) : new Color(1f, 1f, 1f, 0.15f));

        return btn;
    }

    // --- Tooltip ---

    private void BuildTooltip()
    {
        var tip = new PanelContainer { Name = "RouteTooltip" };
        tip.MouseFilter = MouseFilterEnum.Ignore;
        tip.Hide();

        var bgStyle = new StyleBoxFlat();
        bgStyle.BgColor = new Color(0.055f, 0.067f, 0.118f, 0.96f);
        bgStyle.SetCornerRadiusAll(8);
        bgStyle.BorderWidthLeft = 1;
        bgStyle.BorderWidthRight = 1;
        bgStyle.BorderWidthTop = 1;
        bgStyle.BorderWidthBottom = 1;
        bgStyle.BorderColor = PanelBorder;
        bgStyle.ContentMarginLeft = 10;
        bgStyle.ContentMarginRight = 10;
        bgStyle.ContentMarginTop = 8;
        bgStyle.ContentMarginBottom = 8;
        tip.AddThemeStyleboxOverride("panel", bgStyle);

        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 4);
        tip.AddChild(content);

        var titleLabel = new Label { Name = "TooltipTitle" };
        titleLabel.AddThemeFontSizeOverride("font_size", 12);
        titleLabel.HorizontalAlignment = HorizontalAlignment.Left;
        content.AddChild(titleLabel);
        _tooltipTitle = titleLabel;

        var descLabel = new Label { Name = "TooltipDesc" };
        descLabel.AddThemeFontSizeOverride("font_size", 10);
        descLabel.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.7f));
        descLabel.HorizontalAlignment = HorizontalAlignment.Left;
        descLabel.AutowrapMode = TextServer.AutowrapMode.Word;
        descLabel.CustomMinimumSize = new Vector2(200, 0);
        content.AddChild(descLabel);
        _tooltipDesc = descLabel;

        tip.CustomMinimumSize = new Vector2(220, 0);
        AddChild(tip);
        _tooltip = tip;
    }

    private void ShowTooltip(Button btn, string titleKey, string descKey, Color accentColor)
    {
        if (_tooltip == null || _tooltipTitle == null || _tooltipDesc == null) return;

        _tooltipTitle.Text = I18n.Tr(titleKey);
        _tooltipTitle.AddThemeColorOverride("font_color", accentColor);
        _tooltipDesc.Text = I18n.Tr(descKey);

        // Convert btn global coords to panel-local coords
        var btnGlobal = btn.GlobalPosition;
        var panelGlobal = ((Control)_tooltip.GetParent()).GlobalPosition;
        var localPos = btnGlobal - panelGlobal;
        float tipX = localPos.X - 234; // 220 width + 14 gap
        float tipY = localPos.Y;

        _tooltip.Position = new Vector2(tipX, tipY);
        _tooltip.ZIndex = 100;

        ModLogger.Info($"Tooltip: tip=({tipX:F0},{tipY:F0}), btnGlobal=({btnGlobal.X:F0},{btnGlobal.Y:F0}), panelGlobal=({panelGlobal.X:F0},{panelGlobal.Y:F0})");

        _tooltipTween?.Kill();
        _tooltip.Modulate = new Color(1, 1, 1, 0);
        _tooltip.Show();
        _tooltipTween = CreateTween();
        _tooltipTween.TweenProperty(_tooltip, "modulate:a", 1f, 0.15f);
        _tooltipTween.SetEase(Tween.EaseType.Out);
    }

    private void HideTooltip()
    {
        if (_tooltip == null || !_tooltip.Visible) return;

        _tooltipTween?.Kill();
        _tooltipTween = CreateTween();
        _tooltipTween.TweenProperty(_tooltip, "modulate:a", 0f, 0.1f);
        _tooltipTween.SetEase(Tween.EaseType.In);
        _tooltipTween.Finished += () => _tooltip?.Hide();
    }

    public override void _ExitTree()
    {
        _tooltip?.QueueFree();
        _tooltip = null;
        _tooltipTitle = null;
        _tooltipDesc = null;
        base._ExitTree();
    }
}
