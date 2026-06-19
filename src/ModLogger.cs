using System;
using System.IO;
using System.Reflection;
using System.Text;
using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace RoutePlanner;

public static class ModLogger
{
    private static string? _logPath;
    private static readonly object _lock = new();

    /// <summary>Mod root directory — derived from DLL location, works for both local and Steam Workshop installs.</summary>
    public static string ModDir => Path.GetDirectoryName(typeof(ModLogger).Assembly.Location) ?? "";

    public static void Init()
    {
        // Use DLL location so it works both from mods/ and workshop/content/.../
        _logPath = Path.Combine(ModDir, "logs", "route_planner.log");

        try
        {
            var dir = Path.GetDirectoryName(_logPath);
            if (dir != null) Directory.CreateDirectory(dir);
            File.AppendAllText(_logPath, "", Encoding.UTF8);
        }
        catch (Exception ex)
        {
            // Fallback: try game's mods directory
            try
            {
                string exeDir = Path.GetDirectoryName(OS.GetExecutablePath()) ?? "";
                _logPath = Path.Combine(exeDir, "mods", "RoutePlanner", "logs", "route_planner.log");
                var dir = Path.GetDirectoryName(_logPath);
                if (dir != null) Directory.CreateDirectory(dir);
            }
            catch
            {
                _logPath = null;
                Log.Error($"[RoutePlanner] Failed to create log file: {ex.Message}");
            }
        }

        Info("ModLogger initialized");
        Info($"Log path: {_logPath ?? "FILE LOGGING DISABLED"}");
    }

    public static void Info(string msg)
    {
        Log.Info($"[RoutePlanner] {msg}");
        WriteFile("INFO", msg);
    }

    public static void Warn(string msg)
    {
        Log.Warn($"[RoutePlanner] {msg}");
        WriteFile("WARN", msg);
    }

    public static void Error(string msg)
    {
        Log.Error($"[RoutePlanner] {msg}");
        WriteFile("ERROR", msg);
    }

    public static void Error(string msg, Exception ex)
    {
        Log.Error($"[RoutePlanner] {msg}: {ex}");
        WriteFile("ERROR", $"{msg}: {ex}");
    }

    private static void WriteFile(string level, string msg)
    {
        try
        {
            var line = $"{DateTime.UtcNow:HH:mm:ss.fff} [{level}] {msg}{System.Environment.NewLine}";
            lock (_lock)
            {
                if (_logPath != null)
                    File.AppendAllText(_logPath, line, Encoding.UTF8);
            }
        }
        catch
        {
            // Never let logging crash the mod
        }
    }
}
