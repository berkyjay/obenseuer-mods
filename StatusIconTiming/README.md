# Status Icon Timing

A BepInEx mod for Obenseuer that makes need icons appear later, at 66 out of 100
instead of 33.

The game shows a need's icon once it's a third of the way to 100, long before it
does anything to you: hunger and thirst only hurt at 100, and you pass out from
tiredness at 100. This mod moves that point to 66 by default.

It covers hunger, thirst, tiredness, toilet, depression and the alcohol,
mushroom, smoking and gambling needs. Hygiene counts down, so its icon appears
at 100 minus the setting (34 by default, instead of 50). Health, bleeding and
the duck need keep the game's own rules. Icons still pulse at 90.

If a game update changes the code this mod patches, the mod logs a warning and
leaves the icons as the game has them.

## Install

You need BepInEx 6 in the game folder first; see
[Installing BepInEx](../README.md#installing-bepinex).

1. Download `StatusIconTiming-<version>.zip` from this mod's
   [release](https://github.com/berkyjay/obenseuer-mods/releases?q=status-icon-timing).
2. Unzip it into the game folder, so the DLL ends up in
   `BepInEx\plugins\StatusIconTiming\`.

To remove it, delete `BepInEx\plugins\StatusIconTiming\`.

## Settings

The value is `AppearAt` in
`BepInEx\config\berkyjay.obenseuer.statusicontiming.cfg`. It's read when the
game starts.
