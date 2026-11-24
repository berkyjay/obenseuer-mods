using System;
using TMPro;
using UnityEngine;

namespace BulkCraft;

internal static class Ui
{
    // A copy of one of the game's buttons, which keeps its look and sounds, next to the original.
    // The copy mustn't keep the original's Relay: that's a saved object, and a clone would write a
    // second save entry under the same GUID. Its click also went through that Relay, so the
    // button's click event is replaced too.
    internal static RectTransform CloneButton(GameObject source, string name, Action click)
    {
        GameObject clone = UnityEngine.Object.Instantiate(source, source.transform.parent);
        clone.name = name;
        foreach (SavableScript saved in clone.GetComponentsInChildren<SavableScript>(true))
        {
            UnityEngine.Object.DestroyImmediate(saved);
        }
        bool wired = false;
        foreach (UnityEngine.UI.Button button in clone.GetComponentsInChildren<UnityEngine.UI.Button>(true))
        {
            button.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            if (!wired)
            {
                button.onClick.AddListener(() => click());
                wired = true;
            }
        }
        if (!wired)
        {
            Plugin.Log.LogWarning($"{source.name} has no Button component; {name} won't respond to clicks");
        }
        foreach (TextMeshProUGUI label in clone.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            label.fontSizeMax = label.fontSize;
            label.fontSizeMin = 8f;
            label.enableAutoSizing = true;
        }
        // The liquid panel lays its buttons out in a row; this one is placed by hand below them.
        UnityEngine.UI.LayoutElement layout = clone.GetComponent<UnityEngine.UI.LayoutElement>() ?? clone.AddComponent<UnityEngine.UI.LayoutElement>();
        layout.ignoreLayout = true;
        return (RectTransform)clone.transform;
    }

    internal static void SetLabel(RectTransform root, string text)
    {
        if (root == null)
        {
            return;
        }
        foreach (TextMeshProUGUI label in root.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (label.text != text)
            {
                label.text = text;
            }
        }
    }

    internal static void SetActive(Component component, bool active)
    {
        if (component != null && component.gameObject.activeSelf != active)
        {
            component.gameObject.SetActive(active);
        }
    }

    internal static void Tell(string title, string message)
    {
        Plugin.Log.LogInfo($"{title}: {message}");
        Notifications.instance?.CreateNotification(title, message);
    }
}
