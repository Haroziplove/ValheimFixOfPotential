# ValheimFixOfPotential

Client mod for the Forge of Potential. A failed refinement keeps the item. The success chance stays the same as vanilla (65% per idol).

Install it on every player who should be protected. The dedicated server does not roll the refinement, so a copy of the DLL on the server does nothing by itself.

## Install

You need the Valheim **game** folder, the one that contains `valheim.exe`. On a default Steam install that is:

`Steam\steamapps\common\Valheim`

### 1. Install BepInEx

If `Valheim\BepInEx` is already there, skip to step 2.

1. Download [BepInExPack for Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) (the manual zip, not the Thunderstore app, is enough).
2. Extract the zip.
3. Copy everything inside its `BepInExPack_Valheim` folder into the Valheim folder. `winhttp.dll` and the `BepInEx` folder should sit next to `valheim.exe`.
4. Start Valheim once, then quit. That creates `BepInEx\config` and `BepInEx\plugins`.

### 2. Install this mod

1. Copy `SafeRefinement.dll` to:

   `Valheim\BepInEx\plugins\SafeRefinement\SafeRefinement.dll`

   Create the `SafeRefinement` folder if it is not there.
2. Start Valheim and join your world.
3. Open `Valheim\BepInEx\LogOutput.log`. It should contain:

   `SafeRefinement 1.0.0 patched InventoryGui.DoCrafting`

   If that line is missing, the DLL is in the wrong folder or BepInEx did not start.

### 3. Server

Players connect with a normal Valheim client that has the steps above. The dedicated server can stay vanilla. Putting the DLL only in the server's `BepInEx\plugins` folder does not change refinement.

Each player who wants failed items kept needs the mod on their own client. A player without it still loses items.

## What it does

The forge still uses one vanilla roll. When that roll would have broken the item, the item stays in your inventory at the same quality, durability, variant, and crafter, including custom data. The idol is still used up. Materials are not refunded. The message says the refinement failed and the item was kept.

A successful refinement still builds a new item the way the game does, including a fresh durability value.

## Config

After the first launch, edit:

`Valheim\BepInEx\config\com.tomas.saferefinement.cfg`

Save the file. The game picks it up without a restart.

| Key | Default | Meaning |
| --- | --- | --- |
| `General.Enabled` | `true` | Turn the mod off without deleting the DLL. |
| `General.FailureOutcome` | `KeepItem` | `KeepItem`, `Downgrade`, or `Vanilla`. |
| `Debug.LogAttempts` | `false` | Write each attempt to `LogOutput.log`. |

`Downgrade` drops the item one level, which is a branch the idols never reach on their own. `Vanilla` breaks the item and returns the usual material refund.

## Try it

Use a Forge of Potential that is already in the world, and idols you picked up in play. Do not turn on `devcommands` and do not use `spawn`. Both of those mark the character, and `spawn` also marks the item, as cheated. See `TESTING.md` for the full check.

## Build from source

Copy `Environment.props.example` to `Environment.props` and set `VALHEIM_DIR` to your Valheim folder. Then:

```
dotnet build SafeRefinement.csproj -c Release
```

The build copies the DLL into `BepInEx\plugins\SafeRefinement\`.
