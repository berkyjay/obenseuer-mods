using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace HarvestAllButton;

// The panel's Harvest, Replant and settings buttons, and the job they start. A job works one slot
// each time SecondsPerSlot of game time passes, or one per frame at 0, so it can stop at any point:
// slots already done stay done and the rest are untouched. Like the Electronics bench, a timed job
// needs the panel open and speeds up the clock if it would otherwise take more than a few seconds.
// Nothing about a job is saved; closing the panel or reloading just ends it.
internal sealed class HarvestPanel : MonoBehaviour
{
    private const float Margin = 4f;

    private static readonly int[] TimeSteps = { 0, 5, 10, 15, 20, 30, 60 };

    private enum Job
    {
        None,
        Harvest,
        Replant
    }

    private GrowingPanelUI ui;
    private RectTransform harvestRoot;
    private RectTransform replantRoot;
    private RectTransform choiceRoot;
    private RectTransform timeRoot;
    private WaitingUISimple clock;

    private Job job;
    private Growing station;
    private readonly Queue<GrowingSpot> queue = new Queue<GrowingSpot>();
    private int total;
    private int done;
    private int banked;

    internal static bool FastForwarding { get; private set; }

    internal static HarvestPanel For(GrowingPanelUI ui)
    {
        HarvestPanel panel = ui.GetComponent<HarvestPanel>();
        if (panel == null)
        {
            panel = ui.gameObject.AddComponent<HarvestPanel>();
            panel.Build(ui);
        }
        return panel;
    }

    private void Build(GrowingPanelUI panelUi)
    {
        ui = panelUi;
        ui.harvestButton.SetActive(true);
        Transform column = ui.processSlotsUI.transform;
        Transform root = ui.harvestButton.transform;
        while (root.parent != column)
        {
            root = root.parent;
        }
        harvestRoot = (RectTransform)root;
        replantRoot = CloneButton("Replant button main", PressReplant, null);
        choiceRoot = CloneButton("Replant choice button main", () => CycleChoice(1), () => CycleChoice(-1));
        timeRoot = CloneButton("Time per slot button main", () => CycleTime(1), () => CycleTime(-1));
        foreach (RectTransform button in new[] { harvestRoot, replantRoot, choiceRoot, timeRoot })
        {
            FitLabels(button);
        }

        float height = harvestRoot.sizeDelta.y;
        float width = (((RectTransform)column).sizeDelta.x - 3f * Margin) / 2f;
        Place(harvestRoot, 0, 1, width, height);
        Place(replantRoot, 1, 1, width, height);
        Place(choiceRoot, 0, 0, width, height);
        Place(timeRoot, 1, 0, width, height);

        // The slot views (scrolling grid, and big slots for stations with 3 or fewer) fill the column.
        float reserved = 2f * height + 3f * Margin;
        foreach (Transform child in column)
        {
            if (child is RectTransform view && view.anchorMin == Vector2.zero && view.anchorMax == Vector2.one)
            {
                view.offsetMin = new Vector2(view.offsetMin.x, reserved);
            }
        }
        // Three big slots only just fit the full column height, so tighten them to clear the buttons.
        UnityEngine.UI.VerticalLayoutGroup bigSlots = column.Find("Big Slots/Big Slot List")?.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
        if (bigSlots != null)
        {
            bigSlots.padding = new RectOffset(0, 0, 8, 8);
            bigSlots.spacing = 8f;
        }
    }

    // A copy of the Harvest button, which keeps its look and sounds. The copy mustn't keep its Relay:
    // that's a saved object, and a clone would write a second save entry under the same GUID.
    private RectTransform CloneButton(string name, Action leftClick, Action rightClick)
    {
        GameObject clone = Instantiate(harvestRoot.gameObject, harvestRoot.parent);
        clone.name = name;
        foreach (SavableScript saved in clone.GetComponentsInChildren<SavableScript>(true))
        {
            DestroyImmediate(saved);
        }
        Transform face = clone.transform.Find(ui.harvestButton.name);
        UnityEngine.UI.Button button = face.GetComponent<UnityEngine.UI.Button>();
        button.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
        button.onClick.AddListener(() => leftClick());
        if (rightClick != null)
        {
            face.gameObject.AddComponent<RightClick>().Pressed = rightClick;
        }
        return (RectTransform)clone.transform;
    }

    private static void Place(RectTransform button, int column, int row, float width, float height)
    {
        button.anchorMin = button.anchorMax = button.pivot = Vector2.zero;
        button.sizeDelta = new Vector2(width, height);
        button.anchoredPosition = new Vector2(Margin + column * (width + Margin), Margin + row * (height + Margin));
    }

    // These buttons are half the width the Harvest label was made for.
    private static void FitLabels(RectTransform root)
    {
        foreach (TextMeshProUGUI label in root.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            label.fontSizeMax = label.fontSize;
            label.fontSizeMin = 8f;
            label.enableAutoSizing = true;
        }
    }

    private static void SetLabel(RectTransform root, string text)
    {
        foreach (TextMeshProUGUI label in root.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            label.text = text;
        }
    }

    // Called each time a station's panel opens.
    internal void Enter(Growing manu)
    {
        StopJob();
        bool supported = Farming.Supported(manu);
        ui.harvestButton.SetActive(supported || !manu.ManualHarvest);
        replantRoot.gameObject.SetActive(supported);
        choiceRoot.gameObject.SetActive(supported);
        timeRoot.gameObject.SetActive(supported);
        ShowSettings();
        ShowIdleLabels();
    }

    internal void PressHarvest()
    {
        if (job != Job.None)
        {
            StopJob();
            return;
        }
        Growing manu = GrowingPanel.instance.ActiveManu;
        StartJob(Job.Harvest, manu, manu.GrowingSlots.Where(Farming.IsReady), "Nothing is ready to harvest");
    }

    private void PressReplant()
    {
        if (job != Job.None)
        {
            StopJob();
            return;
        }
        Growing manu = GrowingPanel.instance.ActiveManu;
        StartJob(Job.Replant, manu, manu.GrowingSlots.Where(slot => Farming.CanReplant(manu, slot)), WhyNoReplant(manu));
    }

    private static string WhyNoReplant(Growing manu)
    {
        if (manu.GrowingSlots.All(slot => slot.HasPlant()))
        {
            return "No empty slots to replant";
        }
        return Farming.NeedsCropChoice(manu) ? "Several crops possible: click one in the list" : $"No items to replant with: {Plugin.Replant.Value}";
    }

    private void StartJob(Job kind, Growing manu, IEnumerable<GrowingSpot> slots, string nothingToDo)
    {
        queue.Clear();
        foreach (GrowingSpot slot in slots)
        {
            queue.Enqueue(slot);
        }
        if (queue.Count == 0)
        {
            Tell(nothingToDo);
            return;
        }
        job = kind;
        station = manu;
        total = queue.Count;
        done = 0;
        banked = 0;
        TimeOfDayAzure.SecondsPassed += OnSecondsPassed;

        int seconds = total * Plugin.SecondsPerSlot.Value;
        FastForwarding = seconds > 0 && !PlayerIsWaitingOrSleeping() && TimeOfDayAzure.instance.SetSpeedFactorByProgressTime(seconds) > 1f;
        WaitingUISimple waiting = seconds > 0 ? Clock() : null;
        if (waiting != null)
        {
            ui.manufactureImage.gameObject.SetActive(false);
            waiting.ShowUI();
            waiting.SetEndTime(TimeOfDayAzure.instance.currentTimeAndDay.TotalSeconds + (ulong)seconds);
        }
        ShowProgress();
    }

    // The clock from the Electronics bench, which the growing panel doesn't have.
    private WaitingUISimple Clock()
    {
        if (clock == null && ManufacturingPanel.instance != null)
        {
            GameObject copy = Instantiate(ManufacturingPanel.instance.manufacturingPanelUI.waitingUI.gameObject, ui.manufactureImage.transform.parent);
            copy.name = "Harvest clock";
            RectTransform image = ui.manufactureImage.rectTransform;
            RectTransform rect = (RectTransform)copy.transform;
            rect.anchorMin = image.anchorMin;
            rect.anchorMax = image.anchorMax;
            rect.pivot = image.pivot;
            rect.sizeDelta = image.sizeDelta;
            rect.anchoredPosition = image.anchoredPosition;
            clock = copy.GetComponent<WaitingUISimple>();
        }
        return clock;
    }

    // Runs inside TimeOfDayAzure's update, where an exception would cut off every other listener
    // (plants included), so the work itself happens in Update.
    private void OnSecondsPassed(int seconds)
    {
        banked += seconds;
    }

    private void Update()
    {
        if (job == Job.None)
        {
            return;
        }
        if (GrowingPanel.instance == null || GrowingPanel.instance.ActiveManu != station || PlayerIsWaitingOrSleeping())
        {
            StopJob();
            return;
        }
        try
        {
            int cost = Plugin.SecondsPerSlot.Value;
            // Free slots still go one per frame, so a big farm can't stall a single frame.
            if (cost == 0)
            {
                WorkNextSlot(0);
                return;
            }
            while (job != Job.None && banked >= cost)
            {
                banked -= cost;
                WorkNextSlot(cost);
            }
        }
        catch (Exception exception)
        {
            // A job left running would keep the clock fast until the panel closed.
            Plugin.Log.LogError(exception);
            StopJob("Stopped: something went wrong, see LogOutput.log");
        }
    }

    private void WorkNextSlot(int cost)
    {
        GrowingSpot slot = queue.Dequeue();
        bool full = false;
        bool worked = job == Job.Harvest ? Farming.Harvest(station, slot, out full) : Farming.Replant(station, slot);
        if (worked)
        {
            done++;
            Farming.UpdateViews(station);
        }
        else
        {
            // Nothing happened, so it costs nothing.
            banked += cost;
        }
        if (full)
        {
            Inventory.instance.inventoryPanelUI.InventoryFull();
            StopJob($"Inventory and planter are full: harvested {done} of {total}");
        }
        else if (queue.Count == 0)
        {
            StopJob(job == Job.Replant && done < total ? $"Planted {done} of {total}: ran out of {Plugin.Replant.Value} items" : null);
        }
        else
        {
            ShowProgress();
        }
    }

    private void StopJob(string message = null)
    {
        if (job == Job.None)
        {
            return;
        }
        job = Job.None;
        queue.Clear();
        TimeOfDayAzure.SecondsPassed -= OnSecondsPassed;
        if (FastForwarding && TimeOfDayAzure.instance != null && !PlayerIsWaitingOrSleeping())
        {
            TimeOfDayAzure.instance.SetSpeedFactor(1f);
        }
        FastForwarding = false;
        if (clock != null)
        {
            clock.HideUI();
        }
        ui.manufactureImage.gameObject.SetActive(true);
        ShowIdleLabels();
        if (station != null)
        {
            Farming.FinishJob(station);
        }
        station = null;
        if (message != null)
        {
            Tell(message);
        }
    }

    // Closing the panel, changing scene and leaving for another menu page all land here.
    private void OnDisable()
    {
        StopJob();
    }

    private static bool PlayerIsWaitingOrSleeping()
    {
        return WaitingController.instance != null && WaitingController.instance.waitingType != WaitingController.Type.None;
    }

    private static void Tell(string message)
    {
        Plugin.Log.LogInfo(message);
        Notifications.instance.CreateNotification("Harvest", message);
    }

    private void ShowIdleLabels()
    {
        SetLabel(harvestRoot, "Harvest");
        SetLabel(replantRoot, "Replant");
    }

    private void ShowProgress()
    {
        SetLabel(job == Job.Harvest ? harvestRoot : replantRoot, $"Stop ({done}/{total})");
    }

    private void ShowSettings()
    {
        SetLabel(choiceRoot, $"Use: {Plugin.Replant.Value}");
        SetLabel(timeRoot, Plugin.SecondsPerSlot.Value == 0 ? "Instant" : $"{Plugin.SecondsPerSlot.Value} sec/slot");
    }

    private void CycleChoice(int step)
    {
        if (job == Job.None)
        {
            int count = Enum.GetValues(typeof(ReplantChoice)).Length;
            Plugin.Replant.Value = (ReplantChoice)(((int)Plugin.Replant.Value + step + count) % count);
            ShowSettings();
        }
    }

    // Values set by hand in the config file are kept until one of these buttons changes them.
    private void CycleTime(int step)
    {
        if (job == Job.None)
        {
            int now = Plugin.SecondsPerSlot.Value;
            int? next = step > 0
                ? TimeSteps.Cast<int?>().FirstOrDefault(value => value > now)
                : TimeSteps.Cast<int?>().LastOrDefault(value => value < now);
            Plugin.SecondsPerSlot.Value = next ?? (step > 0 ? TimeSteps.First() : TimeSteps.Last());
            ShowSettings();
        }
    }
}

// Button only answers the left mouse button; the settings buttons step backwards on right click.
internal sealed class RightClick : MonoBehaviour, IPointerClickHandler
{
    internal Action Pressed;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Right)
        {
            Pressed?.Invoke();
        }
    }
}
