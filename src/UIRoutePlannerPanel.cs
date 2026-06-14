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

    // Node type icon cache
    private static readonly Dictionary<string, Texture2D?> _iconCache = new();

    private static string GetNodeIconPath(MapPointType pt) => pt switch
    {
        MapPointType.Elite => "res://images/atlases/ui_atlas.sprites/map/icons/map_elite.tres",
        MapPointType.Monster => "res://images/atlases/ui_atlas.sprites/map/icons/map_monster.tres",
        MapPointType.RestSite => "res://images/atlases/ui_atlas.sprites/map/icons/map_rest.tres",
        MapPointType.Shop => "res://images/atlases/ui_atlas.sprites/map/icons/map_shop.tres",
        MapPointType.Treasure => "res://images/atlases/ui_atlas.sprites/map/icons/map_chest.tres",
        MapPointType.Unknown => "res://images/atlases/ui_atlas.sprites/map/icons/map_unknown.tres",
        _ => "res://images/atlases/ui_atlas.sprites/map/icons/map_unknown.tres",
    };

    private static Texture2D? LoadNodeIcon(MapPointType pt)
    {
        string path = GetNodeIconPath(pt);
        if (_iconCache.TryGetValue(path, out var cached)) return cached;
        var tex = ResourceLoader.Load<Texture2D>(path);
        _iconCache[path] = tex;
        return tex;
    }

    // Dimension toggles (replaced plain CheckBox with styled pill buttons)
    private Button? _dangerToggleBtn;
    private Button? _rewardToggleBtn;

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
    private HBoxContainer? _balancedIcons;
    private HBoxContainer? _highRewardIcons;
    private HBoxContainer? _safeIcons;
    private HBoxContainer? _directedIcons;
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

    // Preset buttons
    private readonly Dictionary<string, Button> _presetButtons = new();

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
    private bool _weightsRefreshing; // guard against FocusExited re-entrancy during rebuild

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

    public void UpdatePresetHighlight(string preset)
    {
        foreach (var (key, btn) in _presetButtons)
        {
            bool isActive = key == preset;
            if (isActive)
            {
                btn.AddThemeColorOverride("font_color", Gold);
                var activeStyle = new StyleBoxFlat
                {
                    BgColor = new Color(Gold.R, Gold.G, Gold.B, 0.2f),
                    BorderWidthLeft = 1, BorderWidthRight = 1,
                    BorderWidthTop = 1, BorderWidthBottom = 1,
                    BorderColor = Gold,
                };
                activeStyle.SetCornerRadiusAll(0);
                btn.AddThemeStyleboxOverride("normal", activeStyle);
            }
            else
            {
                btn.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 1f));
                var normalStyle = new StyleBoxFlat
                {
                    BgColor = new Color(0, 0, 0, 0),
                    BorderWidthLeft = 1, BorderWidthRight = 1,
                    BorderWidthTop = 1, BorderWidthBottom = 1,
                    BorderColor = new Color(Gold.R, Gold.G, Gold.B, 0.4f),
                };
                normalStyle.SetCornerRadiusAll(0);
                btn.AddThemeStyleboxOverride("normal", normalStyle);
            }
        }
    }

    public void RefreshRoutes()
    {
        if (_balancedLabel != null) _balancedLabel.Text = _instance.GetRouteLabel(0);
        if (_directedLabel != null) _directedLabel.Text = _instance.GetRouteLabel(1);
        if (_highRewardLabel != null) _highRewardLabel.Text = _instance.GetRouteLabel(2);
        if (_safeLabel != null) _safeLabel.Text = _instance.GetRouteLabel(3);
        RefreshRouteIcons(_balancedIcons, 0);
        RefreshRouteIcons(_directedIcons, 1);
        RefreshRouteIcons(_highRewardIcons, 2);
        RefreshRouteIcons(_safeIcons, 3);
        UpdateRouteSelection();
        UpdateDrawButtonState();
    }

    private static readonly MapPointType[] _iconOrder =
        { MapPointType.Elite, MapPointType.Monster, MapPointType.RestSite, MapPointType.Shop, MapPointType.Treasure, MapPointType.Unknown };

    private void RefreshRouteIcons(HBoxContainer? iconRow, int routeIndex)
    {
        if (iconRow == null) return;
        while (iconRow.GetChildCount() > 0)
        {
            var c = iconRow.GetChild(0);
            iconRow.RemoveChild(c);
            c.QueueFree();
        }

        var counts = _instance.GetRouteCounts(routeIndex);

        foreach (var type in _iconOrder)
        {
            int count = counts.GetValueOrDefault(type, 0);
            var pair = new HBoxContainer();
            pair.AddThemeConstantOverride("separation", 2);
            var texRect = new TextureRect
            {
                Texture = LoadNodeIcon(type),
                CustomMinimumSize = new Vector2(26, 26),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            };
            pair.AddChild(texRect);
            var label = new Label { Text = count.ToString(), VerticalAlignment = VerticalAlignment.Center };
            label.AddThemeFontSizeOverride("font_size", 13);
            if (count == 0)
                label.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.25f));
            else
                label.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.85f));
            pair.AddChild(label);
            iconRow.AddChild(pair);
        }
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
        if (_weightsList == null || _weightsRefreshing) return;
        _weightsRefreshing = true;
        try
        {

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

            // Constraint toggles: ≥ (at least) and ≤ (at most)
            bool lowerActive = constraint.Mode == ConstraintMode.LowerOnly || constraint.Mode == ConstraintMode.Both;
            bool upperActive = constraint.Mode == ConstraintMode.UpperOnly || constraint.Mode == ConstraintMode.Both;

            // ≥ toggle — lower limit
            var lowerToggle = CreateConstraintToggle("≥", lowerActive, I18n.Tr("约束_至少"));
            var lowerInput = CreateConstraintValueEdit(constraint.LowerLimit);
            lowerInput.Visible = lowerActive;
            lowerToggle.Pressed += () =>
            {
                bool wasActive = lowerToggle.HasMeta("active") && (bool)lowerToggle.GetMeta("active");
                bool nowActive = !wasActive;
                UpdateConstraintToggleVisual(lowerToggle, nowActive);
                lowerInput.Visible = nowActive;

                var cur = _instance.GetConstraint(parsedType);
                ModLogger.Info($"≥ toggle [{parsedType}]: wasActive={wasActive} nowActive={nowActive} curMode={cur.Mode} curLowerLimits=[{string.Join(",", cur.LowerLimits)}] curUpperLimits=[{string.Join(",", cur.UpperLimits)}]");
                bool upperStillActive = cur.Mode == ConstraintMode.UpperOnly || cur.Mode == ConstraintMode.Both;
                var newMode = DeriveConstraintMode(nowActive, upperStillActive);
                int lower = 0;
                if (nowActive)
                {
                    lower = cur.LowerLimits[(int)newMode];
                    if (lower <= 0)
                        for (int m = 1; m <= 3; m++)
                            if (cur.LowerLimits[m] > 0) { ModLogger.Info($"≥ restored lower from slot[{m}]={cur.LowerLimits[m]}"); lower = cur.LowerLimits[m]; break; }
                    if (lower <= 0) { ModLogger.Info($"≥ using default lower=1"); lower = 1; }
                }
                int upper = upperStillActive ? cur.UpperLimit : 0;
                ModLogger.Info($"≥ toggle result: lower={lower} upper={upper} newMode={newMode}");
                _instance.OnConstraintChanged(parsedType, newMode, lower, upper);
            };
            constraintBox.AddChild(lowerToggle);

            lowerInput.TextSubmitted += (text) =>
            {
                if (_weightsRefreshing) return;
                var c = _instance.GetConstraint(parsedType);
                _instance.OnConstraintChanged(parsedType, c.Mode, ParseInt(text), c.UpperLimit);
            };
            lowerInput.FocusExited += () =>
            {
                if (_weightsRefreshing) return;
                var c = _instance.GetConstraint(parsedType);
                _instance.OnConstraintChanged(parsedType, c.Mode, ParseInt(lowerInput.Text), c.UpperLimit);
            };
            constraintBox.AddChild(lowerInput);

            // ≤ toggle — upper limit
            var upperToggle = CreateConstraintToggle("≤", upperActive, I18n.Tr("约束_最多"));
            var upperInput = CreateConstraintValueEdit(constraint.UpperLimit);
            upperInput.Visible = upperActive;
            upperToggle.Pressed += () =>
            {
                bool wasActive = upperToggle.HasMeta("active") && (bool)upperToggle.GetMeta("active");
                bool nowActive = !wasActive;
                UpdateConstraintToggleVisual(upperToggle, nowActive);
                upperInput.Visible = nowActive;

                var cur = _instance.GetConstraint(parsedType);
                ModLogger.Info($"≤ toggle [{parsedType}]: wasActive={wasActive} nowActive={nowActive} curMode={cur.Mode} curLowerLimits=[{string.Join(",", cur.LowerLimits)}] curUpperLimits=[{string.Join(",", cur.UpperLimits)}]");
                bool lowerStillActive = cur.Mode == ConstraintMode.LowerOnly || cur.Mode == ConstraintMode.Both;
                var newMode = DeriveConstraintMode(lowerStillActive, nowActive);
                int lower = lowerStillActive ? cur.LowerLimit : 0;
                int upper = 0;
                if (nowActive)
                {
                    upper = cur.UpperLimits[(int)newMode];
                    if (upper <= 0)
                        for (int m = 1; m <= 3; m++)
                            if (cur.UpperLimits[m] > 0) { ModLogger.Info($"≤ restored upper from slot[{m}]={cur.UpperLimits[m]}"); upper = cur.UpperLimits[m]; break; }
                    if (upper <= 0) { ModLogger.Info($"≤ using default upper=3"); upper = 3; }
                }
                ModLogger.Info($"≤ toggle result: lower={lower} upper={upper} newMode={newMode}");
                _instance.OnConstraintChanged(parsedType, newMode, lower, upper);
            };
            constraintBox.AddChild(upperToggle);

            upperInput.TextSubmitted += (text) =>
            {
                if (_weightsRefreshing) return;
                var c = _instance.GetConstraint(parsedType);
                _instance.OnConstraintChanged(parsedType, c.Mode, c.LowerLimit, ParseInt(text));
            };
            upperInput.FocusExited += () =>
            {
                if (_weightsRefreshing) return;
                var c = _instance.GetConstraint(parsedType);
                _instance.OnConstraintChanged(parsedType, c.Mode, c.LowerLimit, ParseInt(upperInput.Text));
            };
            constraintBox.AddChild(upperInput);

            // Left border: gold when satisfied, warm-orange warning when failing
            if (constraint.Mode != ConstraintMode.None)
            {
                var failingSet = _instance.GetFailingConstraints();
                bool isFailing = failingSet.Contains(parsedType);
                cardStyle.BorderWidthLeft = 2;
                cardStyle.BorderColor = isFailing ? WarmOrange : Gold;
                if (isFailing)
                {
                    // Subtle warning tint on the card background
                    cardStyle.BgColor = new Color(WarmOrange.R, WarmOrange.G, WarmOrange.B, 0.08f);
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
        finally
        {
            _weightsRefreshing = false;
        }
    }

    private static ConstraintMode DeriveConstraintMode(bool lowerActive, bool upperActive) =>
        (lowerActive, upperActive) switch
        {
            (true, true) => ConstraintMode.Both,
            (true, false) => ConstraintMode.LowerOnly,
            (false, true) => ConstraintMode.UpperOnly,
            _ => ConstraintMode.None,
        };

    private static void UpdateConstraintToggleVisual(Button btn, bool active)
    {
        btn.SetMeta("active", active);
        if (active)
        {
            btn.AddThemeColorOverride("font_color", Gold);
            var activeStyle = new StyleBoxFlat { BgColor = new Color(Gold.R, Gold.G, Gold.B, 0.15f) };
            activeStyle.SetCornerRadiusAll(3);
            btn.AddThemeStyleboxOverride("normal", activeStyle);
        }
        else
        {
            btn.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.4f));
            var inactiveStyle = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0) };
            btn.AddThemeStyleboxOverride("normal", inactiveStyle);
        }
    }

    private static int ParseInt(string text)
    {
        if (int.TryParse(text, out var v) && v >= 0) return v;
        return 0;
    }

    private static Button CreateConstraintToggle(string symbol, bool active, string tooltip)
    {
        var btn = new Button
        {
            Text = symbol,
            CustomMinimumSize = new Vector2(28, 24),
            TooltipText = tooltip,
        };
        btn.AddThemeFontSizeOverride("font_size", 11);

        // Store active state as meta for inline state tracking
        btn.SetMeta("active", active);

        if (active)
        {
            btn.AddThemeColorOverride("font_color", Gold);
            var activeStyle = new StyleBoxFlat { BgColor = new Color(Gold.R, Gold.G, Gold.B, 0.15f) };
            activeStyle.SetCornerRadiusAll(3);
            btn.AddThemeStyleboxOverride("normal", activeStyle);
        }
        else
        {
            btn.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.4f));
            var inactiveStyle = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0) };
            btn.AddThemeStyleboxOverride("normal", inactiveStyle);
        }

        var hoverStyle = new StyleBoxFlat { BgColor = new Color(1f, 1f, 1f, 0.08f) };
        hoverStyle.SetCornerRadiusAll(3);
        btn.AddThemeStyleboxOverride("hover", hoverStyle);

        var flatStyle = new StyleBoxFlat { BgColor = new Color(1f, 1f, 1f, 0.04f) };
        flatStyle.SetCornerRadiusAll(3);
        btn.AddThemeStyleboxOverride("pressed", flatStyle);
        btn.AddThemeStyleboxOverride("focus", flatStyle);

        return btn;
    }

    private static LineEdit CreateConstraintValueEdit(int value)
    {
        var edit = new LineEdit
        {
            Text = value > 0 ? value.ToString() : "",
            CustomMinimumSize = new Vector2(36, 24),
            MouseFilter = MouseFilterEnum.Stop,
        };
        edit.AddThemeFontSizeOverride("font_size", 11);
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
        dangerHeader.AddThemeConstantOverride("separation", 6);
        _dangerToggleBtn = CreateDimensionToggle("◆", WarmOrange, active: true);
        _dangerToggleBtn.Pressed += OnDangerTogglePressed;
        dangerHeader.AddChild(_dangerToggleBtn);
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
        rewardHeader.AddThemeConstantOverride("separation", 6);
        _rewardToggleBtn = CreateDimensionToggle("★", IceBlue, active: true);
        _rewardToggleBtn.Pressed += OnRewardTogglePressed;
        rewardHeader.AddChild(_rewardToggleBtn);
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

        void AddPresetTooltip(Button btn, string titleKey, string descKey)
        {
            btn.MouseEntered += () => ShowTooltip(btn, titleKey, descKey, Gold);
            btn.MouseExited += HideTooltip;
            btn.TreeExiting += HideTooltip;
        }

        var consBtn = CreatePresetButton("保守");
        consBtn.Pressed += () => _instance.OnPresetSelected("conservative");
        RegisterButtonI18n(consBtn, "保守");
        AddPresetTooltip(consBtn, "保守", "保守_desc");
        _presetButtons["conservative"] = consBtn;
        presetBox.AddChild(consBtn);
        var safeBtn = CreatePresetButton("求稳");
        safeBtn.Pressed += () => _instance.OnPresetSelected("safe_reward");
        RegisterButtonI18n(safeBtn, "求稳");
        AddPresetTooltip(safeBtn, "求稳", "求稳_desc");
        _presetButtons["safe_reward"] = safeBtn;
        presetBox.AddChild(safeBtn);
        var balBtn = CreatePresetButton("均衡");
        balBtn.Pressed += () => _instance.OnPresetSelected("balanced");
        RegisterButtonI18n(balBtn, "均衡");
        AddPresetTooltip(balBtn, "均衡", "均衡_desc");
        _presetButtons["balanced"] = balBtn;
        presetBox.AddChild(balBtn);
        var aggBtn = CreatePresetButton("激进");
        aggBtn.Pressed += () => _instance.OnPresetSelected("aggressive");
        RegisterButtonI18n(aggBtn, "激进");
        AddPresetTooltip(aggBtn, "激进", "激进_desc");
        _presetButtons["aggressive"] = aggBtn;
        presetBox.AddChild(aggBtn);
        var extBtn = CreatePresetButton("极端");
        extBtn.Pressed += () => _instance.OnPresetSelected("extreme");
        RegisterButtonI18n(extBtn, "极端");
        AddPresetTooltip(extBtn, "极端", "极端_desc");
        _presetButtons["extreme"] = extBtn;
        presetBox.AddChild(extBtn);
        content.AddChild(presetBox);

        // --- Node type weights ---
        content.AddChild(CreateSectionHeader("星图数据"));
        _weightsList = new VBoxContainer { Name = "WeightsList" };
        _weightsList.AddThemeConstantOverride("separation", 4);
        content.AddChild(_weightsList);

        // --- Route list ---
        content.AddChild(CreateSectionHeader("航线模式选择"));

        _balancedBtn = CreateRouteButton("◉ 自定义", "自定义_desc", SoftPurple, out _balancedLabel, out _balancedIcons);
        _balancedBtn.Pressed += () => OnRouteButtonPressed(0);
        content.AddChild(_balancedBtn);

        _directedBtn = CreateRouteButton("★ 定向", "定向_desc", WarmOrange, out _directedLabel, out _directedIcons);
        _directedBtn.Pressed += () => OnRouteButtonPressed(1);
        content.AddChild(_directedBtn);

        _highRewardBtn = CreateRouteButton("◆ 高收益", "高收益_desc", Colors.Gold, out _highRewardLabel, out _highRewardIcons);
        _highRewardBtn.Pressed += () => OnRouteButtonPressed(2);
        content.AddChild(_highRewardBtn);

        _safeBtn = CreateRouteButton("● 安全", "保守_desc", LimeGreen, out _safeLabel, out _safeIcons);
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
        OffsetBottom = 1050;
        CustomMinimumSize = new Vector2(400, 800);
    }

    // --- Dimension toggle handlers ---

    private void OnDangerTogglePressed()
    {
        bool nowActive = !_dangerToggleBtn!.HasMeta("active") || !(bool)_dangerToggleBtn.GetMeta("active");
        // Prevent both from being unchecked
        if (!nowActive && (_rewardToggleBtn == null || !IsToggleActive(_rewardToggleBtn)))
        {
            return; // refuse: at least one must be active
        }
        UpdateToggleActive(_dangerToggleBtn, WarmOrange, nowActive);
        _instance.OnDimensionChanged(nowActive, IsToggleActive(_rewardToggleBtn));
    }

    private void OnRewardTogglePressed()
    {
        bool nowActive = !_rewardToggleBtn!.HasMeta("active") || !(bool)_rewardToggleBtn.GetMeta("active");
        if (!nowActive && (_dangerToggleBtn == null || !IsToggleActive(_dangerToggleBtn)))
        {
            return; // refuse: at least one must be active
        }
        UpdateToggleActive(_rewardToggleBtn, IceBlue, nowActive);
        _instance.OnDimensionChanged(IsToggleActive(_dangerToggleBtn), nowActive);
    }

    private void NotifyDimensionChanged()
    {
        _instance.OnDimensionChanged(
            IsToggleActive(_dangerToggleBtn),
            IsToggleActive(_rewardToggleBtn));
    }

    private static bool IsToggleActive(Button? btn) =>
        btn != null && btn.HasMeta("active") && (bool)btn.GetMeta("active");

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
        RefreshWeights();
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

    /// <summary>Creates a pill-shaped toggle with icon for dimension enable/disable.</summary>
    private static Button CreateDimensionToggle(string icon, Color accentColor, bool active)
    {
        var btn = new Button
        {
            Text = icon,
            CustomMinimumSize = new Vector2(42, 22),
        };
        btn.AddThemeFontSizeOverride("font_size", 12);
        btn.SetMeta("active", active);

        // Hover — subtle highlight
        var hoverStyle = new StyleBoxFlat
        {
            BgColor = new Color(accentColor.R, accentColor.G, accentColor.B, 0.12f),
            BorderWidthLeft = 1, BorderWidthRight = 1,
            BorderWidthTop = 1, BorderWidthBottom = 1,
            BorderColor = new Color(accentColor.R, accentColor.G, accentColor.B, 0.3f),
        };
        hoverStyle.SetCornerRadiusAll(11);
        btn.AddThemeStyleboxOverride("hover", hoverStyle);

        // Pressed — deeper
        var pressedStyle = new StyleBoxFlat
        {
            BgColor = new Color(accentColor.R, accentColor.G, accentColor.B, 0.08f),
            BorderWidthLeft = 1, BorderWidthRight = 1,
            BorderWidthTop = 1, BorderWidthBottom = 1,
            BorderColor = new Color(accentColor.R, accentColor.G, accentColor.B, 0.2f),
        };
        pressedStyle.SetCornerRadiusAll(11);
        btn.AddThemeStyleboxOverride("pressed", pressedStyle);
        btn.AddThemeStyleboxOverride("focus", pressedStyle);

        ApplyToggleStyle(btn, accentColor, active);
        return btn;
    }

    private static void ApplyToggleStyle(Button btn, Color accentColor, bool active)
    {
        if (active)
        {
            btn.AddThemeColorOverride("font_color", Colors.White);
            var activeBg = new StyleBoxFlat
            {
                BgColor = new Color(accentColor.R, accentColor.G, accentColor.B, 0.55f),
                BorderWidthLeft = 1, BorderWidthRight = 1,
                BorderWidthTop = 1, BorderWidthBottom = 1,
                BorderColor = new Color(accentColor.R, accentColor.G, accentColor.B, 0.6f),
            };
            activeBg.SetCornerRadiusAll(11);
            btn.AddThemeStyleboxOverride("normal", activeBg);
        }
        else
        {
            btn.AddThemeColorOverride("font_color", new Color(accentColor.R, accentColor.G, accentColor.B, 0.35f));
            var inactiveBg = new StyleBoxFlat
            {
                BgColor = new Color(0, 0, 0, 0),
                BorderWidthLeft = 1, BorderWidthRight = 1,
                BorderWidthTop = 1, BorderWidthBottom = 1,
                BorderColor = new Color(accentColor.R, accentColor.G, accentColor.B, 0.18f),
            };
            inactiveBg.SetCornerRadiusAll(11);
            btn.AddThemeStyleboxOverride("normal", inactiveBg);
        }
    }

    private static void UpdateToggleActive(Button btn, Color accentColor, bool active)
    {
        btn.SetMeta("active", active);
        ApplyToggleStyle(btn, accentColor, active);
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
                tween.Finished += () => { c.Visible = false; c.MouseFilter = MouseFilterEnum.Ignore; };
            }
            else
            {
                c.MouseFilter = MouseFilterEnum.Pass;
                c.Modulate = new Color(1, 1, 1, 0);
                c.Visible = true;
                var tween = CreateTween();
                tween.TweenProperty(c, "modulate:a", 1f, 0.2f);
                tween.SetEase(Tween.EaseType.InOut);
                tween.Finished += () => c.Modulate = Colors.White;
            }
        }

        // Collapsed: 40px height = offset_top(90) + 40 = 130
        float targetBottom = _isCollapsed ? 130f : 1050f;
        CustomMinimumSize = _isCollapsed ? new Vector2(400, 0) : new Vector2(400, 800);
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

    private Button CreateRouteButton(string labelKey, string descKey, Color accentColor, out Label routeLabel, out HBoxContainer iconRow)
    {
        var btn = new Button();
        btn.ToggleMode = true;
        btn.CustomMinimumSize = new Vector2(0, 48);
        btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        btn.AddThemeFontSizeOverride("font_size", 10);

        var container = new VBoxContainer();
        container.MouseFilter = MouseFilterEnum.Ignore;
        container.AddThemeConstantOverride("separation", 2);
        container.SetAnchorsPreset(LayoutPreset.FullRect);
        container.OffsetLeft = 6;
        container.OffsetRight = -6;
        container.OffsetTop = 4;
        container.OffsetBottom = -4;

        // Row 1: icon + score text
        var row = new HBoxContainer();
        row.MouseFilter = MouseFilterEnum.Ignore;
        row.AddThemeConstantOverride("separation", 4);

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
        container.AddChild(row);

        // Row 2: node type icons with counts
        iconRow = new HBoxContainer();
        iconRow.MouseFilter = MouseFilterEnum.Ignore;
        iconRow.AddThemeConstantOverride("separation", 8);
        container.AddChild(iconRow);

        btn.AddChild(container);

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
        btn.CustomMinimumSize = new Vector2(28, 22);
        btn.AddThemeFontSizeOverride("font_size", 11);
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
