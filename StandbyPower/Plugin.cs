using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;
using UnityEngine;

namespace StandbyPower;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log;
    internal static ConfigEntry<float> StandbyPercent;

    private void Awake()
    {
        Log = Logger;
        StandbyPercent = Config.Bind("Standby", "Percent", 5f,
            new ConfigDescription(
                "Power an electric crafting station draws while it is switched on but not crafting, as a percentage of its full power. " +
                "Stations the game already gives a standby value keep the game's value.",
                new AcceptableValueRange<float>(0f, 100f)));
        StandbyPercent.SettingChanged += (_, _) => Standby.ReconcileAll();
        new Harmony(MyPluginInfo.PLUGIN_GUID).PatchAll(typeof(Plugin).Assembly);
        Log.LogInfo($"{MyPluginInfo.PLUGIN_NAME} {MyPluginInfo.PLUGIN_VERSION} loaded: idle stations draw {StandbyPercent.Value}% of full power");
    }

    private void Update()
    {
        try
        {
            Standby.Tick();
        }
        catch (Exception exception)
        {
            Log.LogError(exception);
        }
    }
}

// The game already supports a standby draw: a station whose prefab sets PowerConsumptionIdle
// switches PowerSource.overridePowerConsumption on (draw = powerConsumptionOverride) when it has
// nothing in progress, and Craft() switches it off again. Most electric stations leave
// PowerConsumptionIdle at -1, so they draw full power whenever they're on.
//
// This mod drives the same override switch for those stations. It never writes
// PowerConsumptionIdle, which the game saves, so changing the percentage or removing the mod
// isn't overridden by values stored in the save.
internal static class Standby
{
    private static readonly AccessTools.FieldRef<manufacturing, List<CraftInProgress>> Crafts =
        AccessTools.FieldRefAccess<manufacturing, List<CraftInProgress>>("craftsInProgresses");

    private static readonly HashSet<manufacturing> Stations = new HashSet<manufacturing>();
    private static readonly List<manufacturing> Dead = new List<manufacturing>();
    private static float nextTick;

    internal static void Register(manufacturing station)
    {
        Stations.Add(station);
        Reconcile(station);
    }

    // Saves are loaded after Start and overwrite the override fields, so a slow sweep
    // catches anything the patches below didn't.
    internal static void Tick()
    {
        if (Time.unscaledTime < nextTick)
        {
            return;
        }
        nextTick = Time.unscaledTime + 1f;
        ReconcileAll();
    }

    internal static void ReconcileAll()
    {
        foreach (manufacturing station in Stations)
        {
            if (station == null)
            {
                Dead.Add(station);
                continue;
            }
            Reconcile(station);
        }
        foreach (manufacturing station in Dead)
        {
            Stations.Remove(station);
        }
        Dead.Clear();
    }

    internal static bool Handles(manufacturing station)
    {
        PowerSource power = station._Power;
        return station.PowerConsumptionIdle == -1
            && power != null
            && power.Type == PowerSource.PowerSourceType.Electricity
            && !power.RequiresFuel
            && power.powerConsumption > 0;
    }

    internal static int StandbyWatts(manufacturing station) =>
        Mathf.RoundToInt(station._Power.powerConsumption * Plugin.StandbyPercent.Value / 100f);

    internal static void Reconcile(manufacturing station)
    {
        if (!Handles(station))
        {
            return;
        }
        PowerSource power = station._Power;
        List<CraftInProgress> crafts = Crafts(station);
        bool idle = crafts == null || crafts.Count == 0;
        if (!idle)
        {
            // Craft() turns the override off itself; this covers a save that stored it on.
            power.overridePowerConsumption = false;
            return;
        }
        int standby = StandbyWatts(station);
        if (power.overridePowerConsumption && power.powerConsumptionOverride == standby)
        {
            return;
        }
        power.powerConsumptionOverride = standby;
        if (power.overridePowerConsumption)
        {
            // The setter only updates the grid when the flag changes.
            power.PowerGrid?.UpdateGridStatus();
        }
        else
        {
            power.overridePowerConsumption = true;
        }
    }
}

[HarmonyPatch(typeof(manufacturing), "Start")]
internal static class ManufacturingStartPatch
{
    private static void Postfix(manufacturing __instance)
    {
        try
        {
            Standby.Register(__instance);
        }
        catch (Exception exception)
        {
            Plugin.Log.LogError(exception);
        }
    }
}

// Drop to standby the moment the last craft finishes, not up to a second later
// (a second of real time is many in-game minutes while sleeping).
[HarmonyPatch(typeof(manufacturing), "OnCraftDone")]
internal static class ManufacturingCraftDonePatch
{
    private static void Postfix(manufacturing __instance)
    {
        try
        {
            Standby.Reconcile(__instance);
        }
        catch (Exception exception)
        {
            Plugin.Log.LogError(exception);
        }
    }
}

// The game only checks for spare grid capacity when starting a craft on stations that have
// a standby value. Do the same check for the stations this mod puts on standby.
[HarmonyPatch(typeof(CraftingBase), nameof(CraftingBase.HasPowerForCrafting))]
internal static class HasPowerForCraftingPatch
{
    private static void Postfix(CraftingBase __instance, ref bool __result)
    {
        try
        {
            if (!__result || __instance is not manufacturing station || !Standby.Handles(station))
            {
                return;
            }
            PowerSource power = station._Power;
            if (power.PowerGrid == null || !power.overridePowerConsumption || power.PowerGrid.Unlimited)
            {
                return;
            }
            __result = power.powerConsumption - power.powerConsumptionOverride <= power.PowerGrid.FreeCapacity;
        }
        catch (Exception exception)
        {
            Plugin.Log.LogError(exception);
        }
    }
}
