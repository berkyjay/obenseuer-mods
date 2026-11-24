using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;
using UnityEngine;

namespace CheckoutToInventory;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log;
    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<bool> Notify;

    private void Awake()
    {
        Log = Logger;
        Enabled = Config.Bind("General", "Enabled", true,
            "Put goods into your inventory as soon as you pay at a shop checkout.");
        Notify = Config.Bind("General", "Notify", true,
            "Show a notification when some goods didn't fit and were left at the checkout.");
        new Harmony(MyPluginInfo.PLUGIN_GUID).PatchAll(typeof(Plugin).Assembly);
        Log.LogInfo($"{MyPluginInfo.PLUGIN_NAME} {MyPluginInfo.PLUGIN_VERSION} loaded");
    }
}

// MarketShop.Pay is the checkout's "Pay" press. When the money goes through it hands the goods
// over (owner cleared, so taking them isn't theft), packs them into shopping bags if any were
// bought, and calls ResetState, which replaces the private `items` list with a new one. Goods it
// packs into bags are Destroyed, which only takes effect at the end of the frame, so the pickup
// waits a frame and skips anything gone by then.
[HarmonyPatch(typeof(MarketShop), nameof(MarketShop.Pay))]
internal static class PayPatch
{
    private static readonly AccessTools.FieldRef<MarketShop, List<MarketShop.ConveyorItem>> Items =
        AccessTools.FieldRefAccess<MarketShop, List<MarketShop.ConveyorItem>>("items");

    // Filled shopping bags that Pay spawns (see InstantiatePatch).
    internal static readonly List<GameObject> Spawned = new List<GameObject>();
    internal static bool InPay;

    private static void Prefix(MarketShop __instance, out List<MarketShop.ConveyorItem> __state)
    {
        __state = Items(__instance);
        Spawned.Clear();
        InPay = true;
    }

    private static void Postfix(MarketShop __instance, List<MarketShop.ConveyorItem> __state)
    {
        InPay = false;
        if (!Plugin.Enabled.Value || __state == null || ReferenceEquals(Items(__instance), __state))
        {
            // Disabled, or the payment failed (not enough money) and the belt is unchanged.
            Spawned.Clear();
            return;
        }
        var goods = __state.Where(c => c != null && c.gameObject != null).Select(c => c.gameObject)
            .Concat(Spawned.Where(g => g != null)).Distinct().ToList();
        Spawned.Clear();
        __instance.StartCoroutine(Collect(goods));
    }

    private static Exception Finalizer(Exception __exception)
    {
        InPay = false;
        return __exception;
    }

    private static IEnumerator Collect(List<GameObject> goods)
    {
        yield return null;
        int left = 0;
        foreach (GameObject go in goods)
        {
            if (go == null)
            {
                continue;
            }
            CollectibleItem item = go.GetComponentInChildren<CollectibleItem>();
            if (item == null || !item.enabled)
            {
                // Packages (as at Ankea) and anything else that isn't a plain item stay put.
                continue;
            }
            try
            {
                // The game's own pickup: fills the main inventory and leaves any rest on the belt.
                if (!item.PerformPickup())
                {
                    left += Math.Max(item.amount, 0);
                }
            }
            catch (Exception exception)
            {
                Plugin.Log.LogError(exception);
            }
        }
        if (left > 0)
        {
            Plugin.Log.LogInfo($"{left} bought item(s) didn't fit in the inventory");
            if (Plugin.Notify.Value && Notifications.instance != null)
            {
                Notifications.instance.CreateNotification("Inventory full",
                    $"{left} item{(left == 1 ? "" : "s")} left at the checkout");
            }
        }
    }
}

[HarmonyPatch(typeof(ItemOperations), nameof(ItemOperations.InstantiateItem), typeof(GameObject), typeof(Vector3))]
internal static class InstantiatePatch
{
    private static void Postfix(GameObject __result)
    {
        if (PayPatch.InPay && __result != null)
        {
            PayPatch.Spawned.Add(__result);
        }
    }
}
