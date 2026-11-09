"""Send the ledger site the tenement resources the player has collected, and where each one is.

Usage: python export_ledger.py [--force | --watch]
Once there is a newer save, this builds ledger.json and recipes.json (see recipe_book.py) from
it and pushes both to the ledger site. Saves are only read. Run alone it checks once; --force sends even if nothing
is newer. The "Obenseuer Ledger Export" scheduled task uses --watch, which checks once and
then waits on the server for the page's Check button, ending after WATCH_SECONDS until the task
starts it again. The outcome of the last run is in LOG below, since the task has no window
to show errors in.

Counted: placed storage furniture, the contractor resource chests, built-in storage in the
player's apartment, and what they carry. Shops, residents' apartments, heater fuel and world
loot are not their stock; see the notes in the page.
"""
import ctypes
import glob
import json
import msvcrt
import os
import posixpath
import re
import subprocess
import sys
import time
import traceback
import urllib.request
from ctypes import wintypes

import recipe_book

GAME = r"C:\Program Files (x86)\Steam\steamapps\common\Obenseuer"
ITEMS = os.path.join(GAME, "Obenseuer_Data", "StreamingAssets", "Items.json")
SAVES = os.path.expandvars(r"%USERPROFILE%\AppData\LocalLow\Loiste Interactive\Obenseuer\Saves")  # one folder per character

CONFIG = os.path.join(os.path.dirname(os.path.abspath(__file__)), "config.json")  # see config.example.json
PUSH_LIMIT = 30  # seconds; ssh is killed after this
QUIET_SECONDS = 30  # leave a slot alone until the game has stopped writing to it
WATCH_SECONDS = 285  # the task then restarts this within a minute, and stops it after 7
WAIT_LIMIT = 40  # longer than the server holds /api/wait open
SAVING_RETRIES, SAVING_RETRY_SECONDS = 8, 5  # how long a button press waits out a save

STILL_SAVING = "Waiting for the game to finish saving"
FAILED = "Couldn't finish the check. Details are in last_run.txt on the game PC."

STATE_DIR = os.path.expandvars(r"%LOCALAPPDATA%\obenseuer-ledger")
STATE = os.path.join(STATE_DIR, "last_export.txt")  # Date of the last save sent
LOG = os.path.join(STATE_DIR, "last_run.txt")

GENERIC_READ = 0x80000000
FILE_SHARE_ALL = 0x7  # read, write and delete
OPEN_EXISTING = 3
CreateFileW = ctypes.WinDLL("kernel32", use_last_error=True).CreateFileW
CreateFileW.restype = wintypes.HANDLE
CreateFileW.argtypes = (wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, wintypes.LPVOID,
                        wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE)

RESOURCE_CATEGORY = "Tenement Resource"
RENOVATIONS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "renovations.json")  # from scan_renovations.py

# DifficultyInfo: the renovationCost and renovationResourceCosts of each preset, which match,
# and what each setting multiplies by.
PRESET_COSTS = ["Default", "Half"]  # any later index is Custom, which the save records
COST_MULTIPLIERS = {
    "Default": 1, "Disabled": 0, "Quarter": .25, "Half": .5, "ThreeQuarters": .75, "OneAndQuarter": 1.25,
    "OneAndHalf": 1.5, "OneAndThreeQuarters": 1.75, "Doubled": 2, "TwoAndQuarter": 2.25, "TwoAndHalf": 2.5,
    "TwoAndThreeQuarters": 2.75, "Threefold": 3, "ThreeAndQuarter": 3.25, "ThreeAndHalf": 3.5,
    "ThreeAndThreeQuarters": 3.75, "Fourfold": 4,
}
# TenementApartmentType's upgrade groups, in the save's apartment records.
APARTMENT_GROUPS = ("electricity", "water", "heating", "bathroom", "walls", "balcony", "balcony2", "kitchen")

# Scene object paths of the BuildingArea FurnitureManagers. Anything else falls back to its scene.
ROOMS = {
    "c7db3d3a-54c4-4004-a18b-a01259563671": "Basement",
    "75e83047-b2e2-44b7-b2c2-82e94300a2ba": "Your apartment",
    "989e033e-ccd6-4ae1-b967-0f8d20212823": "Shop",
    "4a80863d-198b-4988-80ef-67695f7bc9b2": "Stairs",
    "1cc2f10a-204d-4bad-be50-37d2f62df62d": "Utility room",
    "9dcdfecb-6554-402d-812d-19b7d43ad560": "Sauna",
    "63364261-b271-4540-bfec-e4f3802a2519": "Shroom room",
    "4eee9fe4-55b9-499d-b86c-fc66bdf9e207": "Greenhouse",
    "65cd4b8a-4f54-4fc0-aeed-46a060c6cf97": "Mines",
    "57beaa39-5a3e-4c3f-9bee-04af4d407d17": "Canal sauna",
}

# Every Storage with a TenementResourceStorage component (0.5.17 scene scan). Samuel's and
# Ken's match resourceStorageSnapshotsByContractor; Rob and Urho are the NPCs in their scenes.
# Books has a chest but no contractor. A chest joins the ledger once its scene is in the save.
CONTRACTOR_CHESTS = {
    ("Interior Tenement Deekula A", "9cc34023-0ed1-4c40-abda-00810081a5e0"): ("Samuel's chest", "Deekula A, Contract Work", "SamuelJonasson"),
    ("Interior Mines OS Mining Services", "cdf8c74a-64bd-49c4-bbfc-4d1bc3ea9abf"): ("Ken's chest", "OS Mining Services", "KenNakahara"),
    ("Interior Bazaar Rob-in", "cdf8c74a-64bd-49c4-bbfc-4d1bc3ea9abf"): ("Rob's chest", "Bazaar, Rob-in", "RobMadsen"),
    ("Interior Bazaar Engineer Office", "cdf8c74a-64bd-49c4-bbfc-4d1bc3ea9abf"): ("Urho's chest", "Bazaar, Engineer Office", "UrhoSiltanen"),
    ("Interior Bazaar Books", "cdf8c74a-64bd-49c4-bbfc-4d1bc3ea9abf"): ("Books chest", "Bazaar", None),
}

OWN_APARTMENT = "APT_1_"
HEATER = "Upgrade_Heating_"
RESIDENT_APARTMENT = re.compile(r"APT_(?!1_)\d+_")
ALWAYS_LIST_SLOTS = 48  # big containers stay listed when empty, as free space


def index_ids(o, ids):
    """Collect the $id objects Json.NET shares through $ref."""
    if isinstance(o, dict):
        if "$id" in o:
            ids[o["$id"]] = o
        for v in o.values():
            index_ids(v, ids)
    elif isinstance(o, list):
        for v in o:
            index_ids(v, ids)


def deref(o, ids):
    while isinstance(o, dict) and "$ref" in o:
        o = ids.get(o["$ref"])
    return o


def open_shared(path):
    """Open a save so this reader can never block the game.

    Saving to another slot starts with Directory.Delete on that slot's folder, and a plain
    open() lacks FILE_SHARE_DELETE, so it would fail that delete and leave the folder half gone.
    """
    handle = CreateFileW(path, GENERIC_READ, FILE_SHARE_ALL, None, OPEN_EXISTING, 0, None)
    if handle == wintypes.HANDLE(-1).value:
        raise ctypes.WinError(ctypes.get_last_error())
    return os.fdopen(msvcrt.open_osfhandle(handle, os.O_RDONLY), encoding="utf-8-sig")


def read_save(path):
    with open_shared(path) as f:
        text = f.read()
    return json.loads(text)


def load(path):
    d = read_save(path)
    ids = {}
    index_ids(d, ids)
    return d, ids


def newest_slot():
    def saved(slot):
        return read_save(os.path.join(slot, "Info.tnmt"))["Date"]
    return max((os.path.dirname(p) for p in glob.glob(os.path.join(SAVES, "*", "*", "Info.tnmt"))), key=saved)


def count(slots, ids, wanted):
    """Return ({item id: amount}, slots in use) for a storage's raw slot list."""
    amounts, used = {}, 0
    for s in slots:
        s = deref(s, ids) or {}
        s = deref(s.get("itemStack", s), ids) or {}
        item, n = s.get("itemId", -1), s.get("itemAmount", 0)
        if item == -1 or n <= 0:
            continue
        used += 1
        if item in wanted:
            amounts[item] = amounts.get(item, 0) + n
    return amounts, used


def scene_storages(d, ids):
    for e in d["Data"]:
        dd = deref(e.get("Data"), ids)
        if isinstance(dd, dict) and "storageSlots" in dd:
            yield e["GUID"], dd


def placed_storages(d, ids):
    """Map each placed furniture's Storage GUID to (room GUID, title, compartment number, compartments)."""
    placed = {}
    for e in d["Data"]:
        dd = e.get("Data")
        if not (isinstance(dd, dict) and dd.get("$type") == "FurnitureManager"):
            continue
        for p in dd.get("placedFurniture", []):
            parts = [g for g in p.get("gameObjectSubGUIDs") or [] if "_Storage_" in g]
            title = (deref(p.get("furniture"), ids) or {}).get("title") or "Furniture"
            for n, g in enumerate(parts, 1):
                placed[g] = (e["GUID"], title, n, len(parts))
    return placed


def add(rows, area, kind, sub, name, slots, used, amounts):
    rows.append({"area": area, "kind": kind, "sub": sub, "name": name, "slots": slots, "used": used, "items": amounts})


class Stock:
    """Every item in the containers the ledger counts, for the recipe page.

    Counted the way CraftingBaseHelperFunctions counts ingredients: a stack with ammo can stand
    for its ammo times its size, and a liquid container for the litres inside it.
    """

    def __init__(self):
        self.count, self.uses, self.litres, self.where = {}, {}, {}, {}

    def put(self, table, item, n, place):
        table[item] = table.get(item, 0) + n
        spots = self.where.setdefault(item, {})
        spots[place] = spots.get(place, 0) + n

    def add(self, place, slots, ids):
        for s in slots:
            s = deref(s, ids) or {}
            s = deref(s.get("itemStack", s), ids) or {}
            item, n = s.get("itemId", -1), s.get("itemAmount", 0)
            if item == -1 or n <= 0:
                continue
            meta = deref(s.get("_meta"), ids) or []
            if isinstance(meta, dict):
                meta = meta.get("$content", [])
            meta = [deref(m, ids) or {} for m in meta]
            ammo = next((m["ammo"] for m in meta if "maxAmmo" in m), None)
            liquid = next((m for m in meta if deref(m.get("liquidItem"), ids)), None)
            self.put(self.count, item, n, place)
            self.uses[item] = self.uses.get(item, 0) + (ammo * n if ammo is not None else n)
            if liquid:
                inside = deref(liquid["liquidItem"], ids)["ID"]
                self.put(self.litres, inside, liquid["liquidAmount"] * n, place)


def collect(slot, wanted, stock=None):
    rows, heater_fuel, resident_stock = [], 0, 0
    stock = stock or Stock()
    for path in sorted(glob.glob(os.path.join(slot, "*.tnmt"))):
        scene = os.path.basename(path)[:-5]
        if scene == "Info":
            continue
        d, ids = load(path)
        if scene == "Globals":
            by_guid = {e["GUID"]: e["Data"] for e in d["Data"]}
            inv = by_guid["Inventory"]
            for label, slots in (("Pockets", inv["_savingSlots"] + inv["_savingCharacterSlots"]),
                                 ("Backpack", by_guid["Backpack"]["storageSlots"]),
                                 ("Rucksack", by_guid["Rucksack"]["storageSlots"]),
                                 ("Cooler bag", by_guid["CoolerBag"]["storageSlots"])):
                amounts, used = count(slots, ids, wanted)
                stock.add(f"On you, {label}", slots, ids)
                if amounts:
                    add(rows, "On you", "carried", "", label, len(slots), used, amounts)
            continue
        placed = placed_storages(d, ids)
        for guid, dd in scene_storages(d, ids):
            slots = dd["storageSlots"]
            amounts, used = count(slots, ids, wanted)
            if guid in placed:
                room, title, part, parts = placed[guid]
                name = f"{title}, part {part}" if parts > 1 else title
                add(rows, ROOMS.get(room, scene), "placed", "", name, len(slots), used, amounts)
                stock.add(f"{ROOMS.get(room, scene)}, {title}", slots, ids)
            elif (scene, guid) in CONTRACTOR_CHESTS:
                area, sub, _ = CONTRACTOR_CHESTS[(scene, guid)]
                add(rows, area, "contractor", sub, "Resource chest", len(slots), used, amounts)
                stock.add(area, slots, ids)
            elif guid.startswith(OWN_APARTMENT):
                add(rows, "Your apartment", "built-in", "", "Built-in storage", len(slots), used, amounts)
                stock.add("Your apartment, built-in storage", slots, ids)
            elif guid.startswith(HEATER):
                heater_fuel += sum(amounts.values())
            elif RESIDENT_APARTMENT.match(guid):
                resident_stock += sum(amounts.values())
    return rows, heater_fuel, resident_stock


def number_duplicates(rows):
    """Tell apart identical furniture in one room, keeping the fullest first."""
    groups = {}
    for r in rows:
        groups.setdefault((r["area"], r["name"]), []).append(r)
    for (_, name), group in groups.items():
        if len(group) > 1:
            group.sort(key=lambda r: -sum(r["items"].values()))
            for n, r in enumerate(group, 1):
                r["name"] = f"{name} {n}"


def cost_scale(difficulty, setting):
    index = difficulty["currentDifficultyIndex"]
    custom = difficulty["customDifficultySettings"][setting]
    return COST_MULTIPLIERS[PRESET_COSTS[index] if index < len(PRESET_COSTS) else custom]


def chests_in(slot):
    """Map each contractor to the chest they draw from, if the player has been to its scene."""
    return {who: area for (scene, _), (area, _, who) in CONTRACTOR_CHESTS.items()
            if who and os.path.exists(os.path.join(slot, scene + ".tnmt"))}


def renovations(slot):
    """List every distinct renovation with how far along it is, following TenementUpgrade.

    A tier counts as done once the current tier reaches it (IsUpgraded). Tiers can be skipped,
    so only requiredUpgrades and a job already running in that slot hold one back.
    """
    plans = json.load(open(RENOVATIONS, encoding="utf-8"))
    d, ids = load(os.path.join(slot, "Globals.tnmt"))
    by_guid = {e["GUID"]: e["Data"] for e in d["Data"]}
    general, apartments = by_guid["TenementController"]["general"], by_guid["TenementController"]["apartments"]
    scale = cost_scale(by_guid["DifficultyController"], "renovationResourceCosts")
    price_scale = cost_scale(by_guid["DifficultyController"], "renovationCost")
    chests = chests_in(slot)
    tiers = {t["path"]: (track, t) for track in plans["tracks"] for t in track["tiers"]}

    def groups_of(apartment):
        kind = deref(apartment["type"], ids)
        return {(deref(kind.get(g), ids) or {}).get("addressableAssetPath") for g in APARTMENT_GROUPS}

    def reached(path, apartment):
        track, tier = tiers[path]
        owner = general if track["building"] else apartment
        return track["enum"].index(owner[track["field"]]) >= tier["tier"]

    def busy(track, apartment):
        owner = general if track["building"] else apartment
        field = track["field"].removeprefix("current")
        return owner.get(field[0].lower() + field[1:] + "Progress") is not None

    def name(path):
        track, tier = tiers[path]
        return f"{track['label']}: {tier['name']}"

    grouped = [(a, groups_of(a)) for a in apartments]
    rows = []
    for track in plans["tracks"]:
        owners = [None] if track["building"] else [a for a, groups in grouped if track["group"] in groups]
        for tier in track["tiers"]:
            # Most Luxurious tiers are placeholders, with no materials and no contractor.
            if not owners or tier["tier"] == 0 or not (tier["needs"] or tier["contractors"]):
                continue
            left = [a for a in owners if not reached(tier["path"], a)]
            unmet = {a and a["apartmentNumber"]: [r for r in tier["requires"] if not reached(r, a)] for a in left}
            ready = [a for a in left if not unmet[a and a["apartmentNumber"]] and not busy(track, a)]
            rows.append({"id": tier["path"], "label": name(tier["path"]), "heading": track["heading"],
                         "done": len(owners) - len(left), "total": len(owners), "ready": len(ready),
                         "first": int(tier["first"] * price_scale), "final": int(tier["final"] * price_scale),
                         "requires": [{"id": r, "label": name(r), "met": sum(reached(r, a) for a in left), "of": len(left)}
                                      for r in tier["requires"]],
                         "needs": {item: max(1, int(n * scale)) for item, n in tier["needs"].items()},
                         "contractors": [{"name": plans["contractors"][c], "chest": chests.get(c)} for c in tier["contractors"]]})
    # The save keeps RM in tenths (Money.RMoneyAmount divides by 10).
    money = {"oc": by_guid["Money"]["OCMoneyAmount"], "rm": by_guid["Money"]["rMoneyAmount"] / 10}
    return rows, [{"name": n, "chest": chests.get(c)} for c, n in plans["contractors"].items()], money


def character(slot):
    """Return (recipe ids found in books, magazines and mail, recipe ids crafted, {skill: level})."""
    d, ids = load(os.path.join(slot, "Globals.tnmt"))
    by_guid = {e["GUID"]: e["Data"] for e in d["Data"]}
    controller = by_guid["RecipeController"]

    def recipe_ids(field):
        return {r["ID"] for r in (deref(x, ids) for x in controller[field]) if r}

    skills = {s["Type"]: s["Skill"] for s in by_guid["PlayerStats"]["Skills"]}
    return recipe_ids("collectedRecipes"), recipe_ids("learnedRecipes"), skills


def build(slot):
    """Return the ledger and the recipe book for one save."""
    catalog = json.load(open(ITEMS, encoding="utf-8-sig"))
    resources = {i["ID"]: i["Title"] for i in catalog if RESOURCE_CATEGORY in i["Categories"]}
    plans, contractors, money = renovations(slot)
    titles = {i["ID"]: i["Title"] for i in catalog}
    # Upgrades require a few things the game doesn't put in that category.
    extras = {i: titles[i] for i in {int(n) for p in plans for n in p["needs"]} if i not in resources}
    stock = Stock()
    rows, heater_fuel, resident_stock = collect(slot, set(resources) | set(extras), stock)
    rows = [r for r in rows if r["items"] or r["slots"] >= ALWAYS_LIST_SLOTS]
    number_duplicates(rows)

    totals = {}
    for r in rows:
        r["total"] = sum(r["items"].values())
        area = totals.setdefault(r["area"], {"name": r["area"], "kind": r["kind"], "sub": r["sub"], "total": 0})
        area["total"] += r["total"]
    rank = {"placed": 0, "built-in": 0, "contractor": 1, "carried": 2}
    areas = sorted(totals.values(), key=lambda a: (rank[a["kind"]], -a["total"]))
    rows.sort(key=lambda r: (rank[r["kind"]], -totals[r["area"]]["total"], r["area"], -r["total"], r["name"]))

    items = [{"id": i, "name": n, "extra": False} for i, n in resources.items()]
    items += [{"id": i, "name": n, "extra": True} for i, n in extras.items()]
    for it in items:
        it["total"] = sum(r["items"].get(it["id"], 0) for r in rows)
    items.sort(key=lambda it: it["name"])

    info = read_save(os.path.join(slot, "Info.tnmt"))
    assert sum(it["total"] for it in items) == sum(a["total"] for a in areas)
    stamp = {"slot": info["SaveName"], "saved": info["Date"], "version": info["ApplicationVersion"]}
    ledger = {**stamp, "items": items, "areas": areas, "containers": rows, "renovations": plans,
              "contractors": contractors, "money": money, "heaterFuel": heater_fuel, "residentStock": resident_stock}
    return ledger, {**stamp, **recipe_book.build(catalog, stock, *character(slot))}


def settings():
    with open(CONFIG, encoding="utf-8") as f:
        return json.load(f)


def push(payload, name="ledger.json"):
    """Replace one of the site's data files on the server in one step, so a reader never sees half a file."""
    server = settings()
    remote = posixpath.join(posixpath.dirname(server["remote_ledger"]), name)
    command = f"cat > {remote}.tmp && mv {remote}.tmp {remote}"
    ssh = subprocess.Popen(["ssh", "-o", "BatchMode=yes", "-o", "ConnectTimeout=8", server["ssh_target"], command],
                           stdin=subprocess.PIPE, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE,
                           creationflags=subprocess.CREATE_NO_WINDOW)
    try:
        _, err = ssh.communicate(payload, timeout=PUSH_LIMIT)
    except subprocess.TimeoutExpired:
        subprocess.run(["taskkill", "/PID", str(ssh.pid), "/T", "/F"], capture_output=True,
                       creationflags=subprocess.CREATE_NO_WINDOW)
        ssh.wait()
        raise
    if ssh.returncode:
        raise RuntimeError(f"ssh exited {ssh.returncode}: {err.decode(errors='replace').strip()}")


def note(text):
    with open(LOG, "w", encoding="utf-8") as f:
        f.write(f"{time.strftime('%Y-%m-%d %H:%M:%S')} {text}\n")
    print(text)
    return text


def export(force):
    slot = newest_slot()
    saved = read_save(os.path.join(slot, "Info.tnmt"))["Date"]
    last = open(STATE, encoding="utf-8").read() if os.path.exists(STATE) else ""
    if saved == last and not force:
        return note(f"No newer save than {os.path.basename(slot)}, saved {saved[:10]} {saved[11:16]}.")
    written = max(os.path.getmtime(p) for p in glob.glob(os.path.join(slot, "*")))
    if time.time() - written < QUIET_SECONDS and not force:
        return note(STILL_SAVING)
    data, recipes = build(slot)
    # Recipes first: each page picks up its own file, and the ledger's arrival marks the save as sent.
    push(json.dumps(recipes, ensure_ascii=False, separators=(",", ":")).encode("utf-8"), "recipes.json")
    push(json.dumps(data, ensure_ascii=False, separators=(",", ":")).encode("utf-8"))
    with open(STATE, "w", encoding="utf-8") as f:
        f.write(saved)
    return note(f"Sent {data['slot']} {saved[:16]}: {sum(i['total'] for i in data['items'])} units, "
                + ", ".join(f"{a['name']} {a['total']}" for a in data["areas"]))


def checked(force=False):
    """Run one export. A failure is noted, not raised, so the next run can try again."""
    try:
        return export(force)
    except Exception:
        note("FAILED, will retry on the next run\n" + traceback.format_exc())
        return FAILED


def call(path, body=None, timeout=10):
    # No proxy: this is a LAN address, and urllib would otherwise pick up the Windows settings.
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    data = None if body is None else json.dumps(body).encode("utf-8")
    site = settings()["site_url"].rstrip("/")
    with opener.open(urllib.request.Request(site + path, data=data), timeout=timeout) as response:
        return json.load(response)


def answer(check_id):
    """Run a check the page asked for, waiting out a save in progress."""
    def report(phase, message):
        call("/api/report", {"id": check_id, "phase": phase, "message": message})

    report("running", "Checking the newest save")
    result = checked()
    for _ in range(SAVING_RETRIES):
        if result != STILL_SAVING:
            break
        report("running", STILL_SAVING)
        time.sleep(SAVING_RETRY_SECONDS)
        result = checked()
    report("done", result)


def watch():
    checked()
    end = time.monotonic() + WATCH_SECONDS
    after = -1
    while time.monotonic() < end:
        try:
            state = call(f"/api/wait?after={after}", timeout=WAIT_LIMIT)
            after = state["id"]
            if state["phase"] == "requested":
                answer(after)
        except OSError:  # the server is off or restarting
            time.sleep(5)


def main():
    os.makedirs(STATE_DIR, exist_ok=True)
    if not os.path.exists(CONFIG):
        note("Copy config.example.json to config.json and fill it in.")
        sys.exit(1)
    if "--watch" in sys.argv:
        watch()
    elif checked("--force" in sys.argv) == FAILED:
        sys.exit(1)


if __name__ == "__main__":
    main()
