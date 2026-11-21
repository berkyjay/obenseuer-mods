# Standby Power

A BepInEx mod for Obenseuer. An electric crafting station that is switched on
but not making anything draws only standby power, 5% of its full power by
default, instead of its full power.

In the unmodded game most electric stations (the Electric Kiln, Chemical Lab,
3D Printer, Loom and others) draw their full power the whole time they're
switched on, whether or not anything is in progress. It's easy to leave one on
after walking away from a craft, and the tenement electricity bill counts every
hour of it, including while you sleep or are in another area. A few stations
already have a standby draw in the game; those keep the game's value.

The station goes back to full power when a craft starts and drops to standby
the moment the last one finishes. Before a craft starts, the game checks that
the grid has room for the jump to full power, as it already does for its own
standby stations.

Only electric crafting stations are affected. Fuel-burning stations, planters
with lamps, fridges, freezers and lights are unchanged.

This mod is new and hasn't had much play yet. Keep backups of your saves.

## Install

You need BepInEx 6 in the game folder first; see
[Installing BepInEx](../README.md#installing-bepinex).

1. Download `StandbyPower-<version>.zip` from this mod's
   [release](https://github.com/berkyjay/obenseuer-mods/releases?q=standby-power).
2. Unzip it into the game folder, so the DLL ends up in
   `BepInEx\plugins\StandbyPower\`.

To remove it, delete `BepInEx\plugins\StandbyPower\`. A station that was on
standby when you last saved may stay at standby until its next craft starts.

## Settings

The standby percentage (0 to 100) is in
`BepInEx\config\berkyjay.obenseuer.standbypower.cfg`. The file appears after
the first game start with the mod installed.
