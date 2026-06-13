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
        double score = GetBaseDanger(point.PointType);
        double mult = GetDangerMultiplier(player);
        score *= mult;
        ApplyEliteRelicCorrections(player, ref score, ref _dummyReward, point, dangerOnly: true);
        score = Math.Clamp(score, 0, 200);

        if (stateHash != 0)
        {
            if (_dangerCache.Count == 0)
                ModLogger.Info($"ScoringEngine: populating danger cache for {point.PointType} (mult={mult:F2}, score={score:F0})");
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
            expectedDanger += weight * GetBaseDangerForRoomType(rt);
            expectedReward += weight * GetBaseRewardForRoomType(rt);
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
            var player = runState.Players[0];
            bool hasPlanisphere = player.Relics.Any(r => r.GetType().Name == "Planisphere");
            if (hasPlanisphere)
            {
                double hpPct = (double)player.Creature.CurrentHp / player.Creature.MaxHp;
                expectedReward += planisphere.HealAmount * planisphere.RewardPerHpPct * (1.0 - hpPct);
            }
        }

        return (expectedDanger, expectedReward);
    }

    private double GetBaseDangerForRoomType(RoomType rt) => rt switch
    {
        RoomType.Monster => RouteScoringConfig.Current.BaseScores["Monster"].Danger,
        RoomType.Elite => RouteScoringConfig.Current.BaseScores["Elite"].Danger,
        RoomType.Treasure => RouteScoringConfig.Current.BaseScores["Treasure"].Danger,
        RoomType.Shop => RouteScoringConfig.Current.BaseScores["Shop"].Danger,
        RoomType.Event => 10, // Events are generally low-risk
        _ => 0,
    };

    private double GetBaseRewardForRoomType(RoomType rt) => rt switch
    {
        RoomType.Monster => RouteScoringConfig.Current.BaseScores["Monster"].Reward,
        RoomType.Elite => RouteScoringConfig.Current.BaseScores["Elite"].Reward,
        RoomType.Treasure => RouteScoringConfig.Current.BaseScores["Treasure"].Reward,
        RoomType.Shop => RouteScoringConfig.Current.BaseScores["Shop"].Reward,
        RoomType.Event => 35, // Events vary but average decent
        _ => 0,
    };

    private void ApplyEliteRelicCorrections(Player player,
        ref double dangerScore, ref double rewardScore, MapPoint point,
        bool dangerOnly = false)
    {
        var cfg = RouteScoringConfig.Current.EliteRelicCorrections;
        bool isElite = point.PointType == MapPointType.Elite;

        double dangerDelta = 0;
        double rewardDelta = 0;

        foreach (var relic in player.Relics)
        {
            switch (relic)
            {
                case SlingOfCourage:
                    if (isElite) dangerDelta += (cfg.Danger.GetValueOrDefault("SlingOfCourage")?.Multiplier ?? 0.85) - 1.0;
                    break;
                case BoomingConch:
                    if (isElite) dangerDelta += (cfg.Danger.GetValueOrDefault("BoomingConch")?.Multiplier ?? 0.88) - 1.0;
                    break;
                case FurCoat fc:
                    if (isElite && fc.GetMarkedCoords()?.Contains(point.coord) == true)
                        dangerDelta += (cfg.Danger.GetValueOrDefault("FurCoat")?.Multiplier ?? 0.30) - 1.0;
                    break;
                case WarHammer:
                    if (isElite) rewardDelta += (cfg.Reward.GetValueOrDefault("WarHammer")?.Multiplier ?? 1.33) - 1.0;
                    break;
                case WhiteStar:
                    if (isElite) rewardDelta += (cfg.Reward.GetValueOrDefault("WhiteStar")?.Multiplier ?? 1.42) - 1.0;
                    break;
                case BlackStar:
                    if (isElite) rewardDelta += (cfg.Reward.GetValueOrDefault("BlackStar")?.Multiplier ?? 1.50) - 1.0;
                    break;
                case SwordOfStone sos:
                    if (isElite && sos.ElitesDefeated < 4)
                        rewardDelta += (cfg.Reward.GetValueOrDefault("SwordOfStone")?.Multiplier ?? 1.25) - 1.0;
                    break;
            }
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

        var result = new Dictionary<string, (double, double)>();
        foreach (var (key, entry) in RouteScoringConfig.Current.BaseScores)
        {
            if (key == "Unassigned") continue;
            double d, r;
            if (key == "Unknown")
            {
                (d, r) = ScoreUnknownByHook(runState);
            }
            else
            {
                d = entry.Danger * dangerMult;
                r = entry.Reward * rewardMult;
            }
            result[key] = (Math.Clamp(d, 0, 200), Math.Clamp(r, 0, 200));
        }
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
