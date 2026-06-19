using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace RoutePlanner;

public static class I18n
{
    public static string CurrentLang { get; private set; } = "zh";
    public static event Action? LanguageChanged;

    private static Dictionary<string, string> _current = new();
    private static readonly Dictionary<string, Dictionary<string, string>> _cache = new();

    private static string ModDir => Path.GetDirectoryName(typeof(I18n).Assembly.Location) ?? "";

    private static string LocaleDir => Path.Combine(ModDir, "locale");

    private static string SettingsPath => Path.Combine(ModDir, "config", "route_planner_settings.json");

    /// <summary>Priority order for route types. Loaded from settings, persisted on change.</summary>
    public static string[] PriorityOrder { get; set; } = Array.Empty<string>();

    /// <summary>Node count constraints. Keyed by MapPointType string, persisted on change.</summary>
    public static Dictionary<string, NodeConstraint>? ConstraintsData { get; set; }

    /// <summary>Auto-draw toggle state. Persisted across restarts.</summary>
    public static bool AutoDraw { get; set; }

    public static void Initialize()
    {
        LoadSettings();
        LoadLocaleIntoCache("zh");
        LoadLocaleIntoCache("en");
        _current = _cache.GetValueOrDefault(CurrentLang, _cache["zh"]);
        ModLogger.Info($"I18n initialized — language={CurrentLang}, strings={_current.Count}");
    }

    public static string Tr(string key)
    {
        if (_current.TryGetValue(key, out var value)) return value;
        if (_cache.TryGetValue("zh", out var zh) && zh.TryGetValue(key, out var zhVal)) return zhVal;
        return key;
    }

    public static void SetLanguage(string lang)
    {
        if (CurrentLang == lang) return;
        CurrentLang = lang;
        _current = _cache.GetValueOrDefault(lang, _cache["zh"]);
        SaveSettings();
        LanguageChanged?.Invoke();
        ModLogger.Info($"Language switched to {lang}");
    }

    private static void LoadLocaleIntoCache(string lang)
    {
        var path = Path.Combine(LocaleDir, $"{lang}.json");
        if (!File.Exists(path))
        {
            ModLogger.Warn($"Locale file not found: {path}");
            return;
        }
        try
        {
            var json = File.ReadAllText(path);
            var doc = JsonSerializer.Deserialize<LocaleFile>(json);
            if (doc?.Strings != null)
                _cache[lang] = doc.Strings;
            ModLogger.Info($"Loaded locale '{lang}' — {_cache[lang].Count} strings");
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Failed to load locale {lang}: {ex.Message}");
        }
    }

    private static void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<ModSettings>(json);
                if (settings?.Language != null)
                    CurrentLang = settings.Language;
                if (settings?.PriorityOrder != null)
                    PriorityOrder = settings.PriorityOrder;
                if (settings?.Constraints != null)
                    ConstraintsData = settings.Constraints;
                AutoDraw = settings?.AutoDraw ?? false;
            }
        }
        catch { /* use default */ }
    }

    public static void SaveSettings()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (dir != null) Directory.CreateDirectory(dir);
            var settings = new ModSettings
            {
                Language = CurrentLang,
                PriorityOrder = PriorityOrder,
                Constraints = ConstraintsData,
                AutoDraw = AutoDraw,
            };
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings));
        }
        catch { /* best effort */ }
    }

    private class LocaleFile
    {
        [JsonPropertyName("strings")]
        public Dictionary<string, string> Strings { get; set; } = new();
    }

    private class ModSettings
    {
        [JsonPropertyName("language")]
        public string Language { get; set; } = "zh";

        [JsonPropertyName("priority_order")]
        public string[]? PriorityOrder { get; set; }

        [JsonPropertyName("constraints")]
        public Dictionary<string, NodeConstraint>? Constraints { get; set; }

        [JsonPropertyName("auto_draw")]
        public bool AutoDraw { get; set; }
    }
}
