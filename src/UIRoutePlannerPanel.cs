using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace RoutePlanner;

public partial class UIRoutePlannerPanel : Control
{
    private readonly RoutePlannerInstance _instance;

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

    // Settings
    private ColorPickerButton? _colorPicker;
    private HSlider? _alphaSlider;
    private Control? _settingsPanel;

    // State
    private bool _isCollapsed;
    private int _selectedRoute;
    private Color _lineColor = Colors.Cyan;

    public Color LineColor => new(_lineColor.R, _lineColor.G, _lineColor.B, (float)_alpha);

    private double _alpha = 0.7;

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

        // Clear old cells
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

            var cell = new VBoxContainer();
            cell.AddThemeConstantOverride("separation", 0);

            // Type name
            var nameLabel = new Label { Text = name, HorizontalAlignment = HorizontalAlignment.Center };
            nameLabel.AddThemeColorOverride("font_color", color);
            nameLabel.AddThemeFontSizeOverride("font_size", 11);
            cell.AddChild(nameLabel);

            // Danger + Reward in one row
            var row = new HBoxContainer();
            row.Alignment = BoxContainer.AlignmentMode.Center;
            row.AddThemeConstantOverride("separation", 2);

            var dLabel = new Label { Text = $"危{F0(s.danger)}", HorizontalAlignment = HorizontalAlignment.Center };
            dLabel.AddThemeColorOverride("font_color", Colors.OrangeRed);
            dLabel.AddThemeFontSizeOverride("font_size", 9);
            row.AddChild(dLabel);

            var rLabel = new Label { Text = $"奖{F0(s.reward)}", HorizontalAlignment = HorizontalAlignment.Center };
            rLabel.AddThemeColorOverride("font_color", new Color(0.35f, 0.7f, 1f));
            rLabel.AddThemeFontSizeOverride("font_size", 9);
            row.AddChild(rLabel);

            cell.AddChild(row);
            _weightsGrid.AddChild(cell);
        }
    }

    private static string F0(double v) => v.ToString("F0");

    private void BuildUI()
    {
        // Panel background with rounded corners
        var bg = new Panel();
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        var bgStyle = new StyleBoxFlat();
        bgStyle.BgColor = new Color(0.05f, 0.05f, 0.08f, 0.85f);
        bgStyle.SetCornerRadiusAll(10);
        bg.AddThemeStyleboxOverride("panel", bgStyle);
        AddChild(bg);

        var container = new VBoxContainer();
        container.SetAnchorsPreset(LayoutPreset.FullRect);
        container.AddThemeConstantOverride("separation", 6);
        AddChild(container);

        // --- Header ---
        var header = new HBoxContainer();
        var title = CreateLabel("路线规划", 16, Colors.White, true);
        header.AddChild(title);
        header.AddChild(new Control { CustomMinimumSize = new Vector2(10, 0) });

        _collapseBtn = CreateButton("_", 24);
        _collapseBtn.CustomMinimumSize = new Vector2(24, 24);
        _collapseBtn.Pressed += OnCollapsePressed;
        header.AddChild(_collapseBtn);
        container.AddChild(header);

        // --- Content wrapper (collapsible) ---
        var content = new VBoxContainer { Name = "Content" };
        content.AddThemeConstantOverride("separation", 4);
        container.AddChild(content);

        // --- Weight sliders ---
        content.AddChild(CreateSeparator());
        content.AddChild(CreateLabel("权重设置", 13, new Color(0.7f, 0.7f, 0.7f)));

        // Danger slider
        var dangerHeader = new HBoxContainer();
        dangerHeader.AddThemeConstantOverride("separation", 2);
        _dangerCheckBox = new CheckBox();
        _dangerCheckBox.ButtonPressed = true;
        _dangerCheckBox.Toggled += OnDangerCheckToggled;
        dangerHeader.AddChild(_dangerCheckBox);
        dangerHeader.AddChild(CreateLabel("危险度", 12, Colors.OrangeRed));
        content.AddChild(dangerHeader);

        var dangerBox = new HBoxContainer();
        _dangerSlider = CreateSlider(50);
        _dangerSlider.ValueChanged += (v) =>
        {
            _instance.OnWeightChanged(v / 100.0, _rewardSlider?.Value / 100.0 ?? 0.5);
            UpdateSliderLabels();
        };
        dangerBox.AddChild(_dangerSlider);
        _dangerLabel = CreateLabel("50%", 12, Colors.OrangeRed);
        _dangerLabel.CustomMinimumSize = new Vector2(35, 0);
        dangerBox.AddChild(_dangerLabel);
        content.AddChild(dangerBox);

        // Reward slider
        var rewardHeader = new HBoxContainer();
        rewardHeader.AddThemeConstantOverride("separation", 2);
        _rewardCheckBox = new CheckBox();
        _rewardCheckBox.ButtonPressed = false;
        _rewardCheckBox.Toggled += OnRewardCheckToggled;
        rewardHeader.AddChild(_rewardCheckBox);
        rewardHeader.AddChild(CreateLabel("奖励度", 12, Colors.DodgerBlue));
        content.AddChild(rewardHeader);

        var rewardBox = new HBoxContainer();
        _rewardSlider = CreateSlider(50);
        _rewardSlider.ValueChanged += (v) =>
        {
            _instance.OnWeightChanged(_dangerSlider?.Value / 100.0 ?? 0.5, v / 100.0);
            UpdateSliderLabels();
        };
        rewardBox.AddChild(_rewardSlider);
        _rewardLabel = CreateLabel("50%", 12, Colors.DodgerBlue);
        _rewardLabel.CustomMinimumSize = new Vector2(35, 0);
        rewardBox.AddChild(_rewardLabel);
        content.AddChild(rewardBox);

        // Preset buttons
        var presetBox = new HBoxContainer();
        presetBox.AddThemeConstantOverride("separation", 4);
        var consBtn = CreateSmallButton("保守");
        consBtn.Pressed += () => _instance.OnPresetSelected("conservative");
        presetBox.AddChild(consBtn);
        var balBtn = CreateSmallButton("均衡");
        balBtn.Pressed += () => _instance.OnPresetSelected("balanced");
        presetBox.AddChild(balBtn);
        var aggBtn = CreateSmallButton("激进");
        aggBtn.Pressed += () => _instance.OnPresetSelected("aggressive");
        presetBox.AddChild(aggBtn);
        content.AddChild(presetBox);

        // --- Node type weights ---
        content.AddChild(CreateSeparator());
        _weightsGrid = new GridContainer { Name = "WeightsGrid" };
        _weightsGrid.Columns = 3;
        _weightsGrid.AddThemeConstantOverride("h_separation", 4);
        _weightsGrid.AddThemeConstantOverride("v_separation", 4);
        content.AddChild(_weightsGrid);

        // --- Route list ---
        content.AddChild(CreateSeparator());
        content.AddChild(CreateLabel("推荐路线", 13, new Color(0.7f, 0.7f, 0.7f)));

        // Balanced route
        var balBox = new HBoxContainer();
        _balancedBtn = CreateRadioButton();
        _balancedBtn.Pressed += () => OnRouteButtonPressed(0);
        balBox.AddChild(_balancedBtn);
        _balancedLabel = CreateRouteLabel(Colors.Cyan);
        balBox.AddChild(_balancedLabel);
        content.AddChild(balBox);

        // High reward route
        var hrBox = new HBoxContainer();
        _highRewardBtn = CreateRadioButton();
        _highRewardBtn.Pressed += () => OnRouteButtonPressed(1);
        hrBox.AddChild(_highRewardBtn);
        _highRewardLabel = CreateRouteLabel(Colors.Gold);
        hrBox.AddChild(_highRewardLabel);
        content.AddChild(hrBox);

        // Safe route
        var safeBox = new HBoxContainer();
        _safeBtn = CreateRadioButton();
        _safeBtn.Pressed += () => OnRouteButtonPressed(2);
        safeBox.AddChild(_safeBtn);
        _safeLabel = CreateRouteLabel(Colors.Green);
        safeBox.AddChild(_safeLabel);
        content.AddChild(safeBox);

        // --- Action buttons ---
        content.AddChild(CreateSeparator());
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 4);
        _drawBtn = CreateSmallButton("绘制");
        _drawBtn.Pressed += () => _instance.OnDrawClicked();
        actions.AddChild(_drawBtn);
        _clearBtn = CreateSmallButton("清除");
        _clearBtn.Pressed += () => _instance.OnClearClicked();
        actions.AddChild(_clearBtn);
        content.AddChild(actions);

        // --- Settings ---
        content.AddChild(CreateSeparator());
        var settingsHeader = CreateSmallButton("⚙ 设置");
        settingsHeader.Pressed += OnSettingsToggle;
        content.AddChild(settingsHeader);

        _settingsPanel = new VBoxContainer { Name = "Settings", Visible = false };
        _settingsPanel.AddThemeConstantOverride("separation", 3);

        var colorBox = new HBoxContainer();
        colorBox.AddChild(CreateLabel("线条颜色", 11, Colors.LightGray));
        _colorPicker = new ColorPickerButton();
        _colorPicker.CustomMinimumSize = new Vector2(40, 24);
        _colorPicker.Color = Colors.Cyan;
        _colorPicker.ColorChanged += (c) => { _lineColor = c; };
        colorBox.AddChild(_colorPicker);
        _settingsPanel.AddChild(colorBox);

        var alphaBox = new HBoxContainer();
        alphaBox.AddChild(CreateLabel("透明度", 11, Colors.LightGray));
        _alphaSlider = new HSlider { MinValue = 10, MaxValue = 100, Value = 70, Step = 5 };
        _alphaSlider.CustomMinimumSize = new Vector2(80, 0);
        _alphaSlider.ValueChanged += (v) => { _alpha = v / 100.0; };
        alphaBox.AddChild(_alphaSlider);
        _settingsPanel.AddChild(alphaBox);

        content.AddChild(_settingsPanel);

        // Set panel position and size
        SetAnchorsPreset(LayoutPreset.TopRight);
        OffsetLeft = -360;
        OffsetRight = 0;
        OffsetTop = 90;
        OffsetBottom = 600;
        CustomMinimumSize = new Vector2(340, 400);
    }

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
            c.Visible = !_isCollapsed;
        if (_collapseBtn != null)
            _collapseBtn.Text = _isCollapsed ? "+" : "_";
        OffsetBottom = _isCollapsed ? 40 : 600;
    }

    private void OnSettingsToggle()
    {
        if (_settingsPanel != null)
            _settingsPanel.Visible = !_settingsPanel.Visible;
    }

    // --- UI Helper Methods ---

    private static Label CreateRouteLabel(Color color)
    {
        var label = new Label
        {
            Text = "计算中...",
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            ClipContents = false,
        };
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontSizeOverride("font_size", 11);
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        return label;
    }

    private static Label CreateLabel(string text, int fontSize, Color color, bool bold = false)
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
            CustomMinimumSize = new Vector2(100, 0),
        };
    }

    private static Button CreateButton(string text, int fontSize)
    {
        var btn = new Button { Text = text };
        btn.CustomMinimumSize = new Vector2(24, 24);
        return btn;
    }

    private static Button CreateSmallButton(string text)
    {
        return new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(40, 22),
        };
    }

    private static Button CreateRadioButton()
    {
        return new Button
        {
            CustomMinimumSize = new Vector2(16, 16),
            ToggleMode = true,
        };
    }

    private static Control CreateSeparator()
    {
        return new ColorRect
        {
            Color = new Color(0.3f, 0.3f, 0.35f, 0.5f),
            CustomMinimumSize = new Vector2(0, 1),
        };
    }
}
