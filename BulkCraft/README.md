# Bulk Craft

A BepInEx mod for Obenseuer that cuts down the clicking for two everyday jobs:
crafting instant recipes, and filling or emptying liquid containers.

## Craft all

Recipes that take no time to make (Rollie, Mushroom Rollie, the e-cigarette
refills, the plated meals) get a **Craft all** button beside Craft. It keeps
crafting the selected recipe until the station runs out of ingredients. The
button shows the count so far; click it again to stop.

- Each craft is a normal craft. Failure chance, quality and skill gain are rolled
  for every one, exactly as if you'd clicked Craft that many times.
- Output goes where the game puts it: into the station, then into your
  inventory. Craft all stops before anything would be dropped on the floor.
- It stops if you close the station, pick another recipe, or the station stops
  crafting (no power, or every crafting slot busy).
- Recipes that take time don't get the button.

## Bulk Fill and Bulk Unfill

In liquid storage (the panel with Fill and Empty buttons), select a container in
your inventory and two more buttons appear under Fill and Empty:

- **Bulk Fill** fills every stack of that kind of container you're carrying, one
  stack after another. Empty and part-filled ones both count.
- **Bulk Unfill** empties every stack of that kind into the storage.

Empty and filled bottles of one kind count as the same container, so selecting
an empty 10 litre bottle fills all your 10 litre bottles that still have room.
The liquid moves at the game's normal speed, using the game's own Fill and Empty,
so mixing quality, spoilage and taking from someone else's tank work as they do
by hand.

A run stops when there's nothing left to fill or empty, when the storage runs
dry or fills up, when you select another item, or when you close the storage. The
button shows the progress; click it again to stop. A notification says how many
stacks were done, and why it stopped early.

## Install

You need BepInEx 6 in the game folder first; see
[Installing BepInEx](../README.md#installing-bepinex).

1. Download `BulkCraft-<version>.zip` from this mod's
   [release](https://github.com/berkyjay/obenseuer-mods/releases?q=bulk-craft).
2. Unzip it into the game folder, so the DLL ends up in
   `BepInEx\plugins\BulkCraft\`.

To remove it, delete `BepInEx\plugins\BulkCraft\`.

It has no settings.
