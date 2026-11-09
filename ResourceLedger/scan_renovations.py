"""Read the game's renovation definitions into renovations.json for export_ledger.py.

Usage: python scan_renovations.py renovations.json
Needs UnityPy (make a scratch venv with py -3.12) and ilspycmd. Reads the game install only.
Rerun after a game update, since the save holds only part of this: building upgrades above
the current tier and two of the four contractors are found only in the asset bundle.
Loading that bundle takes about 20 seconds and peaks at 5.6 GB of memory, so run it alone.
"""
import glob
import json
import os
import re
import subprocess
import sys

import UnityPy

GAME = r"C:\Program Files (x86)\Steam\steamapps\common\Obenseuer"
DATA = os.path.join(GAME, "Obenseuer_Data")
DLL = os.path.join(DATA, "Managed", "Assembly-CSharp.dll")
BUNDLES = os.path.join(DATA, "StreamingAssets", "aa", "StandaloneWindows64", "defaultlocalgroup_assets_all_*.bundle")
ILSPY = os.path.expandvars(r"%USERPROFILE%\.dotnet\tools\ilspycmd.exe")
ILSPY_LIMIT = 90  # seconds
ROOT = "Assets/Content/Tenement/"

# TenementUpgrade's flag, then its tier field, tier enum, the name GetName gives it, the
# field holding the current tier in the save, and the TenementContractor limit from CanUpgrade.
TRACKS = [
    ("upgradeTenementElectricity", "tenementElectricityType", "TenementElectricity", "Tenement electricity", "currentElectricity", "maxTenementElectricityUpgrade"),
    ("upgradeTenementWater", "tenementWaterType", "TenementWater", "Tenement plumbing", "currentWater", "maxTenementWaterUpgrade"),
    ("upgradeHeating", "heatingType", "TenementHeating", "Tenement heating room", "currentHeating", "maxTenementHeatingUpgrade"),
    ("upgradeElevator", "elevatorType", "TenementElevator", "Elevator", "currentElevator", "maxTenementElevatorUpgrade"),
    ("upgradeStairs", "stairsType", "TenementStairs", "Stairwell", "currentStairs", "maxTenementStairsUpgrade"),
    ("upgradeBasement", "basementType", "TenementBasement", "Basement", "currentBasement", "maxTenementBasementUpgrade"),
    ("upgradeBasementSauna", "basementSaunaType", "BasementSauna", "Basement sauna", "currentSauna", "maxBasementSaunaUpgrade"),
    ("upgradeBasementShrooms", "basementShroomsType", "BasementShrooms", "Basement mushroom farm", "currentShrooms", "maxBasementShroomsUpgrade"),
    ("upgradeWorkshop", "workshopType", "BasementWorkShop", "Workshop", "currentWorkshop", "maxBasementWorkShopUpgrade"),
    ("upgradeMine", "mineType", "BasementMine", "Mine", "currentMine", "maxBasementMineUpgrade"),
    ("upgradeRoof", "roofType", "TenementRoof", "Rooftop greenhouse", "currentRoof", "maxTenementRoofUpgrade"),
    ("upgradeOuterWalls", "outerWallType", "OuterWalls", "Facade", "currentFacade", "maxTenementFacadeUpgrade"),
    ("upgradeCourtyard", "courtyardType", "Courtyard", "Courtyard", "currentCourtyard", "maxCourtyardUpgrade"),
    ("upgradeSaunaAndPier", "saunaAndPierType", "SaunaAndPier", "Sauna and pier", "currentSaunaAndPier", "maxSaunaAndPierUpgrade"),
    ("upgradeElectricity", "electricityType", "ApartmentElectricity", "Apartment electricity", "currentElectricity", "maxApartmentUpgrade"),
    ("upgradeWater", "waterType", "ApartmentWater", "Apartment water", "currentWater", "maxApartmentUpgrade"),
    ("upgradeHeatingMethod", "heatingMethod", "ApartmentHeatingMethod", "Apartment heating", "currentHeating", "maxApartmentUpgrade"),
    ("upgradeBathroom", "bathroomType", "BathroomType", "Apartment bathroom", "currentBathroom", "maxApartmentUpgrade"),
    ("upgradeWalls", "wallType", "ApartmentWallType", "Apartment walls", "currentWall", "maxApartmentUpgrade"),
    ("upgradeBalcony", "balconyType", "BalconyType", "Apartment balcony", "currentBalcony", "maxApartmentUpgrade"),
    ("upgradeBalcony2", "balcony2Type", "BalconyType", "Apartment balcony", "currentBalcony2", "maxApartmentUpgrade"),
    ("upgradeKitchen", "kitchenType", "KitchenType", "Apartment kitchen", "currentKitchen", "maxApartmentUpgrade"),
]

# Tracks that GetName labels by apartment number, or that one apartment type has alone.
# The contractor limit for the shop is maxShopUpgrade; the shared tracks use the apartment
# limit here, since they cover sixteen other apartments too.
SPECIAL = {
    "Large-Balcony-Back": ("Back balcony", "Apartments", None),
    "Player-Extension-Front": ("Front extension", "Your apartment", None),
    "Player-Extension-Back": ("Back extension", "Your apartment", None),
    "Shop-Bathroom": ("Shop bathroom", "Shop", "maxShopUpgrade"),
    "Shop-Walls": ("Shop walls", "Shop", "maxShopUpgrade"),
}


def enums():
    """Return every enum in UpgradeType as {name: [member, ...]}, in tier order."""
    p = subprocess.Popen([ILSPY, "-t", "UpgradeType", DLL], stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True)
    try:
        source, _ = p.communicate(timeout=ILSPY_LIMIT)
    except subprocess.TimeoutExpired:
        subprocess.run(["taskkill", "/PID", str(p.pid), "/T", "/F"], capture_output=True)
        p.wait()
        raise
    found = {}
    for name, body in re.findall(r"public enum (\w+)\s*\{([^}]*)\}", source):
        assert "=" not in body, f"{name} sets explicit values, so member order is no longer the tier"
        found[name] = [m.strip() for m in body.split(",") if m.strip()]
    return found


def read_assets():
    """Return ({path: typetree}, {path id: path}) for the tenement assets in the bundle."""
    assets, paths = {}, {}
    env = UnityPy.load(glob.glob(BUNDLES)[0])
    for obj in env.objects:
        if obj.type.name != "AssetBundle":
            continue
        for path, info in obj.read().m_Container:
            if path.startswith(ROOT + "Upgrades/") or path.startswith(ROOT + "Contractors/"):
                paths[info.asset.path_id] = path
                target = info.asset.deref()
                if target.type.name == "MonoBehaviour":
                    assets[path] = target.read_typetree()
    return assets, paths


def build_catalog(assets, paths, enum_names):
    people = sorted((a for p, a in assets.items() if "/Contractors/" in p), key=lambda a: a["contractorName"])
    tracks = []
    for path, group in sorted(assets.items()):
        if "updateGroup" not in group:
            continue
        chain = [paths[u["m_PathID"]] for u in group["updateGroup"]]
        flag, tier_field, enum, label, field, limit = next(t for t in TRACKS if assets[chain[0]][t[0]])
        name = path.rsplit("/", 1)[1].removesuffix(".asset")
        building = "/Upgrades/General/" in path
        label, heading, shop_limit = SPECIAL.get(name, (label, "Tenement" if building else "Apartments", None))
        limit = shop_limit or limit
        tiers = []
        for tier_path in chain:
            upgrade = assets[tier_path]
            tier = upgrade[tier_field]
            needs = {}
            for need in upgrade["requiredItems"]:
                item = str(need["Item"]["ID"])
                needs[item] = needs.get(item, 0) + need["itemAmount"]
            tiers.append({"path": tier_path, "tier": tier, "name": enum_names[enum][tier], "needs": needs,
                          "first": upgrade["priceStartOC"], "final": upgrade["priceFinalRM"],
                          "requires": [paths[r["m_PathID"]] for r in upgrade["requiredUpgrades"]],
                          "contractors": [c["id"] for c in people if c[limit] >= tier]})
        tracks.append({"group": path, "label": label, "heading": heading, "building": bool(building),
                       "field": field, "enum": enum_names[enum], "tiers": tiers})
    order = {"Tenement": 0, "Apartments": 1, "Your apartment": 2, "Shop": 3}
    tracks.sort(key=lambda t: (order[t["heading"]], t["label"]))
    return {"contractors": {c["id"]: c["contractorName"] for c in people}, "tracks": tracks}


def main():
    assets, paths = read_assets()
    catalog = build_catalog(assets, paths, enums())
    tracks = catalog["tracks"]
    with open(sys.argv[1], "w", encoding="utf-8", newline="\n") as f:
        json.dump(catalog, f, indent=1)
    print(len(tracks), "tracks,", sum(len(t["tiers"]) for t in tracks), "tiers,", len(catalog["contractors"]), "contractors")


if __name__ == "__main__":
    main()
