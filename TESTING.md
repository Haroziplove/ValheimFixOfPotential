# Testing SafeRefinement

Use a client that has BepInEx and this plugin. The dedicated server can be vanilla. A client without the plugin still breaks items.

Do not run `devcommands`, and do not use the console `spawn` command. `devcommands` marks the character as having used cheats. `spawn` also marks the spawned piece and any item you pick up as cheated, and a later refinement copies that flag onto the result. Use the forge and idols directly from play.

## 1. Install

Follow the install steps in `README.md`. Start the client and confirm `BepInEx\LogOutput.log` contains `patched InventoryGui.DoCrafting` and no patch error.

## 2. Setup

On a character that has not used cheat commands:

1. Go to a Forge of Potential already placed in the world.
2. Bring a weapon or armor piece you crafted or looted. Damage it first so durability is not full. Note its quality, durability, variant, and crafter name.
3. Bring idols of the matching kind (Battle for weapons, Protection for armor) and tier. About 40 attempts is enough to see the success rate. Fewer still shows whether a failure keeps the item.

## 3. Attempts

Refine with `FailureOutcome=KeepItem`.

- Successes reach the next level. Across a few dozen attempts, expect roughly 65% successes.
- Failures leave that same item in the inventory: same quality, durability, variant, and crafter. Custom data stays.
- One idol is consumed on every attempt, success or failure.
- Failed attempts do not return craft materials.
- The center text on failure says the item was kept.
- The item is not duplicated in the inventory or on the ground.

## 4. Save and load

Log out, restart the server, and log back in. The kept item and any upgraded item are still there, once each.

## 5. Second player

Two clients at the same forge.

- The client with the plugin keeps failed items, including when that player did not place the forge.
- The client without the plugin still loses items on failure.

## 6. Vanilla switch

Set `General.FailureOutcome` to `Vanilla` and save the cfg. Do not restart.

- The next failed attempt destroys the item.
- Materials come back (35% of the craft cost plus the cost of the current level, rounded up).
- Set it back to `KeepItem`, save, and confirm the next failure keeps the item.

Optional: set `Downgrade` and fail once. The item comes back one level lower and the idol is consumed.

## 7. Log

With `Debug.LogAttempts=true`, each attempt in `BepInEx\LogOutput.log` has the player name, item, level, roll, and `Success`, `KeepItem`, `Downgrade`, or `Vanilla`.

Startup should not show a missing-method error. A dedicated server that also has the DLL logs that the server copy does not protect players.
