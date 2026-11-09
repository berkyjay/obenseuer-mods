using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;

namespace NotificationLog;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log;

    private void Awake()
    {
        Log = Logger;
        ConfigEntry<int> maxEntries = Config.Bind("Log", "MaxEntries", 500,
            new ConfigDescription("How many notifications to keep. The history file is cut to this length when the game starts.", new AcceptableValueRange<int>(50, 5000)));
        LogHistory.Load(Path.Combine(Paths.ConfigPath, MyPluginInfo.PLUGIN_GUID + ".history.tsv"), maxEntries.Value);
        new Harmony(MyPluginInfo.PLUGIN_GUID).PatchAll(typeof(Plugin).Assembly);
        Log.LogInfo($"{MyPluginInfo.PLUGIN_NAME} {MyPluginInfo.PLUGIN_VERSION} loaded");
    }
}

// Every notification starts here. Those restored from a save skip this method and go straight to
// ShowNotification, so loading doesn't log them twice.
[HarmonyPatch(typeof(Notifications), nameof(Notifications.CreateNotification))]
internal static class RecordNotification
{
    private static void Postfix(string type, string text)
    {
        // The callers include money and rent code, which an exception here would cut off.
        try
        {
            LogHistory.Add(type, text);
        }
        catch (System.Exception exception)
        {
            Plugin.Log.LogError(exception);
        }
    }
}

// Changing map reloads through here as well, with fromMenu false. Only a load the player chose
// starts a different timeline, so only that gets a marker.
[HarmonyPatch(typeof(SaveController), nameof(SaveController.LoadGameWithMigration))]
internal static class MarkLoadedSave
{
    private static void Prefix(string saveName, bool fromMenu)
    {
        if (!fromMenu)
        {
            return;
        }
        try
        {
            LogHistory.AddMarker($"Loaded {saveName}");
        }
        catch (System.Exception exception)
        {
            Plugin.Log.LogError(exception);
        }
    }
}

[HarmonyPatch(typeof(UIPageButtons), "Start")]
internal static class AddLogTab
{
    private static void Postfix(UIPageButtons __instance)
    {
        // Without the tab the rest of the menu still works. Finishing Start without it matters more.
        try
        {
            LogTab.AddTo(__instance);
        }
        catch (System.Exception exception)
        {
            Plugin.Log.LogError(exception);
        }
    }
}

// The game gives the spare page no tab index, so controller navigation would stay on the page
// before it, and keys like Drop would act on an inventory that's no longer showing. Share the
// Character page's index: that page also has no navigation of its own.
[HarmonyPatch(typeof(InventoryNavigationHandler), nameof(InventoryNavigationHandler.GetTabIndex))]
internal static class LogPageTabIndex
{
    private static void Postfix(MenuPageNames page, ref int __result)
    {
        if (page == MenuPageNames.Items)
        {
            __result = InventoryNavigationHandler.GetTabIndex(MenuPageNames.Character);
        }
    }
}
