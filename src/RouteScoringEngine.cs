using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace RoutePlanner;

public class RouteScoringEngine
{
    private readonly Dictionary<MapPoint, double> _dangerCache = new();
    private readonly Dictionary<MapPoint, double> _rewardCache = new();
    private int _cachedStateHash;

    public void InvalidateCache()
    {
        _dangerCache.Clear();
        _rewardCache.Clear();
        _cachedStateHash = 0;
    }

    public double CalcDangerScore(MapPoint point, IRunState runState)
    {
        int stateHash = HashRunState(runState);
        if (stateHash != 0 && stateHash == _cachedStateHash && _dangerCache.TryGetValue(point, out var cached))
            return cached;

        var player = runState.Players[0];
        double score;
        if (point.PointType == MapPointType.Unknown)
        {
            var (danger, _) = ScoreUnknownByHook(runState);
            score = danger;
        }
        else
        {
            score = GetBaseDanger(point.PointType);
            double mult = GetDangerMultiplier(player);
            score *= mult;
            ApplyEliteRelicCorrections(player, ref score, ref _dummyReward, point, dangerOnly: true);
        }
        score = Math.Clamp(score, 0, 200);

        if (stateHash != 0)
        {
            if (_dangerCache.Count == 0)
                ModLogger.Info($"ScoringEngine: populating danger cache for {point.PointType} (score={score:F0})");
            _dangerCache[point] = score;
        }
        return score;
    }

    public double CalcRewardScore(MapPoint point, IRunState runState)
    {
        int stateHash = HashRunState(runState);
        if (stateHash != 0 && stateHash == _cachedStateHash && _rewardCache.TryGetValue(point, out var cached))
            return cached;

        var player = runState.Players[0];
        double score;
        if (point.PointType == MapPointType.Unknown)
        {
            var (danger, reward) = ScoreUnknownByHook(runState);
            score = reward;
        }
        else
        {
            score = GetBaseReward(point.PointType);
            score *= GetRewardMultiplier(player);
            double dummyDanger = 0;
            ApplyEliteRelicCorrections(player, ref dummyDanger, ref score, point, dangerOnly: false);
        }
        score = Math.Clamp(score, 0, 200);

        if (stateHash != 0)
            _rewardCache[point] = score;
        return score;
    }

    // Shared mutable ref for elite relic corrections
    private static double _dummyReward;

    private double GetBaseDanger(MapPointType type)
    {
        var key = type.ToString();
        return RouteScoringConfig.Current.BaseScores.TryGetValue(key, out var entry) ? entry.Danger : 0;
    }

    private double GetBaseReward(MapPointType type)
    {
        var key = type.ToString();
        return RouteScoringConfig.Current.BaseScores.TryGetValue(key, out var entry) ? entry.Reward : 0;
    }

    private double GetDangerMultiplier(Player player)
    {
        var cfg = RouteScoringConfig.Current.DynamicModifiers.Danger;
        double multiplier = 1.0;

        double hpRatio = (double)player.Creature.CurrentHp / player.Creature.MaxHp;
        if (hpRatio < cfg.low_hp_threshold)
            multiplier += cfg.low_hp_scale * (1.0 - hpRatio / cfg.low_hp_threshold);

        int totalCards = player.Deck.Cards.Count();
        int blockCards = player.Deck.Cards.Count(c =>
            c.Tags.Contains(CardTag.Defend) || c.GainsBlock);
        double blockRatio = totalCards > 0 ? (double)blockCards / totalCards : 0;
        if (blockRatio < cfg.block_card_ratio_threshold)
            multiplier += cfg.block_card_bonus;

        int potionCount = player.Potions.Count();
        if (potionCount == 0)
            multiplier += cfg.no_potion_bonus;

        return Math.Clamp(multiplier, cfg.min_multiplier, cfg.max_multiplier);
    }

    private double GetRewardMultiplier(Player player)
    {
        var cfg = RouteScoringConfig.Current.DynamicModifiers.Reward;
        double multiplier = 1.0;

        double goldDeficit = Math.Max(0, (cfg.gold_threshold - player.Gold) / cfg.gold_threshold);
        multiplier += cfg.gold_deficit_scale * goldDeficit;

        int relicCount = player.Relics.Count;
        if (relicCount < cfg.relic_count_threshold)
            multiplier += cfg.relic_deficit_scale * (1.0 - (double)relicCount / cfg.relic_count_threshold);

        double hpRatio = (double)player.Creature.CurrentHp / player.Creature.MaxHp;
        if (hpRatio > cfg.full_hp_threshold)
            multiplier -= cfg.full_hp_rest_penalty;

        return Math.Clamp(multiplier, cfg.min_multiplier, cfg.max_multiplier);
    }

    private (double danger, double reward) ScoreUnknownByHook(IRunState runState)
    {
        var player = runState.Players[0];
        double dangerMult = GetDangerMultiplier(player);
        double rewardMult = GetRewardMultiplier(player);
        var (eliteDangerDelta, eliteRewardDelta) = GetEliteRelicDeltas(player);

        var baseTypes = new HashSet<RoomType>
        {
            RoomType.Monster, RoomType.Elite, RoomType.Treasure, RoomType.Shop, RoomType.Event
        };
        var availableTypes = Hook.ModifyUnknownMapPointRoomTypes(runState, baseTypes);

        var weights = RouteScoringConfig.Current.UnknownHookScoring.BaseOddsWeights;
        double totalWeight = 0, expectedDanger = 0, expectedReward = 0;

        foreach (var rt in availableTypes)
        {
            string key = rt.ToString();
            double weight = weights.TryGetValue(key, out var w) ? w : 0;
            // Compute fully-corrected score for each possible type
            double baseDanger = GetBaseDangerForRoomType(rt);
            double baseReward = GetBaseRewardForRoomType(rt);
            double correctedDanger = baseDanger * dangerMult;
            double correctedReward = baseReward * rewardMult;
            if (rt == RoomType.Elite)
            {
                correctedDanger *= 1.0 + eliteDangerDelta;
                correctedReward *= 1.0 + eliteRewardDelta;
            }
            expectedDanger += weight * correctedDanger;
            expectedReward += weight * correctedReward;
            totalWeight += weight;
        }

        if (totalWeight > 0)
        {
            expectedDanger /= totalWeight;
            expectedReward /= totalWeight;
        }

        // Supplementary bonuses (Planisphere heal)
        var bonuses = RouteScoringConfig.Current.UnknownHookScoring.SupplementaryBonuses;
        if (bonuses.TryGetValue("Planisphere", out var planisphere))
        {
            bool hasPlanisphere = player.Relics.Any(r => r.GetType().Name == "Planisphere");
            if (hasPlanisphere)
            {
                double hpPct = (double)player.Creature.CurrentHp / player.Creature.MaxHp;
                expectedReward += planisphere.HealAmount * planisphere.RewardPerHpPct * (1.0 - hpPct);
            }
        }

        return (expectedDanger, expectedReward);
    }

    private (double dangerDelta, double rewardDelta) GetEliteRelicDeltas(Player player)
    {
        var cfg = RouteScoringConfig.Current.EliteRelicCorrections;
        double dangerDelta = 0, rewardDelta = 0;
        foreach (var relic in player.Relics)
        {
            switch (relic)
            {
                case SlingOfCourage:
                    dangerDelta += (cfg.Danger.GetValueOrDefault("SlingOfCourage")?.Multiplier ?? 0.85) - 1.0;
                    break;
                case BoomingConch:
                    dangerDelta += (cfg.Danger.GetValueOrDefault("BoomingConch")?.Multiplier ?? 0.88) - 1.0;
                    break;
                case WarHammer:
                    rewardDelta += (cfg.Reward.GetValueOrDefault("WarHammer")?.Multiplier ?? 1.33) - 1.0;
                    break;
                case WhiteStar:
                    rewardDelta += (cfg.Reward.GetValueOrDefault("WhiteStar")?.Multiplier ?? 1.42) - 1.0;
                    break;
                case BlackStar:
                    rewardDelta += (cfg.Reward.GetValueOrDefault("BlackStar")?.Multiplier ?? 1.50) - 1.0;
                    break;
                case SwordOfStone sos:
                    if (sos.ElitesDefeated < 4)
                        rewardDelta += (cfg.Reward.GetValueOrDefault("SwordOfStone")?.Multiplier ?? 1.25) - 1.0;
                    break;
            }
        }
        return (dangerDelta, rewardDelta);
    }

    private static double LookupDanger(string key) =>
        RouteScoringConfig.Current.BaseScores.TryGetValue(key, out var e) ? e.Danger : 0;
    private static double LookupReward(string key) =>
        RouteScoringConfig.Current.BaseScores.TryGetValue(key, out var e) ? e.Reward : 0;

    private double GetBaseDangerForRoomType(RoomType rt) => rt switch
    {
        RoomType.Monster => LookupDanger("Monster"),
        RoomType.Elite => LookupDanger("Elite"),
        RoomType.Treasure => LookupDanger("Treasure"),
        RoomType.Shop => LookupDanger("Shop"),
        RoomType.Event => LookupDanger("Event"),
        _ => 0,
    };

    private double GetBaseRewardForRoomType(RoomType rt) => rt switch
    {
        RoomType.Monster => LookupReward("Monster"),
        RoomType.Elite => LookupReward("Elite"),
        RoomType.Treasure => LookupReward("Treasure"),
        RoomType.Shop => LookupReward("Shop"),
        RoomType.Event => LookupReward("Event"),
        _ => 0,
    };

    private void ApplyEliteRelicCorrections(Player player,
        ref double dangerScore, ref double rewardScore, MapPoint point,
        bool dangerOnly = false)
    {
        if (point.PointType != MapPointType.Elite) return;

        var cfg = RouteScoringConfig.Current.EliteRelicCorrections;
        var (dangerDelta, rewardDelta) = GetEliteRelicDeltas(player);

        // FurCoat: coord-specific check (not in GetEliteRelicDeltas — cannot predict for Unknown)
        foreach (var relic in player.Relics)
        {
            if (relic is FurCoat fc && fc.GetMarkedCoords()?.Contains(point.coord) == true)
                dangerDelta += (cfg.Danger.GetValueOrDefault("FurCoat")?.Multiplier ?? 0.30) - 1.0;
        }

        dangerScore *= 1.0 + dangerDelta;
        rewardScore *= 1.0 + rewardDelta;

        dangerScore = Math.Max(dangerScore, cfg.Clamp.min_danger);
        rewardScore = Math.Min(rewardScore, cfg.Clamp.max_reward);
    }

    public Dictionary<string, (double danger, double reward)> GetEffectiveTypeScores(IRunState runState)
    {
        var player = runState.Players[0];
        double dangerMult = GetDangerMultiplier(player);
        double rewardMult = GetRewardMultiplier(player);
        var (eliteDangerDelta, eliteRewardDelta) = GetEliteRelicDeltas(player);

        var result = new Dictionary<string, (double, double)>();
        foreach (var (key, entry) in RouteScoringConfig.Current.BaseScores)
        {
            double d = entry.Danger * dangerMult;
            double r = entry.Reward * rewardMult;
            if (key == "Elite")
            {
                d *= 1.0 + eliteDangerDelta;
                r *= 1.0 + eliteRewardDelta;
            }
            result[key] = (Math.Clamp(d, 0, 200), Math.Clamp(r, 0, 200));
        }
        // Unknown expected value — already includes dynamic mult + relic corrections internally
        var (unkDanger, unkReward) = ScoreUnknownByHook(runState);
        result["Unknown"] = (Math.Clamp(unkDanger, 0, 200), Math.Clamp(unkReward, 0, 200));
        return result;
    }

    private static int HashRunState(IRunState runState)
    {
        if (runState.Players.Count == 0) return 0;
        var p = runState.Players[0];
        double hpRatio = (double)p.Creature.CurrentHp / p.Creature.MaxHp;
        return HashCode.Combine(
            (int)(hpRatio * 100),
            p.Gold,
            p.Relics.Count,
            p.Potions.Count(),
            p.Deck.Cards.Count()
        );
    }
}
