using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;
using UnityEngine;

namespace TrashBinEmptier;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log;
    internal static ConfigEntry<int> EmptyHour;
    internal static ConfigEntry<bool> Notify;
    internal static HashSet<string> EmptiedFurniture;
    internal static HashSet<string> KeptFurniture;

    private void Awake()
    {
        Log = Logger;
        EmptyHour = Config.Bind("Emptying", "Hour", 6,
            new ConfigDescription("In-game hour (0-23) at which the furniture below is emptied each day.", new AcceptableValueRange<int>(0, 23)));
        EmptiedFurniture = Names(Config.Bind("Emptying", "Furniture", "Trash Bin",
            "Placed furniture, by build-menu name, whose contents are deleted every day. Separate names with commas."));
        Notify = Config.Bind("Emptying", "Notify", true,
            "Show a notification when a bin with something in it is emptied.");
        KeptFurniture = Names(Config.Bind("Keeping", "Furniture", "Green Dumpster",
            "Placed furniture that never empties or refills, even on the stairs or outside, where the game would otherwise turn that on. Separate names with commas."));
        new Harmony(MyPluginInfo.PLUGIN_GUID).PatchAll(typeof(Plugin).Assembly);
        Log.LogInfo($"{MyPluginInfo.PLUGIN_NAME} {MyPluginInfo.PLUGIN_VERSION} loaded: emptying {string.Join(", ", EmptiedFurniture)} at {EmptyHour.Value}:00");
    }

    private void Update()
    {
        try
        {
            BinEmptier.Tick();
        }
        catch (Exception exception)
        {
            Log.LogError(exception);
        }
    }

    private static HashSet<string> Names(ConfigEntry<string> entry) =>
        new HashSet<string>(entry.Value.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0), StringComparer.OrdinalIgnoreCase);
}

internal static class BinEmptier
{
    private const ulong Day = 24 * 60 * 60;

    // Storages loaded from a save, with the in-game time that save was written. Leaving a map
    // saves it first, so this is when the player last left (or saved in) that map.
    private static readonly List<(Storage storage, ulong savedAt, float since)> Loaded = new List<(Storage, ulong, float)>();
    private static readonly HashSet<Storage> WaitingForClose = new HashSet<Storage>();
    private static TimeOfDayAzure clock;
    private static ulong lastChecked;
    private static float nextTick;

    internal static void OnStorageLoaded(Storage storage)
    {
        if (storage.GetComponentInParent<FurniturePlaceable>() != null)
        {
            Loaded.Add((storage, storage.savedTimeAndDay.TotalSeconds, Time.unscaledTime));
        }
    }

    internal static void Tick()
    {
        if (Time.unscaledTime < nextTick)
        {
            return;
        }
        nextTick = Time.unscaledTime + 1f;
        TimeOfDayAzure time = TimeOfDayAzure.instance;
        if (time == null || time.updateTimeDisabled || SaveController.Loading)
        {
            return;
        }
        TimeAndDay current = new TimeAndDay();
        current.SetToCurrentTimeAndDay();
        ulong now = current.TotalSeconds;

        // A new map or a loaded save: the per-bin check below covers the time spent away.
        if (time != clock || now < lastChecked)
        {
            clock = time;
            lastChecked = now;
        }

        for (int i = Loaded.Count - 1; i >= 0; i--)
        {
            var (storage, savedAt, since) = Loaded[i];
            if (Time.unscaledTime - since < 2f)
            {
                continue; // let the map finish loading its clock and furniture first
            }
            Loaded.RemoveAt(i);
            // A stamp of 0 means the storage was never saved; a later one means another timeline.
            if (storage != null && savedAt > 0 && savedAt <= now && Crossed(savedAt, now) && IsEmptied(storage))
            {
                Empty(storage, "while you were away");
            }
        }

        if (Crossed(lastChecked, now))
        {
            foreach (FurniturePlaceable placed in UnityEngine.Object.FindObjectsByType<FurniturePlaceable>(FindObjectsSortMode.None))
            {
                foreach (Storage storage in placed.GetComponentsInChildren<Storage>(true))
                {
                    if (IsEmptied(storage))
                    {
                        Empty(storage, "daily");
                    }
                }
            }
        }
        lastChecked = now;

        foreach (Storage storage in WaitingForClose.ToList())
        {
            if (storage == null)
            {
                WaitingForClose.Remove(storage);
            }
            else if (!IsOpen(storage))
            {
                WaitingForClose.Remove(storage);
                Empty(storage, "after you closed it");
            }
        }
    }

    // True when an emptying hour falls in (from, to].
    private static bool Crossed(ulong from, ulong to)
    {
        if (to <= from)
        {
            return false;
        }
        ulong offset = (ulong)Plugin.EmptyHour.Value * 60 * 60;
        ulong last = to >= offset ? (to - offset) / Day * Day + offset : 0;
        return to >= offset && last > from;
    }

    internal static string Title(Component component)
    {
        FurniturePlaceable placed = component.GetComponentInParent<FurniturePlaceable>();
        return placed != null && placed.furniture != null ? placed.furniture.title : null;
    }

    private static bool IsEmptied(Storage storage)
    {
        string title = Title(storage);
        return title != null && Plugin.EmptiedFurniture.Contains(title);
    }

    private static bool IsOpen(Storage storage) => Storage.active == storage || Storage.currentStorage == storage;

    private static void Empty(Storage storage, string when)
    {
        if (IsOpen(storage))
        {
            WaitingForClose.Add(storage);
            return;
        }
        int stacks = storage.storageSlots.Count(s => s != null && s.itemId != -1 && s.itemAmount > 0);
        if (stacks == 0)
        {
            return;
        }
        storage.Empty();
        string title = Title(storage);
        Plugin.Log.LogInfo($"Emptied {title} at {storage.transform.position} ({stacks} stacks, {when})");
        if (Plugin.Notify.Value && Notifications.instance != null)
        {
            Notifications.instance.CreateNotification($"{title} emptied", stacks == 1 ? "1 stack thrown away" : $"{stacks} stacks thrown away");
        }
    }
}

[HarmonyPatch(typeof(Storage), nameof(Storage.OnLoadingGame))]
internal static class RecordLoadedStorage
{
    private static void Postfix(Storage __instance)
    {
        try
        {
            BinEmptier.OnStorageLoaded(__instance);
        }
        catch (Exception exception)
        {
            Plugin.Log.LogError(exception);
        }
    }
}

// The game turns on refill-and-wipe for some furniture placed on the stairs or outside, but
// doesn't save that, so it stops after the next reload. Bins this mod handles skip it entirely.
[HarmonyPatch(typeof(FurniturePlaceable), nameof(FurniturePlaceable.UpdateStorageStatus))]
internal static class SkipGameWipe
{
    private static bool Prefix(FurniturePlaceable __instance)
    {
        string title = __instance.furniture != null ? __instance.furniture.title : null;
        return title == null || !(Plugin.EmptiedFurniture.Contains(title) || Plugin.KeptFurniture.Contains(title));
    }
}
