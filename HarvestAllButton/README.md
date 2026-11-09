# Harvest All Button

A BepInEx mod for Obenseuer that adds one-click Harvest and Replant to every
planter you own.

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

## Install

You need BepInEx 6 in the game folder first; see
[Installing BepInEx](../README.md#installing-bepinex).

1. Download `HarvestAllButton-<version>.zip` from this mod's
   [release](https://github.com/berkyjay/obenseuer-mods/releases?q=harvest-all-button).
2. Unzip it into the game folder, so the DLL ends up in
   `BepInEx\plugins\HarvestAllButton\`.

To remove it, delete `BepInEx\plugins\HarvestAllButton\`.

## Settings

The Use and Time per slot choices are saved in
`BepInEx\config\berkyjay.obenseuer.harvestallbutton.cfg`.
