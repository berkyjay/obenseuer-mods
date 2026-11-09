using System.Linq;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NotificationLog;

// The Log tab. The game ships a seventh menu page, Items, that it never uses: the page and its
// button are wired together but the button is hidden and the page empty, so this takes it over. The
// tab row is a layout group and makes room by itself. The scroll view and text are copies from
// the Tasks page, which keeps their look. Each scene has its own copy of the menu, so this runs per
// scene and catches up from LogHistory, newest first.
internal sealed class LogTab : MonoBehaviour
{
    private const string TaskViewPath = "Task List/Task List Padding";
    private const string TaskTextPath = "Task Info/Task Info Padding/UI Scroll View/Viewport/Task Info Panel/Description";
    private const string ButtonLabelName = "Items Panel Button Text";
    private const float ScrollSpeed = 600f;
    private const float DeadZone = 0.2f;

    private static readonly AccessTools.FieldRef<UIPageButtons, MenuPage[]> Pages =
        AccessTools.FieldRefAccess<UIPageButtons, MenuPage[]>("MenuPages");

    private static readonly AccessTools.FieldRef<UIPageButtons, InventoryNavigationHandler> Navigation =
        AccessTools.FieldRefAccess<UIPageButtons, InventoryNavigationHandler>("inventoryNavigationHandler");

    private static readonly FieldInfo Tabs = AccessTools.Field(typeof(InventoryNavigationHandler), "_tabs");

    private ScrollRect scroll;
    private RectTransform content;
    private TextMeshProUGUI rowTemplate;
    private int shown;

    internal static void AddTo(UIPageButtons buttons)
    {
        MenuPage[] pages = Pages(buttons);
        MenuPage page = pages.First(candidate => candidate.Name == MenuPageNames.Items);
        Transform tasks = pages.First(candidate => candidate.Name == MenuPageNames.Tasks).Panel.transform;

        // Nothing in the copy may wake before its task rows are gone. The menu can't be on this page
        // yet, because the tab doesn't exist.
        page.Panel.SetActive(false);
        page.Panel.AddComponent<LogTab>().Build(tasks);
        page.Button.transform.Find(ButtonLabelName).GetComponent<TextMeshProUGUI>().text = "Log";
        page.Button.transform.parent.gameObject.SetActive(true);

        // The switch-tab keys walk this list, which leaves the spare page out.
        InventoryNavigationHandler navigation = Navigation(buttons);
        if (navigation != null)
        {
            string[] names = (string[])Tabs.GetValue(navigation);
            Tabs.SetValue(navigation, names.Append(nameof(MenuPageNames.Items)).ToArray());
        }
    }

    private void Build(Transform tasks)
    {
        rowTemplate = tasks.Find(TaskTextPath).GetComponent<TextMeshProUGUI>();
        GameObject view = Instantiate(tasks.Find(TaskViewPath).gameObject, transform);
        view.name = "Log view";
        RectTransform rect = (RectTransform)view.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        scroll = view.GetComponentInChildren<ScrollRect>(true);
        content = scroll.content;
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(content.GetChild(i).gameObject);
        }
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        VerticalLayoutGroup list = content.GetComponent<VerticalLayoutGroup>();
        list.padding = new RectOffset(20, 20, 20, 20);
        list.spacing = 6f;
        list.childControlWidth = true;
        list.childControlHeight = true;
        list.childForceExpandWidth = true;
        list.childForceExpandHeight = false;
    }

    private void OnEnable()
    {
        ShowNewEntries();
        scroll.verticalNormalizedPosition = 1f;
    }

    private void Update()
    {
        ShowNewEntries();
        float input = ScrollInput();
        float overflow = content.rect.height - ((RectTransform)scroll.transform).rect.height;
        if (input != 0f && overflow > 0f)
        {
            scroll.verticalNormalizedPosition = Mathf.Clamp01(scroll.verticalNormalizedPosition + input * ScrollSpeed * Time.unscaledDeltaTime / overflow);
        }
    }

    private void ShowNewEntries()
    {
        int unseen = Mathf.Min(LogHistory.Total - shown, LogHistory.Entries.Count);
        for (int i = LogHistory.Entries.Count - unseen; i < LogHistory.Entries.Count; i++)
        {
            AddRow(LogHistory.Entries[i]);
        }
        shown = LogHistory.Total;
        while (content.childCount > LogHistory.Capacity)
        {
            DestroyImmediate(content.GetChild(content.childCount - 1).gameObject);
        }
    }

    private void AddRow(LogEntry entry)
    {
        TextMeshProUGUI row = Instantiate(rowTemplate, content);
        DestroyImmediate(row.GetComponent<ContentSizeFitter>());
        row.transform.SetAsFirstSibling();
        row.raycastTarget = false;
        row.text = entry.Format();
    }

    // Positive scrolls up. The mouse wheel needs nothing here: ScrollRect handles it. The pad axes
    // only count in controller mode, as they do for the game's own lists, so stick drift can't scroll.
    private static float ScrollInput()
    {
        InputManager input = InputManager.instance;
        if (input.inputType == InputManager.InputType.Keyboard)
        {
            return (input.GetKey("Forward") ? 1f : 0f) - (input.GetKey("Backward") ? 1f : 0f);
        }
        float value = -Input.GetAxis("RightStickVertical");
        if (Mathf.Abs(value) < DeadZone)
        {
            value = Input.GetAxis("DPadVertical");
        }
        if (Mathf.Abs(value) < DeadZone)
        {
            value = Input.GetAxis("LeftStickInventoryVertical");
        }
        return Mathf.Abs(value) < DeadZone ? 0f : value;
    }
}
