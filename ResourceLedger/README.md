# Resource Ledger

A web page for Obenseuer showing where your tenement resources are and what each
renovation needs.

This one isn't a BepInEx mod. It's a small web page you open on a second screen
or a phone. It reads your newest save, never changes it, and adds nothing to the
game.

Download `ResourceLedger-<version>.zip` from its
[release](https://github.com/berkyjay/obenseuer-mods/releases?q=resource-ledger)
and unzip it anywhere on the PC with the game, or use the files in this folder.

- **Where it is:** every tenement resource you've collected, listed by item and by
  container, across your placed storage, contractor chests and what you carry.
- **Renovations:** a drop-down lists each unique renovation, with finished ones
  greyed out. Picking one moves its materials to the top of the list. Red means
  you own too little. Amber means you own enough but need to move it, and it says
  how much and where. A contractor only uses their own chest and what you carry,
  so stock elsewhere doesn't count until you move it.
- **Contractors and costs:** the contractors who can do the selected renovation
  are highlighted. Its OC and RM payments and required upgrades are shown too,
  in red when you can't cover them yet. Click a required upgrade to go to it, and
  use your browser's Back button to return.
- **Recipe book:** a second page, linked from the first, with every recipe in the
  game grouped by station. It shows which ones your character knows, which you can
  make right now with what you own and how many times, and which ingredient you're
  short of. The Ingredients view lists each item with where you keep it, the recipes
  that use it and the recipes that make it.
- **Updates:** new saves appear within about five minutes, or right away with
  **Check for a new save**.

## Setup

It runs on two machines: the Windows PC with the game, and an always-on Linux
machine with Python 3 that the PC can reach over ssh with a key. The page has no
login, so keep it on your home network.

1. Copy `server.py`, `site\` and `deploy\` to a folder on the
   Linux machine, then run `deploy/install.sh` there. It uses sudo to install a
   service on port 8090.
2. On the PC, copy `config.example.json` to `config.json` and fill in your
   server's details.
3. Build the renovation data from your own copy of the game. You need Python, the
   `UnityPy` package, [ilspycmd](https://www.nuget.org/packages/ilspycmd) and about
   6 GB of free memory:

   ```
   python scan_renovations.py renovations.json
   ```

4. Register the background task, which opens no windows:

   ```
   powershell -File deploy\install_task.ps1
   ```

5. Open `http://your-server:8090`, or `http://your-server:8090/recipes.html` for
   the recipe book.

If the page stops updating, read
`%LOCALAPPDATA%\obenseuer-ledger\last_run.txt` on the PC. Running
`python export_ledger.py --force` sends the newest save again. After a game
update, repeat step 3; its lists of rooms and contractor chests may also need
updating.
