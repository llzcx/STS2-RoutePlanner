using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace RoutePlanner;

public class ScoringData
{
    [JsonPropertyName("_schema")]
    public string Schema { get; set; } = "1.0";

    [JsonPropertyName("base_scores")]
    public Dictionary<string, ScoreEntry> BaseScores { get; set; } = new();

    [JsonPropertyName("dynamic_modifiers")]
    public DynamicModifiersData DynamicModifiers { get; set; } = new();

    [JsonPropertyName("unknown_hook_scoring")]
    public UnknownHookScoringData UnknownHookScoring { get; set; } = new();

    [JsonPropertyName("elite_relic_corrections")]
    public EliteRelicCorrectionsData EliteRelicCorrections { get; set; } = new();

    [JsonPropertyName("weight_presets")]
    public Dictionary<string, WeightPreset> WeightPresets { get; set; } = new();

    [JsonPropertyName("default_weights")]
    public WeightPreset DefaultWeights { get; set; } = new();
}

public class ScoreEntry
{
    [JsonPropertyName("danger")]
    public double Danger { get; set; }

    [JsonPropertyName("reward")]
    public double Reward { get; set; }
}

public class DynamicModifiersData
{
    [JsonPropertyName("danger")]
    public DangerModifierData Danger { get; set; } = new();

    [JsonPropertyName("reward")]
    public RewardModifierData Reward { get; set; } = new();
}

public class DangerModifierData
{
    public double low_hp_threshold { get; set; } = 0.3;
    public double low_hp_scale { get; set; } = 0.5;
    public double block_card_ratio_threshold { get; set; } = 0.15;
    public double block_card_bonus { get; set; } = 0.3;
    public double no_potion_bonus { get; set; } = 0.2;
    public double min_multiplier { get; set; } = 0.5;
    public double max_multiplier { get; set; } = 2.0;
}

public class RewardModifierData
{
    public double gold_threshold { get; set; } = 150;
    public double gold_deficit_scale { get; set; } = 0.3;
    public double relic_count_threshold { get; set; } = 3;
    public double relic_deficit_scale { get; set; } = 0.3;
    public double full_hp_threshold { get; set; } = 0.9;
    public double full_hp_rest_penalty { get; set; } = 0.2;
    public double min_multiplier { get; set; } = 0.5;
    public double max_multiplier { get; set; } = 2.0;
}

public class UnknownHookScoringData
{
    [JsonPropertyName("base_odds_weights")]
    public Dictionary<string, double> BaseOddsWeights { get; set; } = new();

    [JsonPropertyName("supplementary_bonuses")]
    public Dictionary<string, SupplementaryBonus> SupplementaryBonuses { get; set; } = new();
}

public class SupplementaryBonus
{
    [JsonPropertyName("heal_amount")]
    public double HealAmount { get; set; }

    [JsonPropertyName("reward_per_hp_pct")]
    public double RewardPerHpPct { get; set; } = 1.0;
}

public class EliteRelicCorrectionsData
{
    [JsonPropertyName("danger")]
    public Dictionary<string, RelicAdjustment> Danger { get; set; } = new();

    [JsonPropertyName("reward")]
    public Dictionary<string, RelicAdjustment> Reward { get; set; } = new();

    [JsonPropertyName("clamp")]
    public ClampData Clamp { get; set; } = new();
}

public class RelicAdjustment
{
    [JsonPropertyName("multiplier")]
    public double Multiplier { get; set; } = 1.0;

    [JsonPropertyName("condition")]
    public string? Condition { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}

public class ClampData
{
    public double min_danger { get; set; } = 5;
    public double max_reward { get; set; } = 200;
}

public class WeightPreset
{
    [JsonPropertyName("danger_weight")]
    public double DangerWeight { get; set; } = 0.5;

    [JsonPropertyName("reward_weight")]
    public double RewardWeight { get; set; } = 0.5;
}

public static class RouteScoringConfig
{
    private const string SchemaVersion = "1.0";
    private static ScoringData _current = BuildDefault();
    private static ScoringData _default = BuildDefault();
    private static string _lastChecksum = "";

    public static string ConfigPath => Path.Combine(
        Path.GetDirectoryName(Godot.OS.GetExecutablePath()) ?? "", "mods", "RoutePlanner", "config", "route_planner_scoring.json");

    public static ScoringData Current => _current;

    public static void Initialize()
    {
        _default = BuildDefault();
        _current = LoadWithFallback(ConfigPath) ?? _default;

        ModLogger.Info($"Scoring config initialized — path={ConfigPath}, exists={File.Exists(ConfigPath)}");

        if (File.Exists(ConfigPath))
            _lastChecksum = ComputeMD5(File.ReadAllText(ConfigPath));
    }

    /// <summary>Called periodically to check for config file changes. Returns true if config was reloaded.</summary>
    public static bool PollConfigChange()
    {
        if (!File.Exists(ConfigPath)) return false;
        string newChecksum = ComputeMD5(File.ReadAllText(ConfigPath));
        if (newChecksum == _lastChecksum) return false;

        var loaded = LoadWithFallback(ConfigPath);
        if (loaded != null)
        {
            _current = loaded;
            _lastChecksum = newChecksum;
            return true;
        }
        return false;
    }

    private static ScoringData LoadWithFallback(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                ModLogger.Warn($"Config file not found at {path}, using defaults");
                return _default;
            }
            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<ScoringData>(json);
            ModLogger.Info($"Config loaded from {path}: {data?.BaseScores.Count ?? 0} base scores, {data?.WeightPresets.Count ?? 0} presets");
            return data ?? _default;
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Failed to load scoring config: {ex.Message}", ex);
            return _default;
        }
    }

    private static ScoringData BuildDefault()
    {
        return new ScoringData
        {
            Schema = SchemaVersion,
            BaseScores = new Dictionary<string, ScoreEntry>
            {
                ["Monster"] = new() { Danger = 30, Reward = 15 },
                ["Elite"] = new() { Danger = 100, Reward = 30 },
                ["RestSite"] = new() { Danger = 0, Reward = 35 },
                ["Shop"] = new() { Danger = 0, Reward = 45 },
                ["Treasure"] = new() { Danger = 0, Reward = 50 },
                ["Event"] = new() { Danger = 10, Reward = 45 },
            },
            DynamicModifiers = new DynamicModifiersData(),
            UnknownHookScoring = new UnknownHookScoringData
            {
                BaseOddsWeights = new Dictionary<string, double>
                {
                    ["Monster"] = 0.10, ["Treasure"] = 0.02, ["Shop"] = 0.03, ["Elite"] = 0.05, ["Event"] = 0.80,
                },
            },
            EliteRelicCorrections = new EliteRelicCorrectionsData
            {
                Danger = new Dictionary<string, RelicAdjustment>
                {
                    ["SlingOfCourage"] = new() { Multiplier = 0.85 },
                    ["BoomingConch"] = new() { Multiplier = 0.88 },
                    ["FurCoat"] = new() { Multiplier = 0.30 },
                },
                Reward = new Dictionary<string, RelicAdjustment>
                {
                    ["WarHammer"] = new() { Multiplier = 1.33 },
                    ["WhiteStar"] = new() { Multiplier = 1.42 },
                    ["BlackStar"] = new() { Multiplier = 1.50 },
                    ["SwordOfStone"] = new() { Multiplier = 1.25, Condition = "elites_defeated < 4" },
                },
            },
            WeightPresets = new Dictionary<string, WeightPreset>
            {
                ["conservative"] = new() { DangerWeight = 0.0, RewardWeight = 0.5 },
                ["safe_reward"] = new() { DangerWeight = 0.0, RewardWeight = 1.0 },
                ["balanced"] = new() { DangerWeight = 0.5, RewardWeight = 0.5 },
                ["aggressive"] = new() { DangerWeight = 1.0, RewardWeight = 1.0 },
                ["extreme"] = new() { DangerWeight = 1.0, RewardWeight = 0.0 },
            },
            DefaultWeights = new WeightPreset { DangerWeight = 0.0, RewardWeight = 0.5 },
        };
    }

    private static string ComputeMD5(string text)
    {
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexStringLower(hash);
    }
}
