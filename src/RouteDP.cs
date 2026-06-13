using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Runs;

namespace RoutePlanner;

public static class RouteDP
{
    public static RoutePlanResult PlanRoutes(
        ActMap map, IRunState runState,
        double dangerWeight, double rewardWeight,
        RouteScoringEngine scoring)
    {
        if (map is GoldenPathActMap)
        {
            ModLogger.Info("RouteDP: GoldenPathActMap detected, using fast path");
            return BuildGoldenPathResult(map, runState, scoring, runState.CurrentMapPoint);
        }

        int rows = map.GetRowCount();
        ModLogger.Info($"RouteDP: rows={rows}, total nodes≈{rows * 7}");

        if (rows <= 2)
        {
            ModLogger.Warn("RouteDP: map has ≤2 rows, returning empty result");
            return new RoutePlanResult();
        }

        // Determine start point: player's current position, or fallback to Ancient
        var currentPoint = runState.CurrentMapPoint;
        int startRow;
        MapPoint startPoint;

        if (currentPoint != null)
        {
            startPoint = currentPoint;
            startRow = currentPoint.coord.row;
            ModLogger.Info($"RouteDP: starting from player position — coord=({startPoint.coord.col},{startPoint.coord.row}), type={startPoint.PointType}");
        }
        else
        {
            // CurrentMapPoint is null when _visitedMapCoords is empty (e.g. multiplayer client sync delay)
            startPoint = map.StartingMapPoint;
            startRow = 0;
            ModLogger.Warn("RouteDP: CurrentMapPoint is null, falling back to Ancient");
        }

        // If player is at boss or beyond, no routes
        if (startRow >= map.BossMapPoint.coord.row)
        {
            ModLogger.Warn("RouteDP: player at or past boss, no routes");
            return new RoutePlanResult();
        }

        var bestPrev = new Dictionary<MapPoint, MapPoint?>();
        var bestScore = new Dictionary<MapPoint, double>();
        var rewardPrev = new Dictionary<MapPoint, MapPoint?>();
        var rewardScore = new Dictionary<MapPoint, double>();
        var safePrev = new Dictionary<MapPoint, MapPoint?>();
        var safeScore = new Dictionary<MapPoint, double>();

        // Initialize: children of the start point
        int initRow = startRow + 1;
        int initCount = 0;
        foreach (var point in map.GetPointsInRow(initRow))
        {
            // Only include points that are children of startPoint
            if (!startPoint.Children.Contains(point))
                continue;

            double dScore = scoring.CalcDangerScore(point, runState);
            double rScore = scoring.CalcRewardScore(point, runState);

            bestScore[point] = rewardWeight * rScore + (2 * dangerWeight - 1) * dScore;
            rewardScore[point] = rScore;
            safeScore[point] = dScore;
            bestPrev[point] = startPoint;
            rewardPrev[point] = startPoint;
            safePrev[point] = startPoint;
            initCount++;
        }
        ModLogger.Info($"RouteDP: initialized {initCount} nodes from start row {startRow} (init row {initRow})");

        if (initCount == 0)
        {
            ModLogger.Warn("RouteDP: no children found from start point");
            return new RoutePlanResult();
        }

        // Row-by-row DP: from (initRow+1) to rows-1 (boss parents)
        for (int row = initRow + 1; row < rows; row++)
        {
            var rowPoints = map.GetPointsInRow(row).ToList();

            foreach (var point in rowPoints)
            {
                double dScore = scoring.CalcDangerScore(point, runState);
                double rScore = scoring.CalcRewardScore(point, runState);
                double combinedBase = rewardWeight * rScore + (2 * dangerWeight - 1) * dScore;

                bestScore[point] = double.MinValue;
                rewardScore[point] = double.MinValue;
                safeScore[point] = double.MaxValue;
                bestPrev[point] = null;
                rewardPrev[point] = null;
                safePrev[point] = null;

                foreach (var parent in point.parents)
                {
                    if (!bestScore.ContainsKey(parent))
                        continue;

                    double candidate = bestScore[parent] + combinedBase;
                    if (candidate > bestScore[point])
                    {
                        bestScore[point] = candidate;
                        bestPrev[point] = parent;
                    }

                    double rCandidate = rewardScore[parent] + rScore;
                    if (rCandidate > rewardScore[point])
                    {
                        rewardScore[point] = rCandidate;
                        rewardPrev[point] = parent;
                    }

                    double sCandidate = safeScore[parent] + dScore;
                    if (sCandidate < safeScore[point])
                    {
                        safeScore[point] = sCandidate;
                        safePrev[point] = parent;
                    }
                }
            }
        }

        // Termination: collect all parents of Boss (and SecondBoss if present)
        var allBossParents = new List<(MapPoint parent, MapPoint boss)>();
        var bossParents = map.BossMapPoint.parents;
        if (bossParents != null)
            foreach (var p in bossParents)
                allBossParents.Add((p, map.BossMapPoint));
        if (map.SecondBossMapPoint != null)
        {
            var secondParents = map.SecondBossMapPoint.parents;
            if (secondParents != null)
                foreach (var p in secondParents)
                    allBossParents.Add((p, map.SecondBossMapPoint));
        }
        ModLogger.Info($"RouteDP: allBossParents={allBossParents.Count}, bestPrev={bestPrev.Count}");

        MapPoint? bestEnd = null, rewardEnd = null, safeEnd = null;
        MapPoint? bestBoss = null, rewardBoss = null, safeBoss = null;
        double bestMax = double.MinValue, rewardMax = double.MinValue, safeMin = double.MaxValue;

        foreach (var (p, boss) in allBossParents)
        {
            if (bestScore.TryGetValue(p, out var bs) && bs > bestMax)
            { bestMax = bs; bestEnd = p; bestBoss = boss; }
            if (rewardScore.TryGetValue(p, out var rs) && rs > rewardMax)
            { rewardMax = rs; rewardEnd = p; rewardBoss = boss; }
            if (safeScore.TryGetValue(p, out var ss) && ss < safeMin)
            { safeMin = ss; safeEnd = p; safeBoss = boss; }
        }
        ModLogger.Info($"RouteDP: end nodes — bestEnd={bestEnd != null}, rewardEnd={rewardEnd != null}, safeEnd={safeEnd != null}");

        var result = new RoutePlanResult
        {
            BalancedRoute = BuildFullPath(bestPrev, bestEnd, bestBoss, map),
            HighRewardRoute = BuildFullPath(rewardPrev, rewardEnd, rewardBoss, map),
            SafeRoute = BuildFullPath(safePrev, safeEnd, safeBoss, map),
            BalancedScore = bestMax,
            HighRewardScore = rewardMax,
            SafeScore = safeMin,
        };

        ApplyRouteConstraints(result, map, runState, scoring, dangerWeight, rewardWeight);

        return result;
    }

    private static void ApplyRouteConstraints(
        RoutePlanResult result, ActMap map, IRunState runState,
        RouteScoringEngine scoring, double dangerWeight, double rewardWeight)
    {
        // High-reward route must contain at least 1 rest site
        if (result.HighRewardRoute.All(p => p.PointType != MapPointType.RestSite))
        {
            ModLogger.Info("Constraint: high-reward has no rest site, substituting balanced route");
            result.HighRewardRoute = result.BalancedRoute;
            result.HighRewardScore = result.BalancedScore;
        }

        // Safe route must have at least 30% of balanced reward
        double safeRewardSum = result.SafeRoute.Sum(p => scoring.CalcRewardScore(p, runState));
        double balancedRewardSum = result.BalancedRoute.Sum(p => scoring.CalcRewardScore(p, runState));
        if (balancedRewardSum > 0 && safeRewardSum < balancedRewardSum * 0.3)
        {
            ModLogger.Info($"Constraint: safe reward ({safeRewardSum:F0}) < 30% of balanced ({balancedRewardSum:F0}), substituting");
            result.SafeRoute = result.BalancedRoute;
            result.SafeScore = result.BalancedScore;
        }
    }

    private static List<MapPoint> BuildFullPath(
        Dictionary<MapPoint, MapPoint?> prev, MapPoint? end,
        MapPoint? boss, ActMap map)
    {
        var path = new List<MapPoint>();
        if (end == null) return path;

        // Add boss at the end
        path.Add(boss ?? map.BossMapPoint);

        // Backtrack through the DP prev chain
        var cur = end;
        while (cur != null)
        {
            path.Add(cur);
            prev.TryGetValue(cur, out var next);
            cur = next;
        }

        // Don't add StartingMapPoint — the DP already includes the player's start
        path.Reverse();
        return path;
    }

    public static List<MapPoint> PlanPriorityRoute(
        ActMap map, IRunState runState,
        MapPointType[] priorityOrder,
        RouteScoringEngine scoring)
    {
        if (priorityOrder == null || priorityOrder.Length == 0)
            return new List<MapPoint>();

        if (map is GoldenPathActMap)
        {
            var gpResult = BuildGoldenPathResult(map, runState, scoring, runState.CurrentMapPoint);
            return gpResult.BalancedRoute;
        }

        int rows = map.GetRowCount();
        if (rows <= 2) return new List<MapPoint>();

        // Map each type to its priority slot (0 = most important)
        var typeSlot = new Dictionary<MapPointType, int>();
        for (int i = 0; i < priorityOrder.Length; i++)
            typeSlot[priorityOrder[i]] = i;

        int nSlots = priorityOrder.Length;

        // Lexicographic comparison: compare slot0 first, if equal compare slot1, etc.
        bool IsBetter(int[] a, int[] b)
        {
            for (int i = 0; i < nSlots; i++)
            {
                if (a[i] > b[i]) return true;
                if (a[i] < b[i]) return false;
            }
            return false;
        }

        int[] EmptyVec() => new int[nSlots];

        int[] AddNode(int[] vec, MapPointType type)
        {
            var copy = (int[])vec.Clone();
            if (typeSlot.TryGetValue(type, out int slot))
                copy[slot]++;
            return copy;
        }

        var currentPoint = runState.CurrentMapPoint;
        int startRow;
        MapPoint startPoint;

        if (currentPoint != null)
        {
            startPoint = currentPoint;
            startRow = currentPoint.coord.row;
        }
        else
        {
            startPoint = map.StartingMapPoint;
            startRow = 0;
        }

        if (startRow >= map.BossMapPoint.coord.row)
            return new List<MapPoint>();

        var prev = new Dictionary<MapPoint, MapPoint?>();
        var counts = new Dictionary<MapPoint, int[]>();

        int initRow = startRow + 1;
        foreach (var point in map.GetPointsInRow(initRow))
        {
            if (!startPoint.Children.Contains(point)) continue;
            counts[point] = AddNode(EmptyVec(), point.PointType);
            prev[point] = startPoint;
        }

        for (int row = initRow + 1; row < rows; row++)
        {
            foreach (var point in map.GetPointsInRow(row))
            {
                counts[point] = EmptyVec();
                prev[point] = null;

                foreach (var parent in point.parents)
                {
                    if (!counts.ContainsKey(parent)) continue;
                    var candidate = AddNode(counts[parent], point.PointType);
                    if (IsBetter(candidate, counts[point]))
                    {
                        counts[point] = candidate;
                        prev[point] = parent;
                    }
                }
            }
        }

        // Collect boss parents
        var allBossParents = new List<(MapPoint parent, MapPoint boss)>();
        var bossParents = map.BossMapPoint.parents;
        if (bossParents != null)
            foreach (var p in bossParents)
                allBossParents.Add((p, map.BossMapPoint));
        if (map.SecondBossMapPoint != null)
        {
            var secondParents = map.SecondBossMapPoint.parents;
            if (secondParents != null)
                foreach (var p in secondParents)
                    allBossParents.Add((p, map.SecondBossMapPoint));
        }

        MapPoint? bestEnd = null;
        MapPoint? bestBoss = null;
        int[] bestCounts = EmptyVec();

        foreach (var (p, boss) in allBossParents)
        {
            if (counts.TryGetValue(p, out var c) && IsBetter(c, bestCounts))
            {
                bestCounts = c;
                bestEnd = p;
                bestBoss = boss;
            }
        }

        return BuildFullPath(prev, bestEnd, bestBoss, map);
    }

    private static RoutePlanResult BuildGoldenPathResult(
        ActMap map, IRunState runState, RouteScoringEngine scoring,
        MapPoint? currentPoint)
    {
        // For GoldenPath, build from current position or from start
        var path = new List<MapPoint>();
        var start = currentPoint ?? map.StartingMapPoint;
        var cur = (MapPoint?)start;
        while (cur != null)
        {
            path.Add(cur);
            cur = cur.Children.FirstOrDefault();
        }

        double totalDanger = path.Sum(p => scoring.CalcDangerScore(p, runState));
        double totalReward = path.Sum(p => scoring.CalcRewardScore(p, runState));

        return new RoutePlanResult
        {
            BalancedRoute = path,
            HighRewardRoute = path,
            SafeRoute = path,
            PriorityRoute = path,
            BalancedScore = totalReward - totalDanger,
            HighRewardScore = totalReward,
            SafeScore = totalDanger,
        };
    }
}
