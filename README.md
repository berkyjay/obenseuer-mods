# Obenseuer Mods

Unofficial BepInEx mods for the game Obenseuer. Free for anyone to use. These
mods are not affiliated with or supported by Loiste Interactive, so please don't
report problems with them to the developers.

| Mod | Version | What it does |
|---|---|---|
| Harvest All Button | 0.2.3 | One-click Harvest and Replant on every planter you own |
| Notification Log | 0.1.0 | A Log tab in the game menu that keeps past notifications |
| Resource Ledger | 0.1.0 | A web page showing where your tenement resources are and what each renovation needs |
| Status Icon Timing | 0.1.1 | Need icons appear later, at 66 out of 100 instead of 33 |
| Trash Bin Emptier | 0.1.0 | Placed Trash Bins empty themselves once a day; Green Dumpsters always keep what's in them |

## Harvest All Button

The game's Harvest button uproots everything on a station, ripe or not, and it
can't be seen on stations that show modifiers, such as the Terrarium. This mod
moves it to the bottom of the plant column and adds three more buttons there.

- **Harvest** picks only what's ready: ripe fruit, fully grown plants and dead
  plants. Items go to your inventory, then to the planter if the inventory is
  full.
- **Replant** fills the empty slots. Each slot gets the crop it last held if you
  have items for it, then the recipe selected in the list. If nothing is
  selected and only one crop can be planted, it uses that one.
- **Use** sets which items Replant uses up, such as true seeds or the lowest
  quality you have. Right-click to step backwards.
- **Time per slot** sets the in-game seconds each slot takes. The default is
  instant. With a time set, the panel has to stay open and the clock speeds up
  on long jobs. Right-click to step backwards.

Clicking Harvest or Replant again while a job runs stops it. Slots already done
stay done.

The buttons work on planters you own. Other people's planters and animal
stations keep the game's own behaviour.

Settings are stored in `BepInEx\config\berkyjay.obenseuer.harvestallbutton.cfg`.

## Notification Log

Adds a Log tab to the game menu that lists notifications newest first, with the
in-game time, so you can read one you missed. Each save you load from the menu
is marked in the list.

The history is kept in
`BepInEx\config\berkyjay.obenseuer.notificationlog.history.tsv`, not in your saves.
It is shared across save slots, and loading an older save leaves later entries
in place below the marker. `MaxEntries` (default 500) is in
`BepInEx\config\berkyjay.obenseuer.notificationlog.cfg`.

The tab takes over a menu page the game ships but doesn't use, so it could clash
if the developers start using that page.

## Trash Bin Emptier

Every Trash Bin you place from the build menu deletes its contents once a day,
at 06:00 in-game, wherever it stands. If you were in another area at 06:00, the
bin is emptied when you next enter its area. A bin you have open is emptied when
you close it. A notification says how many stacks were thrown away.

In the unmodded game, a few pieces of placed furniture (Trash Bin, Green Dumpster,
Beer Crate and the mailboxes) empty and refill like loot containers when placed on
the stairs or outside. That stops after the area reloads, because the game doesn't
save it. With this mod the Trash Bin follows the daily rule instead, and the Green
Dumpster never empties or refills. The Beer Crate and mailboxes are left as they are.

The hour, the furniture that gets emptied (by build-menu name) and the furniture
that always keeps its contents are in
`BepInEx\config\berkyjay.obenseuer.trashbinemptier.cfg`.

## Status Icon Timing

The game shows a need's icon once it's a third of the way to 100, long before it
does anything to you: hunger and thirst only hurt at 100, and you pass out from
tiredness at 100. This mod moves that point to 66 by default.

It covers hunger, thirst, tiredness, toilet, depression and the alcohol,
mushroom, smoking and gambling needs. Hygiene counts down, so its icon appears
at 100 minus the setting (34 by default, instead of 50). Health, bleeding and
the duck need keep the game's own rules. Icons still pulse at 90.

The value is `AppearAt` in `BepInEx\config\berkyjay.obenseuer.statusicontiming.cfg`.
It's read when the game starts. If a game update changes the code this mod
patches, the mod logs a warning and leaves the icons as the game has them.

## Resource Ledger

This one isn't a BepInEx mod. It's a small web page you open on a second screen
or a phone. It reads your newest save, never changes it, and adds nothing to the
game.

- **Where it is:** every tenement resource you've collected, listed by item and by
  container, across your placed storage, contractor chests and what you carry.
- **Renovations:** a drop-down lists each unique renovation, with finished ones
  greyed out. Picking one moves its materials to the top of the list. Red means
  you own too little. Amber means you own enough but need to move it, and it says
  how much and where. A contractor only uses their own chest and what you carry,
  so stock elsewhere doesn't count until you move it.
- **Contractors and costs:** the contractors who can do the selected renovation
  are highlighted. Its OC and RM payments and required upgrades are shown too,
  in red when you can't cover them yet. Click a required upgrade to go to it, and
  use your browser's Back button to return.
- **Recipe book:** a second page, linked from the first, with every recipe in the
  game grouped by station. It shows which ones your character knows, which you can
  make right now with what you own and how many times, and which ingredient you're
  short of. The Ingredients view lists each item with where you keep it, the recipes
  that use it and the recipes that make it.
- **Updates:** new saves appear within about five minutes, or right away with
  **Check for a new save**.

It runs on two machines: the Windows PC with the game, and an always-on Linux
machine with Python 3 that the PC can reach over ssh with a key. The page has no
login, so keep it on your home network.

1. Copy `server.py`, `site\` and `deploy\` from `ResourceLedger\` to a folder on
   the Linux machine, then run `deploy/install.sh` there. It uses sudo to install
   a service on port 8090.
2. On the PC, copy `config.example.json` to `config.json` and fill in your
   server's details.
3. Build the renovation data from your own copy of the game. You need Python, the
   `UnityPy` package, [ilspycmd](https://www.nuget.org/packages/ilspycmd) and about
   6 GB of free memory:

   ```
   python scan_renovations.py renovations.json
   ```

4. Register the background task, which opens no windows:

   ```
   powershell -File deploy\install_task.ps1
   ```

5. Open `http://your-server:8090`, or `http://your-server:8090/recipes.html` for
   the recipe book.

If the page stops updating, read
`%LOCALAPPDATA%\obenseuer-ledger\last_run.txt` on the PC. Running
`python export_ledger.py --force` sends the newest save again. After a game
update, repeat step 3.

## Compatibility

The mods are built against game version 0.5.17 and BepInEx 6 be.791 (Unity Mono).
Notification Log has been played on 0.5.17. Harvest All Button has been played on
0.5.16 and its code checked against 0.5.17. Resource Ledger is built for 0.5.17, and
its lists of rooms and contractor chests may need updating after a game update.

A game update can break any mod. If something stops working, look in
`BepInEx\LogOutput.log` and remove the mod until it's fixed. Keep backups of your
saves.

## Install

No prebuilt DLLs are provided, so you build the mods yourself. That needs the
[.NET SDK](https://dotnet.microsoft.com/download) and a copy of the game.

1. Download the Unity Mono x64 build of BepInEx 6 from the
   [bleeding edge builds](https://builds.bepinex.dev/projects/bepinex_be) and
   unzip it into the game folder.
2. Run the game once so BepInEx creates its folders, then close it.
3. Build the mod you want from the root of this repository:

   ```
   dotnet build HarvestAllButton\HarvestAllButton.csproj -c Release
   ```

4. Copy the DLL from `HarvestAllButton\bin\Release\` to `BepInEx\plugins\` in the
   game folder.

To remove a mod, delete its DLL from `BepInEx\plugins\`.

The build expects the game in Steam's default folder,
`C:\Program Files (x86)\Steam\steamapps\common\Obenseuer`. If yours is somewhere
else, add `-p:GameDir="D:\Your\Path\Obenseuer"` to the build command.
