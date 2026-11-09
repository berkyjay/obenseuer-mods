using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;

namespace HarvestAllButton;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log;
    internal static ConfigEntry<int> SecondsPerSlot;
    internal static ConfigEntry<ReplantChoice> Replant;

    private void Awake()
    {
        Log = Logger;
        SecondsPerSlot = Config.Bind("Harvest", "SecondsPerSlot", 0,
            new ConfigDescription("In-game seconds to harvest or plant one slot, 0 for instant. Also set from the growing panel.", new AcceptableValueRange<int>(0, 600)));
        Replant = Config.Bind("Harvest", "ReplantChoice", ReplantChoice.Lowest,
            "Which items Replant uses up. Also set from the growing panel.");
        new Harmony(MyPluginInfo.PLUGIN_GUID).PatchAll(typeof(Plugin).Assembly);
        Log.LogInfo($"{MyPluginInfo.PLUGIN_NAME} {MyPluginInfo.PLUGIN_VERSION} loaded");
    }
}

// The growing panel's Harvest button is anchored to the top edge of the plant-slot column, the
// same spot as the Modifiers row (power, light, water, ...), which is drawn over it. On stations
// that show modifiers, such as the Terrarium, the button can't be seen. Move it to the bottom
// of the column, beside the Replant and settings buttons, and shorten the slot area above them to
// make room. The panel also hides the button on stations where plants are picked by hand
// (Growing.ManualHarvest); show it there too. See HarvestPanel.
[HarmonyPatch(typeof(GrowingPanel), nameof(GrowingPanel.EnterManu))]
internal static class ShowHarvestButton
{
    private static void Postfix(GrowingPanel __instance)
    {
        // An exception here would stop every planter from opening.
        try
        {
            HarvestPanel.For(__instance.growingPanelUI).Enter(__instance.ActiveManu);
        }
        catch (System.Exception exception)
        {
            Plugin.Log.LogError(exception);
        }
    }
}

// The game's Harvest uproots everything on the station, ripe or not. On planters the player owns,
// run a timed job over the ripe slots instead. Other stations keep the original.
[HarmonyPatch(typeof(GrowingPanel), nameof(GrowingPanel.PressHarvestButton))]
internal static class HarvestButtonRunsJob
{
    private static bool Prefix(GrowingPanel __instance)
    {
        if (__instance.ActiveManu == null || !Farming.Supported(__instance.ActiveManu))
        {
            return true;
        }
        HarvestPanel.For(__instance.growingPanelUI).PressHarvest();
        return false;
    }
}

// TimeOfDayAzure saves its speed factor and doesn't reset it on load, so a save during a job
// would load with the clock still running fast.
[HarmonyPatch(typeof(TimeOfDayAzure), nameof(TimeOfDayAzure.OnSavingGame))]
internal static class SaveAtNormalSpeed
{
    private static void Prefix(TimeOfDayAzure __instance, out float __state)
    {
        __state = __instance.timeSpeedOverrideFactor;
        if (HarvestPanel.FastForwarding)
        {
            __instance.timeSpeedOverrideFactor = 1f;
        }
    }

    private static void Postfix(TimeOfDayAzure __instance, float __state)
    {
        __instance.timeSpeedOverrideFactor = __state;
    }
}

// The game pads the plant-slot column to 24 slots with grey filler slots. On the growing panel,
// only add filler to finish the last row, so the column doesn't scroll past empty filler.
[HarmonyPatch(typeof(ProcessSlotsUI), "CreateProgressSlots")]
internal static class FewerFillerSlots
{
    internal static int RealSlots = -1;

    private static void Prefix(ProcessSlotsUI __instance, int slotAmount)
    {
        bool growingPanel = GrowingPanel.instance != null && GrowingPanel.instance.growingPanelUI.processSlotsUI == __instance;
        RealSlots = growingPanel ? slotAmount : -1;
    }

    private static void Postfix()
    {
        RealSlots = -1;
    }
}

[HarmonyPatch(typeof(ProcessSlotsUI), "CreateNotInUseProgressSlots")]
internal static class FewerFillerSlotsCount
{
    private static void Prefix(ProcessSlotsUI __instance, ref int slotCount)
    {
        int real = FewerFillerSlots.RealSlots;
        if (real > __instance.maxBigSlots)
        {
            slotCount = (3 - real % 3) % 3;
        }
    }
}
