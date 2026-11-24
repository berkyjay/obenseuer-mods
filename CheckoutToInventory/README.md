# Checkout To Inventory

A BepInEx mod for Obenseuer. When you pay at a shop checkout with a conveyor
belt, such as O-Market, your goods go straight into your inventory. You no
longer have to click each item off the belt.

The mod uses the game's own pickup, so bought goods aren't counted as stolen,
and partial stacks fill up the way they would by hand. Anything that doesn't fit
in your main inventory stays on the belt, and a notification says how many
items were left. Goods aren't put in your backpack, the same as picking them up
by hand.

O-Market's Plastic Bags dispenser still works. If you put bags through the
checkout, the filled bags go into your inventory too. Each bag holds 15 items in
one inventory slot, which helps when you buy more than fits loose. Use Unpack on
a bag to take the items out.

At shops that pack your goods into a single package, such as Ankea, the
package stays on the counter as before. Anything they set out on its own goes
into your inventory.

This mod is new and hasn't had much play yet, so it's marked as a pre-release.
Keep backups of your saves.

## Install

You need BepInEx 6 in the game folder first; see
[Installing BepInEx](../README.md#installing-bepinex).

1. Download `CheckoutToInventory-<version>.zip` from this mod's
   [release](https://github.com/berkyjay/obenseuer-mods/releases?q=checkout-to-inventory).
2. Unzip it into the game folder, so the DLL ends up in
   `BepInEx\plugins\CheckoutToInventory\`.

To remove it, delete `BepInEx\plugins\CheckoutToInventory\`.

## Settings

`BepInEx\config\berkyjay.obenseuer.checkouttoinventory.cfg` has `Enabled`, to
turn the mod off without removing it, and `Notify`, for the notification about
goods left on the belt.
