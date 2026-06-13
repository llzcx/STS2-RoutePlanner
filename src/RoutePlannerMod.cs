using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace RoutePlanner;

[ModInitializer("Initialize")]
public static class RoutePlannerMod
{
    public static void Initialize()
    {
        ModLogger.Init();
        ModLogger.Info("RoutePlanner mod initializing...");

        RoutePlannerInstance.Create();
        RouteScoringConfig.Initialize();

        var harmony = new Harmony("route_planner");
        harmony.PatchAll(typeof(RoutePlannerPatches).Assembly);

        ModLogger.Info("RoutePlanner mod initialized successfully");
    }
}
