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
        RouteScoringEngine scoring,
        IReadOnlyDictionary<MapPointType, NodeConstraint>? constraints = null)
    {
        if (map is GoldenPathActMap)
        {
            ModLogger.Info("RouteDP: GoldenPathActMap detected, using fast path");
            return BuildGoldenPathResult(map, runState, scoring, runState.CurrentMapPoint);
        }

        int rows = map.GetRowCount();
        ModLogger.Info($"RouteDP: rows={rows}");

        if (rows <= 2)
        {
            ModLogger.Warn("RouteDP: map has ≤2 rows, returning empty result");
            return new RoutePlanResult();
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
            startRow = map.StartingMapPoint.coord.row;
        }

        if (startRow >= map.BossMapPoint.coord.row)
        {
            ModLogger.Warn("RouteDP: player at or past boss, no routes");
            return new RoutePlanResult();
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

        // Determine which types have active constraints
        var activeTypes = constraints?
            .Where(kv => kv.Value.Mode != ConstraintMode.None)
            .Select(kv => kv.Key).ToList() ?? new List<MapPointType>();

        if (activeTypes.Count > 0)
            return PlanRoutesCountAware(map, runState, dangerWeight, rewardWeight, scoring,
                startPoint, startRow, rows, allBossParents, activeTypes, constraints!);

        return PlanRoutesStandard(map, runState, dangerWeight, rewardWeight, scoring,
            startPoint, startRow, rows, allBossParents, constraints);
    }

    // === Count-aware DP: tracks best score per count profile ===
    private static RoutePlanResult PlanRoutesCountAware(
        ActMap map, IRunState runState,
        double dangerWeight, double rewardWeight,
        RouteScoringEngine scoring,
        MapPoint startPoint, int startRow, int rows,
        List<(MapPoint parent, MapPoint boss)> allBossParents,
        List<MapPointType> constrainedTypes,
        IReadOnlyDictionary<MapPointType, NodeConstraint> constraints)
    {
        // Map each constrained type to an index (0..k-1) for count encoding
        var typeIndex = new Dictionary<MapPointType, int>();
        for (int i = 0; i < constrainedTypes.Count; i++)
            typeIndex[constrainedTypes[i]] = i;

        ModLogger.Info($"DP count-aware: constrained types={string.Join(",", constrainedTypes)}, boss parents={allBossParents.Count}");

        // DP state: per point → per count profile → (score, prev)
        var balByCount = new Dictionary<MapPoint, Dictionary<int, (double score, MapPoint? prev)>>();
        var rewByCount = new Dictionary<MapPoint, Dictionary<int, (double score, MapPoint? prev)>>();
        var safByCount = new Dictionary<MapPoint, Dictionary<int, (double score, MapPoint? prev)>>();

        int initRow = startRow + 1;
        int initCount = 0;
        foreach (var point in map.GetPointsInRow(initRow))
        {
            if (!startPoint.Children.Contains(point)) continue;
            initCount++;

            double dScore = scoring.CalcDangerScore(point, runState);
            double rScore = scoring.CalcRewardScore(point, runState);
            double combined = rewardWeight * rScore + (2 * dangerWeight - 1) * dScore;
            int key = CountKey(typeIndex, point.PointType);

            balByCount[point] = new() { [key] = (combined, startPoint) };
            rewByCount[point] = new() { [key] = (rScore, startPoint) };
            safByCount[point] = new() { [key] = (dScore, startPoint) };
        }
        if (initCount == 0) return new RoutePlanResult();

        // Row-by-row DP
        for (int row = initRow + 1; row < rows; row++)
        {
            foreach (var point in map.GetPointsInRow(row))
            {
                double dScore = scoring.CalcDangerScore(point, runState);
                double rScore = scoring.CalcRewardScore(point, runState);
                double combined = rewardWeight * rScore + (2 * dangerWeight - 1) * dScore;
                int addKey = CountKey(typeIndex, point.PointType);

                var balMap = new Dictionary<int, (double score, MapPoint? prev)>();
                var rewMap = new Dictionary<int, (double score, MapPoint? prev)>();
                var safMap = new Dictionary<int, (double score, MapPoint? prev)>();

                foreach (var parent in point.parents)
                {
                    MergeFromParent(balByCount, parent, addKey, combined, balMap,
                        (candidate, best) => candidate > best, double.MinValue);
                    MergeFromParent(rewByCount, parent, addKey, rScore, rewMap,
                        (candidate, best) => candidate > best, double.MinValue);
                    MergeFromParent(safByCount, parent, addKey, dScore, safMap,
                        (candidate, best) => candidate < best, double.MaxValue);
                }

                if (balMap.Count > 0) balByCount[point] = balMap;
                if (rewMap.Count > 0) rewByCount[point] = rewMap;
                if (safMap.Count > 0) safByCount[point] = safMap;
            }
        }

        // Select best valid paths from boss parents
        var result = new RoutePlanResult();

        var balBest = FindBestValid(balByCount, allBossParents, map, constrainedTypes, typeIndex, constraints, false);
        var rewBest = FindBestValid(rewByCount, allBossParents, map, constrainedTypes, typeIndex, constraints, false);
        var safBest = FindBestValid(safByCount, allBossParents, map, constrainedTypes, typeIndex, constraints, true);

        result.BalancedRoute = balBest.path;
        result.HighRewardRoute = rewBest.path;
        result.SafeRoute = safBest.path;
        result.BalancedScore = balBest.score;
        result.HighRewardScore = rewBest.score;
        result.SafeScore = safBest.score;
        result.BalancedConstraintsSatisfied = balBest.constraintsSatisfied;
        result.HighRewardConstraintsSatisfied = rewBest.constraintsSatisfied;
        result.SafeConstraintsSatisfied = safBest.constraintsSatisfied;

        ApplyRouteConstraints(result, map, runState, scoring, dangerWeight, rewardWeight);
        return result;
    }

    // Encode a count increment for a point type. Returns 0 if type is not constrained.
    private static int CountKey(Dictionary<MapPointType, int> typeIndex, MapPointType pt)
    {
        if (typeIndex.TryGetValue(pt, out int idx))
            return 1 << (idx * 4); // Adds 1 to that type's count when OR'd
        return 0;
    }

    // Merge a parent's count profiles into the current point's map
    private static void MergeFromParent(
        Dictionary<MapPoint, Dictionary<int, (double score, MapPoint? prev)>> byCount,
        MapPoint parent, int addKey, double baseScore,
        Dictionary<int, (double score, MapPoint? prev)> target,
        Func<double, double, bool> isBetter, double worstScore)
    {
        if (!byCount.TryGetValue(parent, out var parentMap)) return;

        foreach (var (parentKey, (parentScore, _)) in parentMap)
        {
            // Compute new count key: add the increment for this type
            int newKey = parentKey + addKey;
            if (addKey > 0)
            {
                // Check for overflow of the 4-bit field
                int shift = 0;
                int temp = addKey;
                while ((temp & 1) == 0) { temp >>= 4; shift += 4; }
                int count = ((parentKey >> shift) & 0xF) + 1;
                if (count > 15) continue; // overflow, skip
            }

            double candidate = parentScore + baseScore;
            if (!target.TryGetValue(newKey, out var existing) || isBetter(candidate, existing.score))
                target[newKey] = (candidate, parent);
        }
    }

    // Find best valid path among all boss parents. Returns empty path when no profile satisfies constraints.
    private static (List<MapPoint> path, double score, bool constraintsSatisfied) FindBestValid(
        Dictionary<MapPoint, Dictionary<int, (double score, MapPoint? prev)>> byCount,
        List<(MapPoint parent, MapPoint boss)> allBossParents,
        ActMap map, List<MapPointType> constrainedTypes,
        Dictionary<MapPointType, int> typeIndex,
        IReadOnlyDictionary<MapPointType, NodeConstraint> constraints,
        bool ascending)
    {
        MapPoint? bestEnd = null, bestBoss = null;
        int bestKey = 0;
        double bestScore = ascending ? double.MaxValue : double.MinValue;
        int checked_ = 0, valid_ = 0;

        foreach (var (p, boss) in allBossParents)
        {
            if (!byCount.TryGetValue(p, out var countMap)) continue;

            foreach (var (key, (score, _)) in countMap)
            {
                checked_++;
                bool valid = true;
                foreach (var (type, constraint) in constraints)
                {
                    if (constraint.Mode == ConstraintMode.None) continue;
                    int idx = typeIndex[type];
                    int count = (key >> (idx * 4)) & 0xF;
                    if (!constraint.IsSatisfied(count)) { valid = false; break; }
                }

                if (valid)
                {
                    valid_++;
                    if (ascending ? score < bestScore : score > bestScore)
                    { bestScore = score; bestEnd = p; bestBoss = boss; bestKey = key; }
                }
            }
        }

        if (constraints.Any(c => c.Value.Mode != ConstraintMode.None))
            ModLogger.Info($"DP count-aware: {valid_}/{checked_} profiles valid, {(bestEnd != null ? "OK" : "NO VALID PATH")}");

        if (bestEnd == null)
            return (new List<MapPoint>(), 0, false);

        // Backtrack through count-aware prev chain
        var path = new List<MapPoint>();
        path.Add(bestBoss ?? map.BossMapPoint);
        var cur = bestEnd;
        var curKey = bestKey;
        while (cur != null)
        {
            path.Add(cur);
            if (byCount.TryGetValue(cur, out var cm) && cm.TryGetValue(curKey, out var entry))
            {
                curKey -= CountKey(typeIndex, cur.PointType);
                cur = entry.prev;
            }
            else break;
        }
        path.Reverse();

        return (path, bestScore, true);
    }

    // === Standard DP (no constraints) ===
    private static RoutePlanResult PlanRoutesStandard(
        ActMap map, IRunState runState,
        double dangerWeight, double rewardWeight,
        RouteScoringEngine scoring,
        MapPoint startPoint, int startRow, int rows,
        List<(MapPoint parent, MapPoint boss)> allBossParents,
        IReadOnlyDictionary<MapPointType, NodeConstraint>? constraints)
    {
        var bestPrev = new Dictionary<MapPoint, MapPoint?>();
        var bestScore = new Dictionary<MapPoint, double>();
        var rewardPrev = new Dictionary<MapPoint, MapPoint?>();
        var rewardScore = new Dictionary<MapPoint, double>();
        var safePrev = new Dictionary<MapPoint, MapPoint?>();
        var safeScore = new Dictionary<MapPoint, double>();

        int initRow = startRow + 1;
        int initCount = 0;
        foreach (var point in map.GetPointsInRow(initRow))
        {
            if (!startPoint.Children.Contains(point)) continue;

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
        if (initCount == 0) return new RoutePlanResult();

        for (int row = initRow + 1; row < rows; row++)
        {
            foreach (var point in map.GetPointsInRow(row))
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
                    if (!bestScore.ContainsKey(parent)) continue;

                    double candidate = bestScore[parent] + combinedBase;
                    if (candidate > bestScore[point])
                    { bestScore[point] = candidate; bestPrev[point] = parent; }

                    double rCandidate = rewardScore[parent] + rScore;
                    if (rCandidate > rewardScore[point])
                    { rewardScore[point] = rCandidate; rewardPrev[point] = parent; }

                    double sCandidate = safeScore[parent] + dScore;
                    if (sCandidate < safeScore[point])
                    { safeScore[point] = sCandidate; safePrev[point] = parent; }
                }
            }
        }

        var result = new RoutePlanResult();
        var balanced = SelectBestValidEnd(allBossParents, bestScore, bestPrev, map, constraints, ascending: false);
        var reward = SelectBestValidEnd(allBossParents, rewardScore, rewardPrev, map, constraints, ascending: false);
        var safe = SelectBestValidEnd(allBossParents, safeScore, safePrev, map, constraints, ascending: true);

        result.BalancedRoute = balanced.path;
        result.HighRewardRoute = reward.path;
        result.SafeRoute = safe.path;
        result.BalancedScore = balanced.score;
        result.HighRewardScore = reward.score;
        result.SafeScore = safe.score;
        result.BalancedConstraintsSatisfied = balanced.constraintsSatisfied;
        result.HighRewardConstraintsSatisfied = reward.constraintsSatisfied;
        result.SafeConstraintsSatisfied = safe.constraintsSatisfied;

        ApplyRouteConstraints(result, map, runState, scoring, dangerWeight, rewardWeight);
        return result;
    }

    private static (List<MapPoint> path, double score, bool constraintsSatisfied) SelectBestValidEnd(
        List<(MapPoint parent, MapPoint boss)> allBossParents,
        Dictionary<MapPoint, double> scores,
        Dictionary<MapPoint, MapPoint?> prev,
        ActMap map,
        IReadOnlyDictionary<MapPointType, NodeConstraint>? constraints,
        bool ascending)
    {
        // Sort candidates by score — first valid is best-scoring valid
        var ordered = ascending
            ? allBossParents.Where(x => scores.ContainsKey(x.parent)).OrderBy(x => scores[x.parent])
            : allBossParents.Where(x => scores.ContainsKey(x.parent)).OrderByDescending(x => scores[x.parent]);

        (MapPoint? end, MapPoint? boss, double score) best = (null, null, ascending ? double.MaxValue : double.MinValue);
        int checked_ = 0, valid_ = 0;

        foreach (var (p, boss) in ordered)
        {
            checked_++;
            var path = BuildFullPath(prev, p, boss, map);

            if (SatisfiesAllConstraints(path, constraints))
            {
                valid_++;
                if (best.end == null)
                {
                    best = (p, boss, scores[p]);
                    var future = path.Skip(1).ToList();
                    var counts = future.GroupBy(n => n.PointType).ToDictionary(g => g.Key, g => g.Count());
                    ModLogger.Info($"DP: valid path found (score={scores[p]:F1}), types: {string.Join(", ", counts.Select(kv => $"{kv.Key}={kv.Value}"))}");
                    break;
                }
            }
        }

        if (constraints != null && constraints.Any(c => c.Value.Mode != ConstraintMode.None))
            ModLogger.Info($"DP constraint filter: {valid_}/{checked_} candidates valid, {(best.end != null ? "OK" : "NO VALID PATH")}");

        if (best.end == null)
            return (new List<MapPoint>(), 0, false);

        return (BuildFullPath(prev, best.end, best.boss, map), best.score, true);
    }

    private static bool SatisfiesAllConstraints(
        List<MapPoint> path,
        IReadOnlyDictionary<MapPointType, NodeConstraint>? constraints)
    {
        if (constraints == null || constraints.Count == 0) return true;
        // Skip path[0] — the player's current position, not a future choice
        var futureNodes = path.Skip(1).ToList();
        foreach (var (type, constraint) in constraints)
        {
            if (constraint.Mode == ConstraintMode.None) continue;
            int count = futureNodes.Count(p => p.PointType == type);
            if (!constraint.IsSatisfied(count))
            {
                ModLogger.Info($"  Constraint FAIL: {type} count={count} not in [{constraint.LowerLimit},{constraint.UpperLimit}] (mode={constraint.Mode})");
                return false;
            }
        }
        return true;
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

    public static (List<MapPoint> path, bool constraintsSatisfied) PlanPriorityRoute(
        ActMap map, IRunState runState,
        MapPointType[] priorityOrder,
        RouteScoringEngine scoring,
        IReadOnlyDictionary<MapPointType, NodeConstraint>? constraints = null)
    {
        if (priorityOrder == null || priorityOrder.Length == 0)
            return (new List<MapPoint>(), true);

        if (map is GoldenPathActMap)
        {
            var gpResult = BuildGoldenPathResult(map, runState, scoring, runState.CurrentMapPoint);
            return (gpResult.BalancedRoute, true);
        }

        int rows = map.GetRowCount();
        if (rows <= 2) return (new List<MapPoint>(), true);

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
            return (new List<MapPoint>(), true);

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

        // Determine which types have active constraints
        var constrainedTypes = constraints?
            .Where(kv => kv.Value.Mode != ConstraintMode.None)
            .Select(kv => kv.Key).ToList() ?? new List<MapPointType>();

        if (constrainedTypes.Count > 0)
            return PlanPriorityRouteCountAware(map, startPoint, startRow, rows, allBossParents,
                priorityOrder, typeSlot, nSlots, IsBetter, EmptyVec, AddNode, constrainedTypes, constraints!);

        // === Original single-profile DP (no active constraints) ===
        return PlanPriorityRouteStandard(map, startPoint, startRow, rows, allBossParents,
            priorityOrder, typeSlot, nSlots, IsBetter, EmptyVec, AddNode, constraints);
    }

    private static (List<MapPoint> path, bool constraintsSatisfied) PlanPriorityRouteStandard(
        ActMap map, MapPoint startPoint, int startRow, int rows,
        List<(MapPoint parent, MapPoint boss)> allBossParents,
        MapPointType[] priorityOrder,
        Dictionary<MapPointType, int> typeSlot, int nSlots,
        Func<int[], int[], bool> IsBetter,
        Func<int[]> EmptyVec,
        Func<int[], MapPointType, int[]> AddNode,
        IReadOnlyDictionary<MapPointType, NodeConstraint>? constraints)
    {
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

        MapPoint? bestEnd = null, bestBoss = null;
        int[] bestCounts = EmptyVec();
        int priChecked = 0, priValid = 0;

        foreach (var (p, boss) in allBossParents)
        {
            if (!counts.TryGetValue(p, out var c)) continue;
            priChecked++;

            if (PriorityCountsSatisfyConstraints(c, priorityOrder, constraints))
            {
                priValid++;
                if (IsBetter(c, bestCounts))
                { bestCounts = c; bestEnd = p; bestBoss = boss; }
            }
        }

        if (constraints != null && constraints.Any(c2 => c2.Value.Mode != ConstraintMode.None))
            ModLogger.Info($"DP priority filter: {priValid}/{priChecked} valid, {(bestEnd != null ? "OK" : "NO VALID PATH")}");

        if (bestEnd == null)
            return (new List<MapPoint>(), false);

        return (BuildFullPath(prev, bestEnd, bestBoss, map), true);
    }

    private static (List<MapPoint> path, bool constraintsSatisfied) PlanPriorityRouteCountAware(
        ActMap map, MapPoint startPoint, int startRow, int rows,
        List<(MapPoint parent, MapPoint boss)> allBossParents,
        MapPointType[] priorityOrder,
        Dictionary<MapPointType, int> typeSlot, int nSlots,
        Func<int[], int[], bool> IsBetter,
        Func<int[]> EmptyVec,
        Func<int[], MapPointType, int[]> AddNode,
        List<MapPointType> constrainedTypes,
        IReadOnlyDictionary<MapPointType, NodeConstraint> constraints)
    {
        var typeIndex = new Dictionary<MapPointType, int>();
        for (int i = 0; i < constrainedTypes.Count; i++)
            typeIndex[constrainedTypes[i]] = i;

        ModLogger.Info($"DP priority count-aware: constrained types={string.Join(",", constrainedTypes)}");

        // DP state: per point → per constraint profile → (priority count vector, prev)
        var byCount = new Dictionary<MapPoint, Dictionary<int, (int[] counts, MapPoint? prev)>>();

        int initRow = startRow + 1;
        int initCount = 0;
        foreach (var point in map.GetPointsInRow(initRow))
        {
            if (!startPoint.Children.Contains(point)) continue;
            initCount++;
            var vec = AddNode(EmptyVec(), point.PointType);
            int key = CountKey(typeIndex, point.PointType);
            byCount[point] = new() { [key] = (vec, startPoint) };
        }
        if (initCount == 0) return (new List<MapPoint>(), true);

        // Row-by-row DP — track best priority count vector per constraint profile
        for (int row = initRow + 1; row < rows; row++)
        {
            foreach (var point in map.GetPointsInRow(row))
            {
                int addKey = CountKey(typeIndex, point.PointType);
                var pointMap = new Dictionary<int, (int[] counts, MapPoint? prev)>();

                foreach (var parent in point.parents)
                {
                    if (!byCount.TryGetValue(parent, out var parentMap)) continue;

                    foreach (var (parentKey, (parentCounts, _)) in parentMap)
                    {
                        int newKey = parentKey + addKey;
                        if (addKey > 0)
                        {
                            int shift = 0; int temp = addKey;
                            while ((temp & 1) == 0) { temp >>= 4; shift += 4; }
                            int count = ((parentKey >> shift) & 0xF) + 1;
                            if (count > 15) continue;
                        }
                        var newCounts = AddNode(parentCounts, point.PointType);
                        if (!pointMap.TryGetValue(newKey, out var existing) || IsBetter(newCounts, existing.counts))
                            pointMap[newKey] = (newCounts, parent);
                    }
                }

                if (pointMap.Count > 0) byCount[point] = pointMap;
            }
        }

        // Select best valid path from boss parents
        MapPoint? bestEnd = null, bestBoss = null;
        int[] bestVec = EmptyVec();
        int bestKey = 0;
        int priChecked = 0, priValid = 0;

        foreach (var (p, boss) in allBossParents)
        {
            if (!byCount.TryGetValue(p, out var countMap)) continue;
            foreach (var (key, (vec, _)) in countMap)
            {
                priChecked++;
                bool valid = true;
                foreach (var (type, c) in constraints)
                {
                    if (c.Mode == ConstraintMode.None) continue;
                    int idx = typeIndex[type];
                    int count = (key >> (idx * 4)) & 0xF;
                    if (!c.IsSatisfied(count)) { valid = false; break; }
                }

                if (valid)
                {
                    priValid++;
                    if (IsBetter(vec, bestVec))
                    { bestVec = vec; bestEnd = p; bestBoss = boss; bestKey = key; }
                }
            }
        }

        ModLogger.Info($"DP priority count-aware: {priValid}/{priChecked} profiles valid, {(bestEnd != null ? "OK" : "NO VALID PATH")}");

        if (bestEnd == null)
            return (new List<MapPoint>(), false);

        // Backtrack through count-aware prev chain
        var path = new List<MapPoint>();
        path.Add(bestBoss ?? map.BossMapPoint);
        var cur = bestEnd;
        var curKey = bestKey;
        while (cur != null)
        {
            path.Add(cur);
            if (byCount.TryGetValue(cur, out var cm) && cm.TryGetValue(curKey, out var entry))
            {
                curKey -= CountKey(typeIndex, cur.PointType);
                cur = entry.prev;
            }
            else break;
        }
        path.Reverse();

        return (path, true);
    }

    private static bool PriorityCountsSatisfyConstraints(
        int[] counts, MapPointType[] priorityOrder,
        IReadOnlyDictionary<MapPointType, NodeConstraint>? constraints)
    {
        if (constraints == null || constraints.Count == 0) return true;
        for (int i = 0; i < priorityOrder.Length; i++)
        {
            var type = priorityOrder[i];
            if (constraints.TryGetValue(type, out var c) && !c.IsSatisfied(counts[i]))
                return false;
        }
        return true;
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
