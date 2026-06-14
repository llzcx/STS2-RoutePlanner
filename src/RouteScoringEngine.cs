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

    // Unknown node odds weights — source-controlled, not user-configurable
    private static readonly Dictionary<string, double> UnknownOddsWeights = new()
    {
        ["Monster"] = 0.10, ["Treasure"] = 0.02, ["Shop"] = 0.03, ["Elite"] = 0.05, ["Event"] = 0.80,
    };

    // Planisphere unknown-node heal bonus — source-controlled
    private const double PlanisphereHealAmount = 15.0;
    private const double PlanisphereRewardPerHpPct = 1.0;

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
            _dangerCache[point] = score;
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
            score *= GetRewardMultiplier(point.PointType.ToString(), player);
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

        if (cfg.TryGetValue("LowHp", out var lowHp))
        {
            double hpRatio = (double)player.Creature.CurrentHp / player.Creature.MaxHp;
            double threshold = lowHp.Threshold ?? 0.3;
            if (hpRatio < threshold)
                multiplier += (lowHp.Multiplier - 1.0) * (1.0 - hpRatio / threshold);
        }

        if (cfg.TryGetValue("BlockDeficit", out var blockDeficit))
        {
            int totalCards = player.Deck.Cards.Count();
            int blockCards = player.Deck.Cards.Count(c =>
                c.Tags.Contains(CardTag.Defend) || c.GainsBlock);
            double blockRatio = totalCards > 0 ? (double)blockCards / totalCards : 0;
            double threshold = blockDeficit.Threshold ?? 0.15;
            if (blockRatio < threshold)
                multiplier += blockDeficit.Multiplier - 1.0;
        }

        if (cfg.TryGetValue("NoPotion", out var noPotion))
        {
            if (player.Potions.Count() == 0)
                multiplier += noPotion.Multiplier - 1.0;
        }

        return multiplier;
    }

    private double GetRewardMultiplier(string typeKey, Player player)
    {
        var cfg = RouteScoringConfig.Current.DynamicModifiers.Reward;
        double multiplier = 1.0;

        switch (typeKey)
        {
            case "Shop":
            {
                if (cfg.TryGetValue("GoldDeficit", out var gold))
                {
                    double threshold = gold.Threshold ?? 150;
                    double deficit = Math.Max(0, (threshold - player.Gold) / threshold);
                    multiplier += (gold.Multiplier - 1.0) * deficit;
                }
                break;
            }
            case "Treasure":
            case "Event":
            {
                if (cfg.TryGetValue("RelicDeficit", out var relic))
                {
                    double threshold = relic.Threshold ?? 3;
                    int relicCount = player.Relics.Count;
                    if (relicCount < threshold)
                        multiplier += (relic.Multiplier - 1.0) * (1.0 - (double)relicCount / threshold);
                }
                break;
            }
            case "RestSite":
            {
                if (cfg.TryGetValue("FullHp", out var fullHp))
                {
                    double threshold = fullHp.Threshold ?? 0.9;
                    double hpRatio = (double)player.Creature.CurrentHp / player.Creature.MaxHp;
                    if (hpRatio > threshold)
                        multiplier += fullHp.Multiplier - 1.0;
                }
                break;
            }
        }
        return multiplier;
    }

    private (double danger, double reward) ScoreUnknownByHook(IRunState runState)
    {
        var player = runState.Players[0];
        double dangerMult = GetDangerMultiplier(player);
        var (eliteDangerDelta, eliteRewardDelta) = GetEliteRelicDeltas(player);

        var baseTypes = new HashSet<RoomType>
        {
            RoomType.Monster, RoomType.Elite, RoomType.Treasure, RoomType.Shop, RoomType.Event
        };
        var availableTypes = Hook.ModifyUnknownMapPointRoomTypes(runState, baseTypes);

        double totalWeight = 0, expectedDanger = 0, expectedReward = 0;

        foreach (var rt in availableTypes)
        {
            string key = rt.ToString();
            double weight = UnknownOddsWeights.TryGetValue(key, out var w) ? w : 0;
            double baseDanger = GetBaseDangerForRoomType(rt);
            double baseReward = GetBaseRewardForRoomType(rt);
            double correctedDanger = baseDanger * dangerMult;
            double correctedReward = baseReward * GetRewardMultiplier(key, player);
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

        // Supplementary bonuses (Planisphere heal) — source-controlled
        if (player.Relics.Any(r => r.GetType().Name == "Planisphere"))
        {
            double hpPct = (double)player.Creature.CurrentHp / player.Creature.MaxHp;
            expectedReward += PlanisphereHealAmount * PlanisphereRewardPerHpPct * (1.0 - hpPct);
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
                    dangerDelta += (cfg.Danger.GetValueOrDefault("SlingOfCourage")?.Multiplier ?? 0.90) - 1.0;
                    break;
                case BoomingConch:
                    dangerDelta += (cfg.Danger.GetValueOrDefault("BoomingConch")?.Multiplier ?? 0.90) - 1.0;
                    break;
                case WarHammer:
                    rewardDelta += (cfg.Reward.GetValueOrDefault("WarHammer")?.Multiplier ?? 1.10) - 1.0;
                    break;
                case WhiteStar:
                    rewardDelta += (cfg.Reward.GetValueOrDefault("WhiteStar")?.Multiplier ?? 1.10) - 1.0;
                    break;
                case BlackStar:
                    rewardDelta += (cfg.Reward.GetValueOrDefault("BlackStar")?.Multiplier ?? 1.10) - 1.0;
                    break;
                case SwordOfStone sos:
                    if (sos.ElitesDefeated < 4)
                        rewardDelta += (cfg.Reward.GetValueOrDefault("SwordOfStone")?.Multiplier ?? 1.05) - 1.0;
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
                dangerDelta += (cfg.Danger.GetValueOrDefault("FurCoat")?.Multiplier ?? 0.90) - 1.0;
        }

        dangerScore *= 1.0 + dangerDelta;
        rewardScore *= 1.0 + rewardDelta;
    }

    public Dictionary<string, (double danger, double reward)> GetEffectiveTypeScores(IRunState runState)
    {
        var player = runState.Players[0];
        double dangerMult = GetDangerMultiplier(player);
        var (eliteDangerDelta, eliteRewardDelta) = GetEliteRelicDeltas(player);

        var result = new Dictionary<string, (double, double)>();
        foreach (var (key, entry) in RouteScoringConfig.Current.BaseScores)
        {
            double d = entry.Danger * dangerMult;
            double r = entry.Reward * GetRewardMultiplier(key, player);
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
