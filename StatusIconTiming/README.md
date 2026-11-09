# Status Icon Timing

A BepInEx mod for Obenseuer that makes need icons appear later, at 66 out of 100
instead of 33.

The game shows a need's icon once it's a third of the way to 100, long before it
does anything to you: hunger and thirst only hurt at 100, and you pass out from
tiredness at 100. This mod moves that point to 66 by default.

## Which icons change

| Icon | Unmodded game shows it at | With this mod (default) |
|---|---|---|
| Hunger, thirst, tiredness | 33 or more | 66 or more |
| Toilet | 33 or more, bowel or bladder | 66 or more |
| Depression | 33 or more | 66 or more |
| Alcohol, mushroom, smoking and gambling needs | 33 or more | 66 or more |
| Hygiene (counts down) | 50 or less | 34 or less (100 minus the setting) |
| Health (counts down) | 66 or less | Unchanged |
| Bleeding | Any bleeding | Unchanged |
| Duck need | 60 or more | Unchanged |

Icons still pulse at 90, as in the game.

### Why health, bleeding and the duck need are left alone

- **Health:** losing health is worth knowing about early, and the game already
  waits until you've lost a third of it before showing the icon.
- **Bleeding:** a bleed needs treating straight away, so its icon still appears
  at the first drop.
- **Duck need:** its icon already appears at the moment it starts to hurt you.
  At 60 it begins raising your depression, so there's nothing to move.

The `AppearAt` setting doesn't affect these three, and this version has no
setting for them.

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
