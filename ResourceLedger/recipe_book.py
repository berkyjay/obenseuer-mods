"""Build recipes.json for the recipe page: every recipe, which ones the character knows, and the
stock of each item involved.

export_ledger.py calls build() with the stock it counted, so one read of the save feeds both
pages. The rules follow the game's code (0.5.19):

- RecipeListUI lists a recipe at a station when it isn't RecipeRequired, or when RecipeController
  holds it in collectedRecipes (books, magazines, mail) or learnedRecipes (crafted once).
- CraftingBaseHelperFunctions fills an ingredient group (ItemGroupData) from any mix of its
  members, counts a stack with ammo by its ammo when the recipe uses ammo as input, and counts
  the liquid inside containers when the recipe uses liquid containers.
- PlayerStats.GetSkill rounds the skill down. The recipe list allows one level under a
  recipe's minimum for most recipes (see docs/obenseuer_skill_xp.md in the parent project).
"""
import json
import os

GAME = r"C:\Program Files (x86)\Steam\steamapps\common\Obenseuer"
RECIPES = os.path.join(GAME, "Obenseuer_Data", "StreamingAssets", "Recipes.json")  # UTF-16

# RecipeType names as the page shows them. Debug recipes are left out.
STATIONS = {
    "Basic": "Basic", "Kitchen": "Kitchen", "Stove": "Stove", "Oven": "Oven", "Microwave": "Microwave",
    "CoffeeMaker": "Coffee maker", "Mashing": "Mashing", "Fermenter": "Fermenter", "Distillery": "Distillery",
    "Malting": "Malting", "Bottler": "Bottler", "Grow": "Growing", "GrowMushrooms": "Growing mushrooms",
    "GrowVines": "Growing vines", "GrowAquatic": "Growing aquatic", "BreedAnimals": "Animal breeding",
    "Compost": "Compost", "WaterSource": "Water source", "Furnace": "Furnace", "Anvil": "Anvil",
    "Machining": "Machining", "Grinder": "Grinder", "Crusher": "Crusher", "Press": "Press",
    "Woodworking": "Woodworking", "Electronics": "Electronics", "Engineering": "Engineering",
    "Printer3D": "3D printer", "ChemicalLab": "Chemical lab", "ChemicalRefinery": "Chemical refinery",
    "PaperWork": "Paperwork", "Sewing": "Sewing", "Loom": "Loom",
}
NO_TAG = "NONE"


def pairs(entries):
    """Recipe inputs and outputs as [[item id, amount]]. Amounts are whole except liquids."""
    entries = entries.get("$content", []) if isinstance(entries, dict) else entries or []
    return [[e["Key"]["ID"], e["Value"]] for e in entries]


def build(catalog, stock, found, crafted, skills):
    """Return the recipe page's data, without the save stamp, which the caller adds."""
    items = {i["ID"]: i for i in catalog}
    titles = {i["Title"] for i in catalog}
    groups = {}
    for i in catalog:
        for m in i["Meta"]:
            if m.get("$type") == "OS.Items.ItemGroupData":
                groups[i["ID"]] = [g["item"]["ID"] for g in m["itemGroup"]]

    recipes, used = [], set()
    for r in json.load(open(RECIPES, encoding="utf-16"))["Recipes"]:
        if r["Type"] not in STATIONS:
            continue
        known = "found" if r["ID"] in found else "crafted" if r["ID"] in crafted else \
            "always" if not r["RecipeRequired"] else None
        settings = r["metaSettings"] or {}
        row = {"id": r["ID"], "name": r["Name"].strip(), "station": STATIONS[r["Type"]], "known": known,
               "in": pairs(r["input"]), "out": pairs(r["output"]),
               "skill": r["RelevantSkill"], "min": r["MinimumSkill"], "hard": r["HardMinimumSkill"],
               "time": r["ProcessTime"], "fail": r["FailChance"],
               "ammo": settings.get("useAmmoAsInput", False), "liquid": settings.get("useLiquidContainers", False)}
        # A few recipes list None among their conditions, which no station has, so it means nothing.
        needs = [c for c in r["RequiredConditions"] if c != "None"]
        excludes = [c for c in r["ExcludingConditions"] if c != "None"]
        if needs:
            row["needs"] = needs
        if excludes:
            row["not"] = excludes
        if r["variantItemCategory"] != "None":
            row["variant"] = [r["variantItemCategory"], max(1, r["variantItemAmount"])]
        if not known:
            # Some tags are the title of the magazine that teaches the recipe; the rest name a group.
            tags = [t for t in r["RecipeTags"] if t != NO_TAG]
            row["from"] = [t for t in tags if t in titles]
            row["tags"] = [t for t in tags if t not in titles]
        recipes.append(row)
        used.update(i for i, _ in row["in"] + row["out"])

    for g in list(used):
        used.update(groups.get(g, []))
    out = {}
    for i in sorted(used):
        item = items.get(i)
        entry = {"name": item["Title"] if item else f"Item {i}"}
        n, uses, litres = stock.count.get(i, 0), stock.uses.get(i, 0), round(stock.litres.get(i, 0), 1)
        if n:
            entry["n"] = n
        if uses != n:
            entry["uses"] = round(uses, 1)
        if litres:
            entry["litres"] = litres
        if i in groups:
            entry["group"] = groups[i]
        if item and "Liquid" in item["Categories"]:
            entry["isLiquid"] = True
        where = stock.where.get(i)
        if where:
            entry["where"] = sorted(([p, round(a, 1)] for p, a in where.items()), key=lambda w: -w[1])
        out[str(i)] = entry
    return {"recipes": recipes, "items": out, "skills": {k: int(v) for k, v in skills.items()}}
