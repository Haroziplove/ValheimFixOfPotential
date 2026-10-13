using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace SafeRefinement
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.tomas.saferefinement";
        public const string PluginName = "SafeRefinement";
        public const string PluginVersion = "1.0.0";

        internal static Plugin Instance { get; private set; }
        internal static ManualLogSource Log { get; private set; }
        internal static Harmony Harmony { get; private set; }

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            ModConfig.Bind(Config);
            ModConfig.InstallWatcher();

            Harmony = new Harmony(PluginGuid);
            bool patched = DoCraftingPatch.TryApply(Harmony, Logger);
            if (!patched)
            {
                Logger.LogError("SafeRefinement is disabled because InventoryGui.DoCrafting could not be patched.");
                return;
            }

            try
            {
                Harmony.Patch(
                    AccessTools.Method(typeof(ZNet), "Awake"),
                    postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterZNetAwake)));
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Could not hook ZNet.Awake to warn about a dedicated-server install: " + ex.Message);
            }

            Logger.LogInfo("SafeRefinement " + PluginVersion + " patched InventoryGui.DoCrafting. Game " + global::Version.CurrentVersion + ". Install this on every player client. A dedicated server does not run the refinement roll.");
        }

        private void Update()
        {
            ModConfig.Tick();
        }

        private void OnDestroy()
        {
            ModConfig.DisposeWatcher();
        }

        private static void AfterZNetAwake(ZNet __instance)
        {
            if (__instance != null && __instance.IsDedicated())
            {
                Instance.Logger.LogWarning("SafeRefinement is loaded on a dedicated server. Refinement is resolved on each player client, so this copy will not protect anyone. Install the plugin on every client.");
            }
        }
    }
}
