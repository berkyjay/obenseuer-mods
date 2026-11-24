using System.Collections.Generic;
using OS.Items;
using UnityEngine;

namespace BulkCraft;

// Bulk Fill and Bulk Unfill on the liquid storage panel (tanks, barrels, taps). They work on
// every stack in the player's inventory of the same container type as the selected one: empty
// and filled bottles of one kind count as one type. Each stack is selected in turn and the game's
// own Fill or Empty is pressed for it, so liquid moves at the game's speed and theft, quality and
// spoilage mixing work as by hand. A run stops when no stack is left that the game would fill or
// empty (tank empty or full, a different liquid), when another item is selected, or when the
// panel closes. Nothing about a run is saved.
internal sealed class BulkLiquid : MonoBehaviour
{
    private const float RowGap = 6f;

    private enum Mode
    {
        None,
        Fill,
        Unfill
    }

    // The run in progress, if any, read by the ItemInfo patches.
    internal static BulkLiquid Active;
    // True while this mod changes the selected item, so the patches don't take it for the player.
    internal static bool Selecting;

    private LiquidStoragePanelUI panel;
    private RectTransform fillButton;
    private RectTransform unfillButton;

    private Mode mode;
    private int family;
    private readonly HashSet<SlotController> tried = new HashSet<SlotController>();
    internal SlotController Current;
    private int started;
    private int total;
    private LiquidStorage.Result lastRefusal;

    internal static BulkLiquid For(LiquidStoragePanelUI panel)
    {
        BulkLiquid bulk = panel.GetComponent<BulkLiquid>();
        if (bulk == null)
        {
            bulk = panel.gameObject.AddComponent<BulkLiquid>();
            bulk.panel = panel;
            bulk.fillButton = Ui.CloneButton(panel.FillButtonGO, "Bulk Fill Button", () => bulk.Press(Mode.Fill));
            bulk.unfillButton = Ui.CloneButton(panel.EmptyButtonGO, "Bulk Unfill Button", () => bulk.Press(Mode.Unfill));
            bulk.LateUpdate();
        }
        return bulk;
    }

    private void Press(Mode pressed)
    {
        if (mode != Mode.None)
        {
            Stop("Stopped");
            return;
        }
        ItemData selected = ItemInfo.instance != null ? ItemInfo.instance.ItemData : null;
        if (selected == null || selected.slotController == null || LiquidStorage.currentStorage == null)
        {
            return;
        }
        family = Family(selected.slotController.itemStack);
        if (family <= 0)
        {
            return;
        }
        mode = pressed;
        tried.Clear();
        Current = null;
        started = 0;
        lastRefusal = LiquidStorage.Result.OK;
        total = Candidates().Count;
        if (total == 0)
        {
            Stop(null);
            return;
        }
        Active = this;
        // The first stack starts on the next Update, after this click has finished.
    }

    private void Update()
    {
        if (mode == Mode.None)
        {
            return;
        }
        try
        {
            Step();
        }
        catch (System.Exception exception)
        {
            Plugin.Log.LogError(exception);
            Stop("Stopped: something went wrong, see LogOutput.log");
        }
    }

    private void Step()
    {
        if (LiquidStorage.currentStorage == null || Inventory.instance == null || ItemInfo.instance == null)
        {
            Stop("Stopped: the container was closed");
            return;
        }
        if (LiquidStorage.CurrentOutputStorage.fillingItem || LiquidStorage.CurrentInputStorage.unfillingItem)
        {
            return;
        }
        SlotController next = null;
        foreach (SlotController slot in Candidates())
        {
            if (!tried.Contains(slot))
            {
                next = slot;
                break;
            }
        }
        if (next == null)
        {
            Stop(null);
            return;
        }
        tried.Add(next);
        ItemData data = next.slot != null ? next.slot.GetItemItemData() : null;
        if (data == null)
        {
            // Not shown in the inventory panel (a closed backpack), so it can't be selected.
            return;
        }
        Current = next;
        Selecting = true;
        try
        {
            ItemInfo.instance.Activate(data);
        }
        finally
        {
            Selecting = false;
        }
        if (mode == Mode.Fill)
        {
            panel.PressFill();
        }
        else
        {
            panel.PressEmpty();
        }
        if (LiquidStorage.CurrentOutputStorage.fillingItem || LiquidStorage.CurrentInputStorage.unfillingItem)
        {
            started++;
        }
    }

    // The player's stacks of the chosen container type that the game would fill or empty now.
    private List<SlotController> Candidates()
    {
        List<SlotController> found = new List<SlotController>();
        foreach (SlotController slot in PlayerSlots())
        {
            if (slot == null || slot.itemStack == null || slot.itemStack.IsEmpty() || Family(slot.itemStack) != family)
            {
                continue;
            }
            LiquidStorage.Result result = mode == Mode.Fill
                ? LiquidStorage.CurrentOutputStorage.ItemCanBeFilled(ref slot.itemStack)
                : LiquidStorage.CurrentInputStorage.ItemCanBeUnfilled(ref slot.itemStack);
            if (result == LiquidStorage.Result.OK)
            {
                found.Add(slot);
            }
            else if (result != LiquidStorage.Result.FullItem && result != LiquidStorage.Result.EmptyItem)
            {
                lastRefusal = result;
            }
        }
        return found;
    }

    private static SlotController[] PlayerSlots()
    {
        try
        {
            return Inventory.instance.AllSlots(ignoreBackpack: false, ignoreCharacterSlots: true);
        }
        catch (System.Exception)
        {
            return Inventory.instance.Slots ?? new SlotController[0];
        }
    }

    // One number for a kind of container, the same whether it's empty or holds a liquid: the game
    // swaps an empty bottle's item for a filled one when it's filled, and back when it's emptied.
    private static int Family(ItemStack stack)
    {
        if (stack.GetMetaOfType<ItemEmptyLiquidContainerData>() != null)
        {
            return stack.itemId;
        }
        ItemLiquidData liquid = stack.GetMetaOfType<ItemLiquidData>();
        if (liquid == null)
        {
            return 0;
        }
        if (liquid.emptyContainerItem != null && liquid.emptyContainerItem.ID > 0)
        {
            return liquid.emptyContainerItem.ID;
        }
        return stack.itemId;
    }

    internal void Stop(string reason)
    {
        if (mode == Mode.None)
        {
            return;
        }
        Mode was = mode;
        mode = Mode.None;
        Active = null;
        Current = null;
        string verb = was == Mode.Fill ? "Filled" : "Emptied";
        string done = $"{verb} {started} of {total} stack{(total == 1 ? "" : "s")}";
        if (reason == null && started < total)
        {
            reason = lastRefusal switch
            {
                LiquidStorage.Result.Empty => "The container ran dry",
                LiquidStorage.Result.Full => "The container is full",
                LiquidStorage.Result.DifferentLiquid => "Some hold a different liquid",
                _ => null
            };
        }
        Ui.Tell(was == Mode.Fill ? "Bulk fill" : "Bulk unfill", reason == null ? done : $"{reason}. {done}");
    }

    private void OnDisable()
    {
        Stop("Stopped: the container was closed");
        Ui.SetActive(fillButton, false);
        Ui.SetActive(unfillButton, false);
    }

    // Each bulk button sits under the game's button of the same kind, and shows when that button
    // is enabled for the selected item (or while its own run is going).
    private void LateUpdate()
    {
        if (panel == null)
        {
            return;
        }
        Place(fillButton, panel.FillButtonGO, panel.FillButtonDisabledGO);
        Place(unfillButton, panel.EmptyButtonGO, panel.EmptyButtonDisabledGO);
        bool idle = mode == Mode.None;
        Ui.SetActive(fillButton, mode == Mode.Fill || (idle && panel.FillButtonGO.activeSelf));
        Ui.SetActive(unfillButton, mode == Mode.Unfill || (idle && panel.EmptyButtonGO.activeSelf));
        string progress = $"Stop ({started}/{total})";
        Ui.SetLabel(fillButton, mode == Mode.Fill ? progress : "Bulk Fill");
        Ui.SetLabel(unfillButton, mode == Mode.Unfill ? progress : "Bulk Unfill");
    }

    private static void Place(RectTransform bulk, GameObject enabled, GameObject disabled)
    {
        GameObject source = enabled.activeSelf ? enabled : disabled.activeSelf ? disabled : null;
        if (bulk == null || source == null)
        {
            return;
        }
        RectTransform rect = (RectTransform)source.transform;
        bulk.anchorMin = rect.anchorMin;
        bulk.anchorMax = rect.anchorMax;
        bulk.pivot = rect.pivot;
        bulk.sizeDelta = rect.sizeDelta;
        bulk.anchoredPosition = rect.anchoredPosition - new Vector2(0f, rect.rect.height + RowGap);
    }
}
