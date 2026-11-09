# Obenseuer Mods

Unofficial BepInEx mods for the game Obenseuer. Free for anyone to use. These
mods are not affiliated with or supported by Loiste Interactive, so please don't
report problems with them to the developers.

Each mod works on its own: install only the ones you want. Click a mod's name for
what it does, or Download for its zip.

| Mod | Version | What it does | |
|---|---|---|---|
| [Harvest All Button](HarvestAllButton/README.md) | 0.2.3 | One-click Harvest and Replant on every planter you own | [Download](https://github.com/berkyjay/obenseuer-mods/releases/tag/harvest-all-button-v0.2.3) |
| [Notification Log](NotificationLog/README.md) | 0.1.0 | A Log tab in the game menu that keeps past notifications | [Download](https://github.com/berkyjay/obenseuer-mods/releases/tag/notification-log-v0.1.0) |
| [Status Icon Timing](StatusIconTiming/README.md) | 0.1.1 | Need icons appear later, at 66 out of 100 instead of 33 | [Download](https://github.com/berkyjay/obenseuer-mods/releases/tag/status-icon-timing-v0.1.1) |
| [Trash Bin Emptier](TrashBinEmptier/README.md) | 0.1.0 (pre-release) | Placed Trash Bins empty themselves once a day; Green Dumpsters always keep what's in them | [Download](https://github.com/berkyjay/obenseuer-mods/releases/tag/trash-bin-emptier-v0.1.0) |
| [Resource Ledger](ResourceLedger/README.md) | 0.1.0 | A web page showing where your tenement resources are and what each renovation needs, plus a recipe book. Not a BepInEx mod: it needs a small home server | [Download](https://github.com/berkyjay/obenseuer-mods/releases/tag/resource-ledger-v0.1.0) |

## Installing BepInEx

The mods need BepInEx 6, installed once:

1. Download the Unity Mono x64 build of BepInEx 6 from the
   [bleeding edge builds](https://builds.bepinex.dev/projects/bepinex_be) and
   unzip it into the game folder.
2. Run the game once so BepInEx creates its folders, then close it.

Then unzip each mod you want into the game folder. Each zip holds
`BepInEx\plugins\<ModName>\`, so it lands in the right place. To remove a mod,
delete its folder from `BepInEx\plugins\`.

## Compatibility

The mods are built against game version 0.5.17 and BepInEx 6 be.791 (Unity Mono).
Notification Log and Status Icon Timing have been played on 0.5.17. Harvest All
Button has been played on 0.5.16 and its code checked against 0.5.17. Trash Bin
Emptier is new and hasn't had much play yet.

A game update can break any mod. If something stops working, look in
`BepInEx\LogOutput.log` and remove the mod until it's fixed. Keep backups of your
saves.

## Building from source

You need the [.NET SDK](https://dotnet.microsoft.com/download) and a copy of the
game. From the root of this repository:

```
dotnet build HarvestAllButton\HarvestAllButton.csproj -c Release
```

The DLL is written to `HarvestAllButton\bin\Release\`.

The build expects the game in Steam's default folder,
`C:\Program Files (x86)\Steam\steamapps\common\Obenseuer`. If yours is somewhere
else, add `-p:GameDir="D:\Your\Path\Obenseuer"` to the build command.

`tools\package.ps1 <ModName>` builds a mod and makes its release zip in `dist\`.
For Resource Ledger it zips the committed files of that folder instead.
