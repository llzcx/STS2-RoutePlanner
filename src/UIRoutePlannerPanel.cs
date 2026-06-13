using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

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
    private GridContainer? _weightsGrid;

    // Route labels
    private Label? _balancedLabel;
    private Label? _highRewardLabel;
    private Label? _safeLabel;
    private Button? _balancedBtn;
    private Button? _highRewardBtn;
    private Button? _safeBtn;

    // Action buttons
    private Button? _drawBtn;
    private Button? _clearBtn;
    private Button? _collapseBtn;

    // State
    private bool _isCollapsed;
    private int _selectedRoute;

    public UIRoutePlannerPanel(RoutePlannerInstance instance)
    {
        _instance = instance;
        Name = "RoutePlannerPanel";
        BuildUI();
    }

    public void SetSliderValues(double dangerWeight, double rewardWeight)
    {
        if (_dangerSlider != null) _dangerSlider.Value = dangerWeight * 100;
        if (_rewardSlider != null) _rewardSlider.Value = rewardWeight * 100;
    }

    public void RefreshRoutes()
    {
        if (_balancedLabel != null) _balancedLabel.Text = _instance.GetRouteLabel(0);
        if (_highRewardLabel != null) _highRewardLabel.Text = _instance.GetRouteLabel(1);
        if (_safeLabel != null) _safeLabel.Text = _instance.GetRouteLabel(2);
        UpdateRouteSelection();
        RefreshWeights();
    }

    private static readonly (string key, string name, Color color)[] _weightTypes =
    {
        ("Elite",    "精英", new Color(1f, 0.45f, 0f)),
        ("Monster",  "怪物", new Color(0.9f, 0.55f, 0.3f)),
        ("RestSite", "休息", new Color(0.3f, 0.85f, 0.3f)),
        ("Shop",     "商店", new Color(0.85f, 0.75f, 0.2f)),
        ("Treasure", "宝箱", new Color(0.25f, 0.75f, 0.85f)),
        ("Unknown",  "未知", new Color(0.55f, 0.55f, 0.6f)),
    };

    private void RefreshWeights()
    {
        if (_weightsGrid == null) return;

        while (_weightsGrid.GetChildCount() > 0)
        {
            var c = _weightsGrid.GetChild(0);
            _weightsGrid.RemoveChild(c);
            c.QueueFree();
        }

        var scores = _instance.GetEffectiveTypeScores();
        if (scores.Count == 0) return;

        foreach (var (key, name, color) in _weightTypes)
        {
            if (!scores.TryGetValue(key, out var s)) continue;

            // Card container with floating background
            var card = new Panel();
            var cardStyle = new StyleBoxFlat();
            cardStyle.BgColor = new Color(1f, 1f, 1f, 0.06f);
            cardStyle.SetCornerRadiusAll(6);
            card.AddThemeStyleboxOverride("panel", cardStyle);
            card.CustomMinimumSize = new Vector2(96, 44);

            var vbox = new VBoxContainer();
            vbox.AddThemeConstantOverride("separation", 2);
            vbox.SetAnchorsPreset(LayoutPreset.FullRect);
            vbox.Position = new Vector2(6, 4);

            // Node type name
            var nameLabel = new Label { Text = name, HorizontalAlignment = HorizontalAlignment.Center };
            nameLabel.AddThemeColorOverride("font_color", color);
            nameLabel.AddThemeFontSizeOverride("font_size", 11);
            vbox.AddChild(nameLabel);

            // Danger + Reward in one row
            var row = new HBoxContainer();
            row.Alignment = BoxContainer.AlignmentMode.Center;
            row.AddThemeConstantOverride("separation", 8);

            var dLabel = new Label { Text = $"◆{F0(s.danger)}", HorizontalAlignment = HorizontalAlignment.Center };
            dLabel.AddThemeColorOverride("font_color", WarmOrange);
            dLabel.AddThemeFontSizeOverride("font_size", 10);
            row.AddChild(dLabel);

            var rLabel = new Label { Text = $"★{F0(s.reward)}", HorizontalAlignment = HorizontalAlignment.Center };
            rLabel.AddThemeColorOverride("font_color", IceBlue);
            rLabel.AddThemeFontSizeOverride("font_size", 10);
            row.AddChild(rLabel);

            vbox.AddChild(row);
            card.AddChild(vbox);
            _weightsGrid.AddChild(card);
        }
    }

    private static string F0(double v) => v.ToString("F0");

    private void BuildUI()
    {
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;

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
        accentLine.OffsetLeft = 12;
        accentLine.OffsetRight = -12;
        accentLine.OffsetTop = 0;
        AddChild(accentLine);

        var container = new VBoxContainer();
        container.SetAnchorsPreset(LayoutPreset.FullRect);
        container.AddThemeConstantOverride("separation", 6);
        container.OffsetLeft = 12;
        container.OffsetRight = -12;
        container.OffsetTop = 10;
        container.OffsetBottom = -8;
        AddChild(container);

        // --- Header ---
        var header = new HBoxContainer();
        var title = CreateLabel("◇ 路线导航仪", 14, StarWhite);
        title.AddThemeFontSizeOverride("font_size", 14);
        header.AddChild(title);
        header.AddChild(new Control { CustomMinimumSize = new Vector2(10, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill });

        _collapseBtn = CreateIconButton("⊟", 22);
        _collapseBtn.CustomMinimumSize = new Vector2(22, 22);
        _collapseBtn.Pressed += OnCollapsePressed;
        header.AddChild(_collapseBtn);
        container.AddChild(header);

        // --- Content wrapper (collapsible) ---
        var content = new VBoxContainer { Name = "Content" };
        content.AddThemeConstantOverride("separation", 4);
        container.AddChild(content);

        // --- Weight sliders ---
        content.AddChild(CreateSectionHeader("导航参数"));

        // Danger slider
        var dangerHeader = new HBoxContainer();
        dangerHeader.AddThemeConstantOverride("separation", 2);
        _dangerCheckBox = new CheckBox();
        _dangerCheckBox.ButtonPressed = true;
        _dangerCheckBox.Toggled += OnDangerCheckToggled;
        dangerHeader.AddChild(_dangerCheckBox);
        dangerHeader.AddChild(CreateLabel("危险容限", 12, WarmOrange));
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
        rewardHeader.AddChild(CreateLabel("收益渴求", 12, IceBlue));
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
        presetBox.AddChild(consBtn);
        var safeBtn = CreatePresetButton("求稳");
        safeBtn.Pressed += () => _instance.OnPresetSelected("safe_reward");
        presetBox.AddChild(safeBtn);
        var balBtn = CreatePresetButton("均衡");
        balBtn.Pressed += () => _instance.OnPresetSelected("balanced");
        presetBox.AddChild(balBtn);
        var aggBtn = CreatePresetButton("激进");
        aggBtn.Pressed += () => _instance.OnPresetSelected("aggressive");
        presetBox.AddChild(aggBtn);
        var extBtn = CreatePresetButton("极端");
        extBtn.Pressed += () => _instance.OnPresetSelected("extreme");
        presetBox.AddChild(extBtn);
        content.AddChild(presetBox);

        // --- Node type weights ---
        content.AddChild(CreateSectionHeader("星图数据"));
        _weightsGrid = new GridContainer { Name = "WeightsGrid" };
        _weightsGrid.Columns = 3;
        _weightsGrid.AddThemeConstantOverride("h_separation", 6);
        _weightsGrid.AddThemeConstantOverride("v_separation", 6);
        content.AddChild(_weightsGrid);

        // --- Route list ---
        content.AddChild(CreateSectionHeader("推荐航线"));

        _balancedBtn = CreateRouteButton("◉", "自定义", SoftPurple, out _balancedLabel);
        _balancedBtn.Pressed += () => OnRouteButtonPressed(0);
        content.AddChild(_balancedBtn);

        _highRewardBtn = CreateRouteButton("◆", "高收益", Colors.Gold, out _highRewardLabel);
        _highRewardBtn.Pressed += () => OnRouteButtonPressed(1);
        content.AddChild(_highRewardBtn);

        _safeBtn = CreateRouteButton("●", "保守", LimeGreen, out _safeLabel);
        _safeBtn.Pressed += () => OnRouteButtonPressed(2);
        content.AddChild(_safeBtn);

        // --- Action buttons ---
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 6);
        _drawBtn = CreateActionButton("✦ 绘制航线", WarmOrange, primary: true);
        _drawBtn.Pressed += () => _instance.OnDrawClicked();
        actions.AddChild(_drawBtn);
        _clearBtn = CreateActionButton("✧ 清除", Gold, primary: false);
        _clearBtn.Pressed += () => _instance.OnClearClicked();
        actions.AddChild(_clearBtn);
        content.AddChild(actions);

        // Set panel position and size
        SetAnchorsPreset(LayoutPreset.TopRight);
        OffsetLeft = -420;
        OffsetRight = 0;
        OffsetTop = 90;
        OffsetBottom = 560;
        CustomMinimumSize = new Vector2(400, 440);
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
    }

    private void UpdateRouteSelection()
    {
        if (_balancedBtn != null) _balancedBtn.ButtonPressed = _selectedRoute == 0;
        if (_highRewardBtn != null) _highRewardBtn.ButtonPressed = _selectedRoute == 1;
        if (_safeBtn != null) _safeBtn.ButtonPressed = _selectedRoute == 2;
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
        float targetBottom = _isCollapsed ? 130f : 560f;
        MouseFilter = _isCollapsed ? MouseFilterEnum.Ignore : MouseFilterEnum.Stop;
        CustomMinimumSize = _isCollapsed ? new Vector2(400, 0) : new Vector2(400, 440);
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

    private static Button CreateRouteButton(string icon, string label, Color accentColor, out Label routeLabel)
    {
        var btn = new Button();
        btn.ToggleMode = true;
        btn.CustomMinimumSize = new Vector2(0, 28);
        btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        btn.AddThemeFontSizeOverride("font_size", 11);

        // Inner HBox for icon + route text
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        row.SetAnchorsPreset(LayoutPreset.FullRect);
        row.OffsetLeft = 4;
        row.OffsetRight = -4;

        var iconLabel = new Label { Text = $"{icon} {label}" };
        iconLabel.AddThemeColorOverride("font_color", accentColor);
        iconLabel.AddThemeFontSizeOverride("font_size", 11);
        iconLabel.CustomMinimumSize = new Vector2(58, 0);
        iconLabel.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(iconLabel);

        routeLabel = new Label
        {
            Text = "计算中...",
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            ClipContents = false,
        };
        routeLabel.AddThemeColorOverride("font_color", accentColor);
        routeLabel.AddThemeFontSizeOverride("font_size", 11);
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

    private static Control CreateSectionHeader(string text)
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

        var label = CreateLabel(text, 12, Gold);
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
}
