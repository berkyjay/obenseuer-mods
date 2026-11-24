using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;

namespace BulkCraft;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log;

    private void Awake()
    {
        Log = Logger;
        new Harmony(MyPluginInfo.PLUGIN_GUID).PatchAll(typeof(Plugin).Assembly);
        Log.LogInfo($"{MyPluginInfo.PLUGIN_NAME} {MyPluginInfo.PLUGIN_VERSION} loaded");
    }
}

// The crafting panel's recipe info re-creates its Craft button every time it shows a recipe,
// which includes after every craft. Add Craft all beside it each time. Only the crafting
// panel's (manufacturing stations); planters and process stations have their own panels.
[HarmonyPatch(typeof(RecipeInfoUI), nameof(RecipeInfoUI.ShowRecipe))]
internal static class AddCraftAllButton
{
    private static void Postfix(RecipeInfoUI __instance, Recipe recipe)
    {
        // An exception here would stop the recipe from showing.
        try
        {
            ManufacturingPanel panel = ManufacturingPanel.instance;
            if (panel != null && panel.manufacturingPanelUI.recipeListUI.recipeInfo == __instance)
            {
                CraftAll.For(__instance).Shown(recipe);
            }
        }
        catch (System.Exception exception)
        {
            Plugin.Log.LogError(exception);
        }
    }
}

// Counts the crafts a Craft all run makes, and how many failed.
[HarmonyPatch(typeof(CraftingBase), "CreateItems")]
internal static class CountCrafts
{
    private static void Postfix(CraftingBase __instance, bool __result)
    {
        if (CraftAll.Running != null && __instance == CraftAll.Running)
        {
            CraftAll.Crafts++;
            if (!__result)
            {
                CraftAll.Failures++;
            }
        }
    }
}

[HarmonyPatch(typeof(LiquidStoragePanelUI), "OnEnable")]
internal static class AddBulkLiquidButtons
{
    private static void Postfix(LiquidStoragePanelUI __instance)
    {
        // An exception here would break the liquid panel.
        try
        {
            BulkLiquid.For(__instance);
        }
        catch (System.Exception exception)
        {
            Plugin.Log.LogError(exception);
        }
    }
}

// Picking another item while a bulk fill runs stops the game's fill of the current stack;
// stop the run too rather than carry on with the next one.
[HarmonyPatch(typeof(ItemInfo), nameof(ItemInfo.Activate))]
internal static class SelectionStopsBulkLiquid
{
    private static void Prefix(ItemData newItemData)
    {
        BulkLiquid active = BulkLiquid.Active;
        if (active == null || BulkLiquid.Selecting || newItemData == null || newItemData.slotController == active.Current)
        {
            return;
        }
        active.Stop("Stopped: another item was selected");
    }
}
