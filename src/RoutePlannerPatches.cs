using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

namespace RoutePlanner;

[HarmonyPatch]
public static class RoutePlannerPatches
{
    // Patch SetMap instead of Initialize, since the map data is available at that point
    [HarmonyPatch(typeof(NMapScreen), "SetMap")]
    [HarmonyPostfix]
    public static void OnSetMap(NMapScreen __instance, ActMap map)
    {
        ModLogger.Info($"Harmony: NMapScreen.SetMap called, ActMap={map?.GetType().Name ?? "null"}");
        if (RoutePlannerInstance.Instance == null)
        {
            ModLogger.Warn("Harmony: RoutePlannerInstance.Instance is null, skipping");
            return;
        }

        var runState = __instance.GetType()
            .GetField("_runState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
            .GetValue(__instance) as RunState;

        ModLogger.Info($"Harmony: runState via reflection = {(runState != null ? "found" : "NULL")}");

        if (runState != null)
        {
            __instance.GetTree().CreateTimer(0.0).Timeout += () =>
            {
                ModLogger.Info("Harmony: deferred OnMapScreenReady firing");
                RoutePlannerInstance.Instance.OnMapScreenReady(__instance, runState);
            };
        }
    }

    [HarmonyPatch(typeof(NMapScreen), "_ExitTree")]
    [HarmonyPrefix]
    public static void OnMapScreenExit()
    {
        ModLogger.Info("Harmony: NMapScreen._ExitTree called");
        RoutePlannerInstance.Instance?.OnMapScreenExit();
    }
}
