# Notification Log

A BepInEx mod for Obenseuer that adds a Log tab to the game menu, listing past
notifications newest first with the in-game time, so you can read one you
missed. Each save you load from the menu is marked in the list.

The history is kept in
`BepInEx\config\berkyjay.obenseuer.notificationlog.history.tsv`, not in your
saves. It is shared across save slots, and loading an older save leaves later
entries in place below the marker.

The tab takes over a menu page the game ships but doesn't use, so it could clash
if the developers start using that page.

## Install

You need BepInEx 6 in the game folder first; see
[Installing BepInEx](../README.md#installing-bepinex).

1. Download `NotificationLog-<version>.zip` from this mod's
   [release](https://github.com/berkyjay/obenseuer-mods/releases?q=notification-log).
2. Unzip it into the game folder, so the DLL ends up in
   `BepInEx\plugins\NotificationLog\`.

To remove it, delete `BepInEx\plugins\NotificationLog\`. The history file stays
in `BepInEx\config\` until you delete it.

## Settings

`MaxEntries` (default 500) is in
`BepInEx\config\berkyjay.obenseuer.notificationlog.cfg`.
