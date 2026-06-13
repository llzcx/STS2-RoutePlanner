using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

namespace RoutePlanner;

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

    private static readonly MapPointType[] DefaultPriorityOrder =
    {
        MapPointType.Elite, MapPointType.Monster, MapPointType.RestSite,
        MapPointType.Shop, MapPointType.Treasure, MapPointType.Unknown,
    };

    private MapPointType[] _priorityOrder = LoadPriorityOrder();

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
        RecalculateDPOnly();
    }

    public void OnDimensionChanged(bool dangerEnabled, bool rewardEnabled)
    {
        _dangerEnabled = dangerEnabled;
        _rewardEnabled = rewardEnabled;
        RecalculateDPOnly();
    }

    private (double dangerW, double rewardW) GetEffectiveWeights()
    {
        double d = _dangerEnabled ? _dangerWeight : 0;
        double r = _rewardEnabled ? _rewardWeight : 0;
        return (d, r);
    }

    public void OnPresetSelected(string preset)
    {
        var presets = RouteScoringConfig.Current.WeightPresets;
        if (presets.TryGetValue(preset, out var p))
        {
            _dangerWeight = p.DangerWeight;
            _rewardWeight = p.RewardWeight;
            RecalculateDPOnly();
            _panel?.SetSliderValues(_dangerWeight, _rewardWeight);
        }
    }

    public void OnRouteSelected(int index)
    {
        _selectedRouteIndex = index;
    }

    public void OnDrawClicked()
    {
        var route = GetSelectedRoute();
        if (route != null)
        {
            ModLogger.Info($"Draw clicked — route has {route.Count} nodes");
            _drawingManager?.DrawRoute(route, Colors.White);
        }
        else
        {
            ModLogger.Warn("Draw clicked but no route selected");
        }
    }

    public void OnClearClicked()
    {
        _drawingManager?.ClearAllDrawings();
    }

    public Dictionary<string, (double danger, double reward)> GetEffectiveTypeScores()
    {
        if (_runState == null) return new();
        return _scoring.GetEffectiveTypeScores(_runState);
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
        double danger = 0, reward = 0;
        if (_runState != null)
        {
            danger = route.Sum(p => _scoring.CalcDangerScore(p, _runState));
            reward = route.Sum(p => _scoring.CalcRewardScore(p, _runState));
        }
        int elites = route.Count(p => p.PointType == MapPointType.Elite);
        int rests = route.Count(p => p.PointType == MapPointType.RestSite);
        int treasures = route.Count(p => p.PointType == MapPointType.Treasure);
        int shops = route.Count(p => p.PointType == MapPointType.Shop);
        int monsters = route.Count(p => p.PointType == MapPointType.Monster);
        int unknowns = route.Count(p => p.PointType == MapPointType.Unknown);

        var (dW, rW) = GetEffectiveWeights();
        string counts = BuildCountsString(elites, rests, treasures, shops, monsters, unknowns);

        return index switch
        {
            0 => $"{I18n.Tr("自定义")} {I18n.Tr("危险")}{F(danger)} {I18n.Tr("奖励")}{F(reward)} | {counts}",
            1 => $"{I18n.Tr("定向")} {I18n.Tr("危险")}{F(danger)} {I18n.Tr("奖励")}{F(reward)} | {counts}",
            2 => $"{I18n.Tr("高收益")} {I18n.Tr("奖励")}{F(reward)} | {counts}",
            3 => $"{I18n.Tr("保守")} {I18n.Tr("危险")}{F(danger)} | {counts}",
            _ => "",
        };
    }

    public MapPointType[] GetPriorityOrder() => _priorityOrder;

    private static string F(double v) => v.ToString("F0");

    private static string BuildCountsString(int elites, int rests, int treasures, int shops, int monsters, int unknowns)
    {
        string counts = $"{elites}{I18n.Tr("精英")} {rests}{I18n.Tr("休息")} {treasures}{I18n.Tr("宝箱")} {shops}{I18n.Tr("商店")} {monsters}{I18n.Tr("普通")}";
        if (unknowns > 0) counts += $" {unknowns}{I18n.Tr("未知")}";
        return counts;
    }

    private void RecalculateRoutes()
    {
        if (_actMap == null || _runState == null)
        {
            ModLogger.Warn("RecalculateRoutes: actMap or runState is null, skipping");
            return;
        }
        var (dW, rW) = GetEffectiveWeights();
        ModLogger.Info($"Recalculating routes with weights danger={dW:F2} (enabled={_dangerEnabled}) reward={rW:F2} (enabled={_rewardEnabled})");
        _currentResult = RouteDP.PlanRoutes(_actMap, _runState, dW, rW, _scoring);
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
            _currentResult.PriorityRoute = RouteDP.PlanPriorityRoute(_actMap, _runState, _priorityOrder, _scoring);
        _panel?.RefreshRoutes();
    }

    private void RecalculateDPOnly()
    {
        if (_actMap == null || _runState == null) return;
        var (dW, rW) = GetEffectiveWeights();
        _currentResult = RouteDP.PlanRoutes(_actMap, _runState, dW, rW, _scoring);
        _currentResult.PriorityRoute = RouteDP.PlanPriorityRoute(_actMap, _runState, _priorityOrder, _scoring);
        _panel?.RefreshRoutes();
    }

    private void RecalculatePriorityRoute()
    {
        if (_actMap == null || _runState == null || _currentResult == null) return;
        _currentResult.PriorityRoute = RouteDP.PlanPriorityRoute(_actMap, _runState, _priorityOrder, _scoring);
    }

}
