# Sweep Transfer

A BepInEx mod for Obenseuer that moves items between your inventory and an open
container by sweeping the mouse over them.

Hold **Right Alt**, press the left mouse button and move the cursor across the
slots. Each item the cursor passes over moves to the other side, the same as if
you'd double-clicked it. Whole stacks move.

## How a sweep behaves

- It moves items only from the side it started on. Items that land in the other
  panel aren't swept back if the cursor crosses it in the same stroke.
- It stops at the first item that won't move, usually because the other side is
  full. Release the button and start a new sweep to carry on.
- Letting go of Alt stops the sweep. With Alt held, pressing on an item doesn't
  pick it up.
- It only moves items between your inventory and an ordinary container. It does
  nothing with a trader open (moving there buys and sells), at the bottle
  recycler (which would destroy what you sweep), in liquid storage, or when no
  container is open. Equipment slots are never touched.

## Install

You need BepInEx 6 in the game folder first; see
[Installing BepInEx](../README.md#installing-bepinex).

1. Download `SweepTransfer-<version>.zip` from this mod's
   [release](https://github.com/berkyjay/obenseuer-mods/releases?q=sweep-transfer).
2. Unzip it into the game folder, so the DLL ends up in
   `BepInEx\plugins\SweepTransfer\`.

To remove it, delete `BepInEx\plugins\SweepTransfer\`.

## Settings

In `BepInEx\config\berkyjay.obenseuer.sweeptransfer.cfg`, read when the game
starts:

| Setting | Default | What it does |
|---|---|---|
| `SweepKey` | `RightAlt` | The key to hold while sweeping. Any Unity key name works; mouse side buttons are `Mouse3` and `Mouse4`. Left Shift and Left Control are already used by the game. |
| `ToggleMode` | `false` | `true` makes `SweepKey` a switch: press it once to turn sweeping on (a notification says so), and again to turn it off. While it's on, every left-button press sweeps. It turns off when the menu closes. |
