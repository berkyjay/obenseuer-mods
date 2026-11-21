using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SweepTransfer;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    // How far apart, in screen pixels, the cursor's path is checked between frames,
    // so a fast swipe doesn't jump over a slot.
    private const float SampleStep = 8f;

    internal static ManualLogSource Log;
    internal static ConfigEntry<KeyCode> SweepKey;
    internal static ConfigEntry<bool> ToggleMode;

    // True from a left-button press that started a sweep until the button is released.
    // The patches below read it to keep the game's own drag and hold-to-move out of the way.
    internal static bool Sweeping;
    // The item whose drag was blocked during this sweep, so its later drag events are blocked too.
    internal static ItemData SuppressedDrag;

    private bool toggledOn;
    // Set when a move fails (usually the target is full) or the key is let go in hold mode;
    // the rest of the stroke then moves nothing.
    private bool stopped;
    // Which side the first moved item came from: true for the player's inventory.
    // Later items must come from the same side, so items just moved aren't swept back.
    private bool? fromPlayer;
    private Vector2 lastPosition;
    private readonly HashSet<Slot> visited = new HashSet<Slot>();
    private readonly List<RaycastResult> hits = new List<RaycastResult>();
    private PointerEventData pointer;

    private void Awake()
    {
        Log = Logger;
        SweepKey = Config.Bind("Controls", "SweepKey", KeyCode.RightAlt,
            "The key that turns the left mouse button into a sweep. Mouse side buttons are Mouse3 and Mouse4. " +
            "Left Shift and Left Control are taken by the game (move one, move half).");
        ToggleMode = Config.Bind("Controls", "ToggleMode", false,
            "false: hold SweepKey while sweeping. true: press SweepKey once to turn sweeping on and again to turn it off; " +
            "it also turns off when the menu closes.");
        new Harmony(MyPluginInfo.PLUGIN_GUID).PatchAll(typeof(Plugin).Assembly);
        Log.LogInfo($"{MyPluginInfo.PLUGIN_NAME} {MyPluginInfo.PLUGIN_VERSION} loaded, key {SweepKey.Value}, " +
                    (ToggleMode.Value ? "toggle" : "hold"));
    }

    private void Update()
    {
        GameUIController ui = GameUIController.instance;
        if (ui == null || !ui.IsMenuVisible)
        {
            toggledOn = false;
            EndSweep();
            return;
        }

        if (ToggleMode.Value && Input.GetKeyDown(SweepKey.Value))
        {
            toggledOn = !toggledOn;
            Notifications.instance?.CreateNotification("Sweep transfer", toggledOn ? "On" : "Off", silent: true);
        }

        if (!Input.GetMouseButton(0))
        {
            EndSweep();
            return;
        }
        // Any drag from an earlier press has ended by now. SuppressedDrag isn't cleared on
        // release, as the game's end-drag event can arrive after this Update in that frame.
        if (Input.GetMouseButtonDown(0))
        {
            SuppressedDrag = null;
        }

        if (!Sweeping)
        {
            // A sweep starts only on the press itself, never partway through a normal drag.
            if (!Input.GetMouseButtonDown(0) || !Engaged() || !TransferScreen() || EventSystem.current == null)
            {
                return;
            }
            Sweeping = true;
            stopped = false;
            fromPlayer = null;
            visited.Clear();
            pointer = new PointerEventData(EventSystem.current);
            lastPosition = Input.mousePosition;
            Visit(lastPosition);
            return;
        }

        if (!ToggleMode.Value && !Input.GetKey(SweepKey.Value))
        {
            stopped = true;
        }
        if (stopped || !TransferScreen() || EventSystem.current == null)
        {
            return;
        }

        Vector2 current = Input.mousePosition;
        float distance = Vector2.Distance(lastPosition, current);
        int steps = Mathf.Max(1, Mathf.CeilToInt(distance / SampleStep));
        for (int i = 1; i <= steps && !stopped; i++)
        {
            Visit(Vector2.Lerp(lastPosition, current, (float)i / steps));
        }
        lastPosition = current;
    }

    private bool Engaged() => ToggleMode.Value ? toggledOn : Input.GetKey(SweepKey.Value);

    // Only plain moves between the player's inventory and an open container. With a trader,
    // the bottle recycler or a liquid container open, the game's quick-move buys, sells,
    // recycles or pours instead, and with no container open it equips.
    private static bool TransferScreen()
    {
        if (Inventory.instance == null || Inventory.instance.ForeignSlots == null)
        {
            return false;
        }
        if (TradePanel.instance != null && TradePanel.instance.activeTrade != null)
        {
            return false;
        }
        if (BottleRecyclingPanel.instance != null && BottleRecyclingPanel.instance.open)
        {
            return false;
        }
        return LiquidStorage.currentStorage == null;
    }

    private void Visit(Vector2 position)
    {
        pointer.position = position;
        hits.Clear();
        EventSystem.current.RaycastAll(pointer, hits);
        Slot slot = null;
        foreach (RaycastResult hit in hits)
        {
            if (hit.gameObject != null && (slot = hit.gameObject.GetComponentInParent<Slot>()) != null)
            {
                break;
            }
        }
        if (slot == null || !visited.Add(slot))
        {
            return;
        }

        // Inventory and container slots only. Equipment and anything else is left alone.
        SlotController controller = slot.slotcontrol;
        if (controller == null ||
            !(Contains(Inventory.instance.Slots, controller) || Contains(Inventory.instance.ForeignSlots, controller)))
        {
            return;
        }
        if (fromPlayer.HasValue && fromPlayer.Value != slot.playerSlot)
        {
            return;
        }
        ItemData item = slot.GetItemItemData();
        if (item == null || item.amount <= 0)
        {
            return;
        }

        Item moved = item.item;
        if (item.QuickMove(item.amount))
        {
            fromPlayer = slot.playerSlot;
            moved?.PlaySound();
        }
        else
        {
            stopped = true;
        }
    }

    private static bool Contains(SlotController[] slots, SlotController controller)
    {
        if (slots == null)
        {
            return false;
        }
        foreach (SlotController s in slots)
        {
            if (s == controller)
            {
                return true;
            }
        }
        return false;
    }

    private void EndSweep()
    {
        Sweeping = false;
        stopped = false;
        pointer = null;
    }
}

// Pressing on an item normally picks it up once the mouse moves. During a sweep the
// item stays in its slot instead.
[HarmonyPatch(typeof(ItemData), nameof(ItemData.OnBeginDrag), typeof(PointerEventData))]
internal static class BlockBeginDrag
{
    private static bool Prefix(ItemData __instance)
    {
        if (!Plugin.Sweeping)
        {
            return true;
        }
        Plugin.SuppressedDrag = __instance;
        return false;
    }
}

[HarmonyPatch(typeof(ItemData), nameof(ItemData.OnDrag), typeof(PointerEventData))]
internal static class BlockDrag
{
    private static bool Prefix(ItemData __instance) => __instance != Plugin.SuppressedDrag;
}

[HarmonyPatch(typeof(ItemData), nameof(ItemData.OnEndDrag), typeof(PointerEventData))]
internal static class BlockEndDrag
{
    private static bool Prefix(ItemData __instance)
    {
        if (__instance != Plugin.SuppressedDrag)
        {
            return true;
        }
        Plugin.SuppressedDrag = null;
        return false;
    }
}

// The game moves the hovered item after the mouse button is held for a second. During a
// sweep that would move items the sweep skipped, or send moved ones back.
[HarmonyPatch(typeof(HoldController), "Update")]
internal static class PauseHoldMove
{
    private static bool Prefix() => !Plugin.Sweeping;
}
