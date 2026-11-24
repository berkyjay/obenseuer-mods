using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BulkCraft;

// The "Craft all" button on a crafting station's recipe panel, for recipes that take no time
// (Rollie, Mushroom Rollie, e-cigarette refills, plated meals, ...). It presses the game's own
// Craft once per frame until the station runs out of ingredients, so every craft rolls its own
// failure, quality and skill gain exactly as a click would. It stops early if an output would
// land on the floor, if the panel closes or another recipe is picked, or if a craft doesn't
// happen (no power, station busy). Nothing about a run is saved.
internal sealed class CraftAll : MonoBehaviour
{
    private const float Gap = 4f;

    private static readonly AccessTools.FieldRef<CraftingBase, Recipe> SelectedRecipe =
        AccessTools.FieldRefAccess<CraftingBase, Recipe>("selectedRecipe");

    private static readonly MethodInfo CompareRecipeToItems =
        AccessTools.Method(typeof(CraftingBase), "CompareRecipeToItems");

    // The station a run is crafting at, read by the CreateItems patch to count crafts.
    internal static manufacturing Running;
    internal static int Crafts;
    internal static int Failures;

    private RecipeInfoUI info;
    private RectTransform button;
    private Vector2 fullSize;
    private bool instantRecipe;

    private int recipeId;
    private string recipeName;

    internal static CraftAll For(RecipeInfoUI info)
    {
        CraftAll craftAll = info.GetComponent<CraftAll>();
        if (craftAll == null)
        {
            craftAll = info.gameObject.AddComponent<CraftAll>();
            craftAll.info = info;
        }
        return craftAll;
    }

    // Called after every RecipeInfoUI.ShowRecipe on the crafting panel. ShowRecipe destroys and
    // re-creates the Craft buttons each time, which takes this button with them.
    internal void Shown(Recipe recipe)
    {
        button = null;
        manufacturing station = CraftingBase.currentCraftingBase as manufacturing;
        instantRecipe = recipe != null && recipe.ProcessTime <= 0 && station != null && !station.Autocraft;
        if (!instantRecipe || info.createButtonEnabled == null)
        {
            return;
        }
        RectTransform craft = (RectTransform)info.createButtonEnabled.transform;
        fullSize = craft.sizeDelta;
        button = Ui.CloneButton(info.createButtonEnabled, "Craft all button", Press);
        button.gameObject.SetActive(false);
        LateUpdate();
    }

    private void Press()
    {
        if (Running != null)
        {
            Stop("Stopped");
            return;
        }
        manufacturing station = CraftingBase.currentCraftingBase as manufacturing;
        Recipe recipe = info.CurrentRecipe();
        if (station == null || recipe == null)
        {
            return;
        }
        Running = station;
        recipeId = recipe.ID;
        recipeName = recipe.Name;
        Crafts = 0;
        Failures = 0;
        // The first craft happens on the next Update, after this click has finished.
    }

    private void Update()
    {
        if (Running == null)
        {
            return;
        }
        try
        {
            CraftOnce();
        }
        catch (System.Exception exception)
        {
            Plugin.Log.LogError(exception);
            Stop("Stopped: something went wrong, see LogOutput.log");
        }
    }

    private void CraftOnce()
    {
        manufacturing station = Running;
        if (station == null || CraftingBase.currentCraftingBase != station)
        {
            Stop("Stopped: the station was closed");
            return;
        }
        Recipe shown = info.CurrentRecipe();
        if (shown == null || shown.ID != recipeId || shown.Name != recipeName)
        {
            Stop("Stopped: another recipe was picked");
            return;
        }
        // The game's Craft doesn't check ingredients itself; its button being enabled is the check.
        if (info.createButtonEnabled == null || !info.createButtonEnabled.activeSelf || info.craftingDisabled)
        {
            Stop(null);
            return;
        }
        Recipe recipe = SelectedRecipe(station);
        if (recipe == null || recipe.ID != recipeId || !(bool)CompareRecipeToItems.Invoke(station, new object[] { recipe, null, false, true }))
        {
            Stop(null);
            return;
        }
        if (!OutputsFit(station, recipe))
        {
            Inventory.instance.inventoryPanelUI.InventoryFull();
            Stop("Stopped: no room for more in the station or your inventory");
            return;
        }
        int before = Crafts;
        station.Craft();
        if (Crafts == before)
        {
            Stop("Stopped: the station didn't craft (no power, or busy)");
        }
    }

    // Whether every possible output (success or failure) would fit in the station or, as the game
    // overflows there, the player's inventory, so nothing gets dropped on the floor. Uses the
    // game's own per-slot rule; empty slots are only counted once.
    private static bool OutputsFit(manufacturing station, Recipe recipe)
    {
        if (recipe.Output == null)
        {
            recipe.SetItems();
        }
        Storage storage = station.OutputStorage ?? station.InputOutputStorage;
        List<SlotController> pool = new List<SlotController>();
        if (storage != null && storage.Slots != null)
        {
            pool.AddRange(storage.Slots);
        }
        if (Inventory.instance != null && Inventory.instance.Slots != null)
        {
            pool.AddRange(Inventory.instance.Slots);
        }
        HashSet<SlotController> claimed = new HashSet<SlotController>();
        IEnumerable<KeyValuePair<Item, float>> outputs = (recipe.Output ?? new Dictionary<Item, float>())
            .Concat(recipe.OutputFail ?? new Dictionary<Item, float>());
        foreach (KeyValuePair<Item, float> output in outputs)
        {
            Item item = output.Key;
            // Liquid outputs go into containers or the station's liquid store, not slots.
            if (item == null || (item.Categories != null && item.Categories.Contains("Liquid")))
            {
                continue;
            }
            int remaining = Mathf.Max(1, Mathf.CeilToInt(output.Value));
            object[] meta = item.CloneMeta();
            foreach (SlotController slot in pool)
            {
                if (slot == null || claimed.Contains(slot))
                {
                    continue;
                }
                bool wasEmpty = slot.itemStack.IsEmpty();
                SlotController.SlotHasSpaceForItem(item, slot, slot.itemStack, out int left, -1, remaining, 0, meta);
                if (left < remaining)
                {
                    if (wasEmpty)
                    {
                        claimed.Add(slot);
                    }
                    remaining = left;
                }
                if (remaining <= 0)
                {
                    break;
                }
            }
            if (remaining > 0)
            {
                return false;
            }
        }
        return true;
    }

    private void Stop(string reason)
    {
        if (Running == null)
        {
            return;
        }
        Running = null;
        string made = Crafts == 0 ? $"Crafted no {recipeName}" : $"Crafted {recipeName} {Crafts} times";
        if (Failures > 0)
        {
            made += $" ({Failures} failed)";
        }
        Ui.Tell("Craft all", reason == null ? made : $"{reason}. {made}");
        LateUpdate();
    }

    private void OnDisable()
    {
        Stop("Stopped: the recipe panel closed");
    }

    // The game turns its Craft buttons on and off after ShowRecipe too (slots full, missing
    // ingredients), so the split is checked every frame.
    private void LateUpdate()
    {
        if (button == null || info == null)
        {
            return;
        }
        bool running = Running != null;
        bool show = running || (instantRecipe && info.createButtonEnabled != null && info.createButtonEnabled.activeSelf && !info.craftingDisabled);
        float half = (fullSize.x - Gap) / 2f;
        foreach (GameObject vanilla in new[] { info.createButtonEnabled, info.createButtonDisabled, info.createButtonProgress })
        {
            if (vanilla != null)
            {
                RectTransform rect = (RectTransform)vanilla.transform;
                rect.sizeDelta = show ? new Vector2(half, fullSize.y) : fullSize;
                rect.anchoredPosition = show ? new Vector2(-(half + Gap) / 2f, 0f) : Vector2.zero;
            }
        }
        button.sizeDelta = new Vector2(half, fullSize.y);
        button.anchoredPosition = new Vector2((half + Gap) / 2f, 0f);
        Ui.SetLabel(button, running ? $"Stop ({Crafts})" : "Craft all");
        Ui.SetActive(button, show);
    }
}
