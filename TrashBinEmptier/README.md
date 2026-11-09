# Trash Bin Emptier

A BepInEx mod for Obenseuer. Every Trash Bin you place from the build menu
deletes its contents once a day, at 06:00 in-game, wherever it stands, and Green
Dumpsters always keep what's in them.

If you were in another area at 06:00, the bin is emptied when you next enter its
area. A bin you have open is emptied when you close it. A notification says how
many stacks were thrown away.

In the unmodded game, a few pieces of placed furniture (Trash Bin, Green
Dumpster, Beer Crate and the mailboxes) empty and refill like loot containers
when placed on the stairs or outside. That stops after the area reloads, because
the game doesn't save it. With this mod the Trash Bin follows the daily rule
instead, and the Green Dumpster never empties or refills. The Beer Crate and
mailboxes are left as they are.

This mod is new and hasn't had much play yet, so it's marked as a pre-release.
Keep backups of your saves.

## Install

You need BepInEx 6 in the game folder first; see
[Installing BepInEx](../README.md#installing-bepinex).

1. Download `TrashBinEmptier-<version>.zip` from this mod's
   [release](https://github.com/berkyjay/obenseuer-mods/releases?q=trash-bin-emptier).
2. Unzip it into the game folder, so the DLL ends up in
   `BepInEx\plugins\TrashBinEmptier\`.

To remove it, delete `BepInEx\plugins\TrashBinEmptier\`.

## Settings

The hour, the furniture that gets emptied (by build-menu name) and the furniture
that always keeps its contents are in
`BepInEx\config\berkyjay.obenseuer.trashbinemptier.cfg`.
