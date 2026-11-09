using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using OS.Items;

namespace HarvestAllButton;

// Which items Replant may use. Seed quality has no effect on the crop (StartGrow ignores it), so
// this only picks which stack is used up. True seeds have no quality at all.
public enum ReplantChoice
{
    Seeds,
    Lowest,
    Highest,
    Crap,
    Poor,
    Bad,
    Okay,
    Good,
    Fine,
    Excellent
}

// One-slot harvesting and planting, through the game's own code where it exists.
internal static class Farming
{
    private static readonly AccessTools.FieldRef<CraftingBase, List<Recipe>> AllRecipes =
        AccessTools.FieldRefAccess<CraftingBase, List<Recipe>>("AllRecipes");

    private static readonly AccessTools.FieldRef<CraftingBase, Recipe> SelectedRecipe =
        AccessTools.FieldRefAccess<CraftingBase, Recipe>("selectedRecipe");

    private static readonly AccessTools.FieldRef<GrowingSpot, ItemReference> SavedPlant =
        AccessTools.FieldRefAccess<GrowingSpot, ItemReference>("savedPlant");

    private static readonly Func<Growing, List<ItemStack>, bool> HasSpace =
        AccessTools.MethodDelegate<Func<Growing, List<ItemStack>, bool>>(
            AccessTools.Method(typeof(Growing), "HasSpaceFor", new[] { typeof(List<ItemStack>) }));

    private static readonly Func<Growing, Recipe, ItemStack[], bool> CanPlant =
        AccessTools.MethodDelegate<Func<Growing, Recipe, ItemStack[], bool>>(
            AccessTools.Method(typeof(Growing), "CanQuickPlant"));

    private static readonly Func<Growing, Recipe, bool> HasSkill =
        AccessTools.MethodDelegate<Func<Growing, Recipe, bool>>(
            AccessTools.Method(typeof(Growing), "HasQuickPlantSkill"));

    private static readonly Func<Growing, Recipe, Item, ItemStack[], IEnumerable<ItemStack>> FindStacks =
        AccessTools.MethodDelegate<Func<Growing, Recipe, Item, ItemStack[], IEnumerable<ItemStack>>>(
            AccessTools.Method(typeof(Growing), "FindInputStacks"));

    private static readonly Action<Growing, Recipe, int, GrowingSpot, ItemStack[]> StartGrow =
        AccessTools.MethodDelegate<Action<Growing, Recipe, int, GrowingSpot, ItemStack[]>>(
            AccessTools.Method(typeof(Growing), "StartGrow", new[] { typeof(Recipe), typeof(int), typeof(GrowingSpot), typeof(ItemStack[]) }));

    private static readonly Action<Growing> AverageLights =
        AccessTools.MethodDelegate<Action<Growing>>(AccessTools.Method(typeof(Growing), "AveragePlantLights"));

    // What each emptied slot last held, for Replant. Lives until the scene unloads.
    private static readonly Dictionary<GrowingSpot, int> LastPlant = new Dictionary<GrowingSpot, int>();

    // Animal stations keep the game's behaviour, and planters that belong to someone else are left
    // alone because taking from them is theft. This is the game's own "Owned" test.
    internal static bool Supported(Growing station)
    {
        if (station.GetComponent<AnimalBreeder>() != null || station.RecipesWithUpgrades.Contains(RecipeType.BreedAnimals))
        {
            return false;
        }
        return station.owner == null || station.owner.ID <= 1 || station.PermissionToTake;
    }

    private static grow PlantIn(GrowingSpot slot)
    {
        return (slot != null && slot.HasPlant()) ? slot.Plant.GetComponentInChildren<grow>() : null;
    }

    // grow.HarvestExternal has no ripeness check: it uproots whatever it's given. Only call it on
    // plants that are dead, have ripe fruit, or are fully grown with no fruit to wait for.
    internal static bool IsReady(GrowingSpot slot)
    {
        grow plant = PlantIn(slot);
        if (plant == null)
        {
            return false;
        }
        if (plant.growSpoil != null && plant.growSpoil.spoiled)
        {
            return true;
        }
        if (plant.visibleGrownItems.Length != 0)
        {
            return plant.visibleGrownItems.Any(fruit => fruit.gameObject.activeInHierarchy && fruit.readyToPick);
        }
        return plant.Age >= plant.FullGrownAge;
    }

    // Returns true if the slot was harvested. full means the items fit in neither the inventory
    // nor the planter, so the plant stayed as it was.
    internal static bool Harvest(Growing station, GrowingSpot slot, out bool full)
    {
        full = false;
        if (!IsReady(slot))
        {
            return false;
        }
        grow plant = PlantIn(slot);
        int plantId = SavedPlant(slot)?.ID ?? -1;
        bool destroy;
        HarvestOverflow.Station = station;
        try
        {
            List<ItemStack> stacks = plant.HarvestExternal(out destroy, out full, items => HasSpace(station, items));
            foreach (ItemStack stack in stacks)
            {
                ItemOperations.AddItems(stack);
            }
        }
        finally
        {
            HarvestOverflow.Station = null;
        }
        if (destroy && !plant.dontDestroyOnPickUp)
        {
            slot.HarvestPlant();
            foreach (GrowingSpot gone in LastPlant.Keys.Where(key => key == null).ToList())
            {
                LastPlant.Remove(gone);
            }
            if (plantId != -1)
            {
                LastPlant[slot] = plantId;
            }
        }
        return !full;
    }

    internal static bool CanReplant(Growing station, GrowingSpot slot)
    {
        return !slot.HasPlant() && Pick(station, slot, Plugin.Replant.Value).recipe != null;
    }

    internal static bool Replant(Growing station, GrowingSpot slot)
    {
        if (slot.HasPlant())
        {
            return false;
        }
        (Recipe recipe, ItemStack stack) = Pick(station, slot, Plugin.Replant.Value);
        if (recipe == null)
        {
            return false;
        }
        // RemoveItems takes from the first matching stack, so the chosen one goes first.
        ItemStack[] items = station.InputSlots.Where(other => other != stack).Prepend(stack).ToArray();
        StartGrow(station, recipe, 1, slot, items);
        return true;
    }

    // Only meaningful when no slot could be replanted: true if that's down to a choice of crops.
    internal static bool NeedsCropChoice(Growing station)
    {
        return CropCount(Options(station, AllRecipes(station), Plugin.Replant.Value)) > 1;
    }

    // A slot's own plant wins while there are items for it. Then comes the recipe selected in the
    // list, which is the last one clicked or hovered, and null before that. Then the only crop that
    // can be planted; with a choice of crops, that's for the player to decide. Low, Medium and High
    // Potency mushroom bags are separate crops, so using up one kind lands here.
    private static (Recipe recipe, ItemStack stack) Pick(Growing station, GrowingSpot slot, ReplantChoice choice)
    {
        var options = new List<(Recipe recipe, ItemStack stack)>();
        if (LastPlant.TryGetValue(slot, out int plantId))
        {
            options = Options(station, AllRecipes(station).Where(recipe => Grows(recipe, plantId)), choice);
        }
        Recipe selected = SelectedRecipe(station);
        if (options.Count == 0 && selected != null)
        {
            options = Options(station, new[] { selected }, choice);
        }
        if (options.Count == 0)
        {
            options = Options(station, AllRecipes(station), choice);
            if (CropCount(options) > 1)
            {
                options.Clear();
            }
        }
        var best = options.OrderBy(option => choice == ReplantChoice.Highest ? -QualityOf(option.stack) : QualityOf(option.stack))
            .ThenBy(option => option.recipe.FailChance);
        return best.FirstOrDefault();
    }

    private static List<(Recipe recipe, ItemStack stack)> Options(Growing station, IEnumerable<Recipe> recipes, ReplantChoice choice)
    {
        ItemStack[] stacks = station.InputSlots;
        var options = new List<(Recipe recipe, ItemStack stack)>();
        foreach (Recipe recipe in recipes)
        {
            if (!CanPlant(station, recipe, stacks) || !HasSkill(station, recipe))
            {
                continue;
            }
            Item input = recipe.Input.Keys.FirstOrDefault(item => item.Categories == null || !item.Categories.Contains("Liquid")) ?? recipe.Input.Keys.First();
            options.AddRange(FindStacks(station, recipe, input, stacks).Where(stack => Matches(stack, choice)).Select(stack => (recipe, stack)));
        }
        return options;
    }

    private static bool Grows(Recipe recipe, int plantId)
    {
        if (recipe.Output == null)
        {
            recipe.SetItems();
        }
        return recipe.Output != null && recipe.Output.Keys.Any(item => item.ID == plantId);
    }

    // StartGrow plants the recipe's first output.
    private static int CropCount(List<(Recipe recipe, ItemStack stack)> options)
    {
        return options.Select(option => option.recipe.Output.Keys.First().ID).Distinct().Count();
    }

    // Items with no quality rank as Okay, the game's default of 1.
    private static float QualityOf(ItemStack stack)
    {
        return stack.GetMetaOfType<ItemQualityData>()?.quality ?? 1f;
    }

    private static bool Matches(ItemStack stack, ReplantChoice choice)
    {
        ItemQualityData quality = stack.GetMetaOfType<ItemQualityData>();
        switch (choice)
        {
            case ReplantChoice.Seeds:
                return quality == null;
            case ReplantChoice.Lowest:
            case ReplantChoice.Highest:
                return true;
            default:
                return quality != null && quality.GetQuality().ToString() == choice.ToString();
        }
    }

    internal static bool HasRoomFor(Growing station, ItemStack stack)
    {
        return HasSpace(station, new List<ItemStack> { stack });
    }

    internal static void UpdateViews(Growing station)
    {
        GrowingPanel.instance.UpdateItemsInProgress();
        station.InputOutputStorage.UpdateSlots();
        station.OutputStorage?.UpdateSlots();
        Inventory.instance.UpdateAllSlots();
        station.CheckItems();
    }

    internal static void FinishJob(Growing station)
    {
        AverageLights(station);
        if ((bool)InventoryNavigationHandler.Instance)
        {
            InventoryNavigationHandler.Instance.InitializeGrowingPanelProgressSlots(startup: false);
        }
    }
}

// Set during a harvest, so items that don't fit in the inventory go to this planter. Not in Farming:
// the AddItems patch reads it for every item the game adds, and if a game update broke one of
// Farming's reflected lookups, touching that class would throw and stop all of them.
internal static class HarvestOverflow
{
    internal static Growing Station;
}

// Harvests add items with ItemOperations.AddItems, which drops whatever doesn't fit on the ground.
// While a harvest runs, fill the inventory and backpack, then put the rest in the planter.
[HarmonyPatch(typeof(ItemOperations), nameof(ItemOperations.AddItems), new[] { typeof(Item), typeof(int), typeof(int), typeof(object[]), typeof(SlotController), typeof(bool), typeof(bool) })]
internal static class HarvestToInventoryThenPlanter
{
    private static bool Prefix(Item item, int amount, int owner, object[] meta, SlotController slot, ref bool __result)
    {
        Growing station = HarvestOverflow.Station;
        if (station == null || slot != null)
        {
            return true;
        }
        int rest = ItemOperations.AddItemAndReturnRemaining(item, owner, null, meta, amount);
        if (rest > 0)
        {
            (station.OutputStorage ?? station.InputOutputStorage).AddItem(item, rest, owner, meta);
        }
        __result = true;
        return false;
    }
}

// Ripe fruit is left on the plant when the inventory is full. During a harvest, the planter's
// slots count as room too.
[HarmonyPatch(typeof(GrowItem), nameof(GrowItem.InventoryHasSpace))]
internal static class FruitFitsInPlanter
{
    private static void Postfix(grow.ItemAmount ___item, int ___itemAmount, object[] ___meta, grow ___grow, ref bool __result)
    {
        if (!__result && HarvestOverflow.Station != null)
        {
            ItemStack stack = ItemStack.CreateStackFromItem(___item.item.Item, ___itemAmount, ___grow.ownerId, ___meta);
            __result = Farming.HasRoomFor(HarvestOverflow.Station, stack);
        }
    }
}
