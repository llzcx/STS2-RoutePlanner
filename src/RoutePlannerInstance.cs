using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

namespace RoutePlanner;

public enum ConstraintMode
{
    None,
    UpperOnly,
    LowerOnly,
    Both
}

public class NodeConstraint
{
    public ConstraintMode Mode { get; set; }

    // Per-mode storage: indices 0=None, 1=UpperOnly, 2=LowerOnly, 3=Both
    public int[] LowerLimits { get; set; } = new int[4];
    public int[] UpperLimits { get; set; } = new int[4];

    // Convenience accessors for current Mode — with setters for JSON backward compat
    public int LowerLimit
    {
        get => LowerLimits[(int)Mode];
        set => LowerLimits[(int)Mode] = value;
    }
    public int UpperLimit
    {
        get => UpperLimits[(int)Mode];
        set => UpperLimits[(int)Mode] = value;
    }

    public bool IsSatisfied(int count) => Mode switch
    {
        ConstraintMode.None => true,
        ConstraintMode.UpperOnly => count <= UpperLimit,
        ConstraintMode.LowerOnly => count >= LowerLimit,
        ConstraintMode.Both => count >= LowerLimit && count <= UpperLimit,
        _ => true,
    };
}

public class RoutePlannerInstance
{
    public static RoutePlannerInstance? Instance { get; private set; }

    public static RoutePlannerInstance Create()
    {
        Instance = new RoutePlannerInstance();
        return Instance;
    }

    private NMapScreen? _mapScreen;
    private RunState? _runState;
    private ActMap? _actMap;
    private readonly RouteScoringEngine _scoring = new();
    private RoutePlanResult? _currentResult;
    private UIRoutePlannerPanel? _panel;
    private RouteDrawingManager? _drawingManager;

    private double _dangerWeight = 0.0;
    private double _rewardWeight = 0.5;
    private bool _dangerEnabled = true;
    private bool _rewardEnabled = true;
    private int _selectedRouteIndex;
    private Timer? _configPollTimer;
    private bool _autoDraw;
    private string _currentPreset = "";

    private static readonly MapPointType[] DefaultPriorityOrder =
    {
        MapPointType.Elite, MapPointType.Monster, MapPointType.RestSite,
        MapPointType.Shop, MapPointType.Treasure, MapPointType.Unknown,
    };

    private MapPointType[] _priorityOrder = LoadPriorityOrder();
    private Dictionary<MapPointType, NodeConstraint> _constraints = LoadConstraints();

    private static Dictionary<MapPointType, NodeConstraint> LoadConstraints()
    {
        var result = new Dictionary<MapPointType, NodeConstraint>();
        var saved = I18n.ConstraintsData;
        if (saved == null) return result;
        foreach (var kv in saved)
        {
            if (Enum.TryParse<MapPointType>(kv.Key, out var type))
                result[type] = kv.Value;
        }
        return result;
    }

    private void SaveConstraints()
    {
        I18n.ConstraintsData = _constraints.ToDictionary(
            kv => kv.Key.ToString(),
            kv => kv.Value);
        I18n.SaveSettings();
    }

    private static MapPointType[] LoadPriorityOrder()
    {
        var saved = I18n.PriorityOrder;
        if (saved == null || saved.Length == 0) return (MapPointType[])DefaultPriorityOrder.Clone();
        try
        {
            return saved.Select(s => Enum.Parse<MapPointType>(s)).ToArray();
        }
        catch
        {
            return (MapPointType[])DefaultPriorityOrder.Clone();
        }
    }

    private void SavePriorityOrder()
    {
        I18n.PriorityOrder = _priorityOrder.Select(p => p.ToString()).ToArray();
        I18n.SaveSettings();
    }

    public void OnMapScreenReady(NMapScreen mapScreen, RunState runState)
    {
        ModLogger.Info($"OnMapScreenReady called — ActMap type: {runState.Map?.GetType().Name ?? "null"}");

        // Clean up previous state in case SetMap is called twice on the same screen
        OnMapScreenExit();

        _mapScreen = mapScreen;
        _runState = runState;
        _actMap = runState.Map;

        ModLogger.Info($"ActMap: {_actMap?.GetType().Name}, rows={_actMap?.GetRowCount()}, act={runState.CurrentActIndex}");

        _scoring.InvalidateCache();

        // Restore persisted auto-draw state
        _autoDraw = I18n.AutoDraw;

        // Create UI panel
        _panel = new UIRoutePlannerPanel(this);
        mapScreen.AddChild(_panel);
        ModLogger.Info("UI panel created and added to map screen");

        // Create drawing manager
        _drawingManager = new RouteDrawingManager(mapScreen);

        // Start config hot-reload polling
        _configPollTimer = new Timer();
        _configPollTimer.WaitTime = 1.0;
        _configPollTimer.Timeout += OnConfigPoll;
        mapScreen.AddChild(_configPollTimer);
        _configPollTimer.Start();

        // Initial route calculation
        RecalculateRoutes();
    }

    private void OnConfigPoll()
    {
        if (RouteScoringConfig.PollConfigChange())
        {
            ModLogger.Info("Config change detected via polling, marking dirty");
            MarkDirty();
        }
    }

    public void OnMapScreenExit()
    {
        ModLogger.Info("OnMapScreenExit — cleaning up");
        _configPollTimer?.Stop();
        _configPollTimer?.QueueFree();
        _configPollTimer = null;
        _panel?.QueueFree();
        _panel = null;
        _drawingManager = null;
        _mapScreen = null;
        _runState = null;
        _actMap = null;
        _currentResult = null;
    }

    public void MarkDirty()
    {
        _scoring.InvalidateCache();
        RecalculateRoutes();
    }

    public void OnWeightChanged(double dangerWeight, double rewardWeight)
    {
        _dangerWeight = dangerWeight;
        _rewardWeight = rewardWeight;
        _currentPreset = "";
        _panel?.UpdatePresetHighlight("");
        RecalculateDPOnly();
        _panel?.RefreshWeights();
    }

    public void OnDimensionChanged(bool dangerEnabled, bool rewardEnabled)
    {
        _dangerEnabled = dangerEnabled;
        _rewardEnabled = rewardEnabled;
        RecalculateDPOnly();
        _panel?.RefreshWeights();
    }

    private (double dangerW, double rewardW) GetEffectiveWeights()
    {
        // When danger disabled: use 0.5 so (2*d - 1) = 0, danger is neutral in formula
        double d = _dangerEnabled ? _dangerWeight : 0.5;
        double r = _rewardEnabled ? _rewardWeight : 0;
        return (d, r);
    }

    public string CurrentPreset => _currentPreset;

    public void OnPresetSelected(string preset)
    {
        var presets = RouteScoringConfig.Current.WeightPresets;
        if (presets.TryGetValue(preset, out var p))
        {
            _currentPreset = preset;
            _dangerWeight = p.DangerWeight;
            _rewardWeight = p.RewardWeight;
            RecalculateDPOnly();
            _panel?.SetSliderValues(_dangerWeight, _rewardWeight);
            _panel?.RefreshWeights();
            _panel?.UpdatePresetHighlight(preset);
        }
    }

    public void OnRouteSelected(int index)
    {
        _selectedRouteIndex = index;
    }

    public void OnDrawClicked()
    {
        MarkDirty();
        var route = GetSelectedRoute();
        if (route != null && route.Count > 0)
        {
            ModLogger.Info($"Draw clicked — route has {route.Count} nodes");
            _drawingManager?.DrawRoute(route, Colors.White);
        }
        else
        {
            ModLogger.Warn("Draw clicked but no valid route");
        }
    }

    public void OnClearClicked()
    {
        _drawingManager?.ClearAllDrawings();
    }

    public bool AutoDraw { get => _autoDraw; set => _autoDraw = value; }

    public void ToggleAutoDraw()
    {
        _autoDraw = !_autoDraw;
        I18n.AutoDraw = _autoDraw;
        I18n.SaveSettings();
    }

    public void TryAutoDraw()
    {
        if (!_autoDraw) return;
        _drawingManager?.ClearAllDrawings();
        var route = GetSelectedRoute();
        if (route != null && route.Count > 0)
            _drawingManager?.DrawRoute(route, Colors.White);
    }

    public bool IsSelectedRouteSatisfyingConstraints()
    {
        if (_currentResult == null) return true;
        return _selectedRouteIndex switch
        {
            0 => _currentResult.BalancedConstraintsSatisfied,
            1 => _currentResult.PriorityConstraintsSatisfied,
            2 => _currentResult.HighRewardConstraintsSatisfied,
            3 => _currentResult.SafeConstraintsSatisfied,
            _ => true,
        };
    }

    public HashSet<MapPointType> GetFailingConstraints()
    {
        var failing = new HashSet<MapPointType>();
        var route = GetSelectedRoute();
        if (route == null || route.Count == 0) return failing;

        var futureNodes = route.Skip(1).ToList();
        foreach (var (type, constraint) in _constraints)
        {
            if (constraint.Mode == ConstraintMode.None) continue;
            int count = futureNodes.Count(p => p.PointType == type);
            if (!constraint.IsSatisfied(count))
                failing.Add(type);
        }
        return failing;
    }

    public Dictionary<string, (double danger, double reward)> GetEffectiveTypeScores()
    {
        var rs = GetRunState();
        if (rs == null) return new();
        return _scoring.GetEffectiveTypeScores(rs);
    }

    public RoutePlanResult? GetCurrentResult() => _currentResult;

    public List<MapPoint>? GetSelectedRoute()
    {
        if (_currentResult == null) return null;
        return _selectedRouteIndex switch
        {
            0 => _currentResult.BalancedRoute,
            1 => _currentResult.PriorityRoute,
            2 => _currentResult.HighRewardRoute,
            3 => _currentResult.SafeRoute,
            _ => _currentResult.BalancedRoute,
        };
    }

    public void OnPriorityOrderChanged(MapPointType[] order)
    {
        _priorityOrder = order;
        SavePriorityOrder();
        RecalculatePriorityRoute();
        _panel?.RefreshRoutes();
        _panel?.RefreshWeights();
        TryAutoDraw();
    }

    public string GetRouteLabel(int index)
    {
        if (_currentResult == null) return "";
        var route = index switch
        {
            0 => _currentResult.BalancedRoute,
            1 => _currentResult.PriorityRoute,
            2 => _currentResult.HighRewardRoute,
            3 => _currentResult.SafeRoute,
            _ => _currentResult.BalancedRoute,
        };

        if (route.Count == 0)
        {
            return index switch
            {
                0 => $"{I18n.Tr("自定义")}  — {I18n.Tr("无符合路线")}",
                1 => $"{I18n.Tr("定向")}  — {I18n.Tr("无符合路线")}",
                2 => $"{I18n.Tr("高收益")}  — {I18n.Tr("无符合路线")}",
                3 => $"{I18n.Tr("安全")}  — {I18n.Tr("无符合路线")}",
                _ => "",
            };
        }

        double danger = 0, reward = 0;
        if (_runState != null)
        {
            danger = route.Sum(p => _scoring.CalcDangerScore(p, _runState));
            reward = route.Sum(p => _scoring.CalcRewardScore(p, _runState));
        }

        return index switch
        {
            0 => $"{I18n.Tr("自定义")} {I18n.Tr("危险")}{F(danger)} {I18n.Tr("奖励")}{F(reward)}",
            1 => $"{I18n.Tr("定向")} {I18n.Tr("危险")}{F(danger)} {I18n.Tr("奖励")}{F(reward)}",
            2 => $"{I18n.Tr("高收益")} {I18n.Tr("奖励")}{F(reward)}",
            3 => $"{I18n.Tr("安全")} {I18n.Tr("危险")}{F(danger)}",
            _ => "",
        };
    }

    public Dictionary<MapPointType, int> GetRouteCounts(int index)
    {
        var result = new Dictionary<MapPointType, int>();
        if (_currentResult == null) return result;
        var route = index switch
        {
            0 => _currentResult.BalancedRoute,
            1 => _currentResult.PriorityRoute,
            2 => _currentResult.HighRewardRoute,
            3 => _currentResult.SafeRoute,
            _ => _currentResult.BalancedRoute,
        };
        if (route.Count <= 1) return result; // skip start node

        var futureNodes = route.Skip(1).ToList();
        foreach (var type in new[] { MapPointType.Elite, MapPointType.RestSite, MapPointType.Treasure, MapPointType.Shop, MapPointType.Monster, MapPointType.Unknown })
        {
            int c = futureNodes.Count(p => p.PointType == type);
            if (c > 0) result[type] = c;
        }
        return result;
    }

    public MapPointType[] GetPriorityOrder() => _priorityOrder;

    public NodeConstraint GetConstraint(MapPointType type)
    {
        if (_constraints.TryGetValue(type, out var c))
            return c;
        return new NodeConstraint();
    }

    public IReadOnlyDictionary<MapPointType, NodeConstraint> GetAllConstraints() => _constraints;

    public void OnConstraintChanged(MapPointType type, ConstraintMode mode, int lower, int upper)
    {
        if (!_constraints.TryGetValue(type, out var c))
        {
            c = new NodeConstraint();
            _constraints[type] = c;
        }
        c.Mode = mode;
        c.LowerLimit = lower;
        c.UpperLimit = upper;
        if (mode == ConstraintMode.None)
        {
            // Clear the None-mode value; it's not meaningful
            c.LowerLimits[(int)ConstraintMode.None] = 0;
            c.UpperLimits[(int)ConstraintMode.None] = 0;
        }
        ModLogger.Info($"Constraint changed: {type} mode={mode} lower={lower} upper={upper}, active={_constraints.Count(kv => kv.Value.Mode != ConstraintMode.None)}");
        SaveConstraints();
        RecalculateDPOnly();
        _panel?.RefreshWeights();
    }

    private static string F(double v) => v.ToString("F0");

    /// <summary>Get the latest RunState from NMapScreen via reflection.</summary>
    private RunState? GetRunState()
    {
        if (_mapScreen == null) return _runState;
        var field = _mapScreen.GetType()
            .GetField("_runState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field == null) return _runState;
        var rs = field.GetValue(_mapScreen) as RunState;
        if (rs != null) _runState = rs; // update cached ref
        return rs ?? _runState;
    }

    /// <summary>Get the latest ActMap from RunState.</summary>
    private ActMap? GetActMap()
    {
        var rs = GetRunState();
        if (rs != null) _actMap = rs.Map;
        return _actMap;
    }

    private void RecalculateRoutes()
    {
        var runState = GetRunState();
        var actMap = GetActMap();
        if (actMap == null || runState == null)
        {
            ModLogger.Warn("RecalculateRoutes: actMap or runState is null, skipping");
            return;
        }
        var (dW, rW) = GetEffectiveWeights();
        ModLogger.Info($"Recalculating routes — curMapCoord={runState.CurrentMapCoord?.ToString() ?? "NULL"}, danger={dW:F2} reward={rW:F2}");
        _currentResult = RouteDP.PlanRoutes(actMap, runState, dW, rW, _scoring, _constraints);
        if (_currentResult != null)
        {
            ModLogger.Info($"DP result: balanced={_currentResult.BalancedRoute.Count} nodes, " +
                $"highReward={_currentResult.HighRewardRoute.Count} nodes, safe={_currentResult.SafeRoute.Count} nodes");
        }
        else
        {
            ModLogger.Warn("DP returned null result");
        }
        if (_currentResult != null)
        {
            var (priPath, priSatisfied) = RouteDP.PlanPriorityRoute(actMap, runState, _priorityOrder, _scoring, _constraints);
            _currentResult.PriorityRoute = priPath;
            _currentResult.PriorityConstraintsSatisfied = priSatisfied;
        }
        _panel?.RefreshRoutes();
        _panel?.RefreshWeights();
        TryAutoDraw();
    }

    private void RecalculateDPOnly()
    {
        var runState = GetRunState();
        var actMap = GetActMap();
        if (actMap == null || runState == null)
        {
            ModLogger.Warn("RecalculateDPOnly: actMap or runState null, skipping");
            return;
        }
        var (dW, rW) = GetEffectiveWeights();
        LogActiveConstraints();
        _currentResult = RouteDP.PlanRoutes(actMap, runState, dW, rW, _scoring, _constraints);
        if (_currentResult != null)
        {
            var (priPath, priSatisfied) = RouteDP.PlanPriorityRoute(actMap, runState, _priorityOrder, _scoring, _constraints);
            _currentResult.PriorityRoute = priPath;
            _currentResult.PriorityConstraintsSatisfied = priSatisfied;
        }
        _panel?.RefreshRoutes();
        TryAutoDraw();
    }

    private void LogActiveConstraints()
    {
        var active = _constraints.Where(kv => kv.Value.Mode != ConstraintMode.None).ToList();
        if (active.Count == 0) return;
        foreach (var (type, c) in active)
            ModLogger.Info($"  Constraint[{type}]: mode={c.Mode} lower={c.LowerLimit} upper={c.UpperLimit}");
    }

    private void RecalculatePriorityRoute()
    {
        var runState = GetRunState();
        var actMap = GetActMap();
        if (actMap == null || runState == null || _currentResult == null) return;
        var (priPath, priSatisfied) = RouteDP.PlanPriorityRoute(actMap, runState, _priorityOrder, _scoring, _constraints);
        _currentResult.PriorityRoute = priPath;
        _currentResult.PriorityConstraintsSatisfied = priSatisfied;
    }

}
