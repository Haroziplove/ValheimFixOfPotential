# Notes for the next Valheim patch

Verified against **Valheim 1.0.17** (`Version.CurrentVersion = 1.0.17`).

- Client assembly: `valheim_Data/Managed/assembly_valheim.dll`
- Dedicated server assembly (app 896660): `valheim_server_Data/Managed/assembly_valheim.dll`
- Those two files differ. `InventoryGui` decompiles to the same method. On the server build, `ZNet.IsDedicated()` returns true. On the client build it returns false.

## Patched method

`InventoryGui.DoCrafting(Player)`

The refinement roll is the only `UnityEngine.Random.Range(float, float)` in that method:

```
float roll = UnityEngine.Random.Range(0f, 1f);
if (idol.m_shared.m_upgradeChance >= roll) success
else if (idol.m_shared.m_breakChance >= 1f - roll) break
else downgrade
```

The patch does not replace that call. It replaces the body of the break branch (the block that logs `Upgrader <color=red>BROKE</color> ...` and refunds `m_recover` materials). Success and the vanilla downgrade branch are left as compiled.

The item is removed from the player inventory before the roll (`UnequipItem` then `Inventory.RemoveItem`). On `KeepItem`, the same `ItemDrop.ItemData` instance is added back with `Inventory.AddItem(item, gridPos)`, so quality, durability, variant, crafter id, crafter name, custom data, world level, and picked-up state stay on that object.

## Content checked beside the DLL

- Location `Assets/world/Locations/Mountains/AncientUpgradeStation.prefab` contains piece `UpgradeStation`.
- `CraftingStation.m_upgrader` is true. `m_name` is `$piece_upgradestation` ("Forge of Potential"). There is no container.
- All 16 idol prefabs `Upgrader0`–`Upgrader7` × Weapon/Armor have upgrade chance 0.65, break chance 1, and `m_breakReturnIngreientsAmount` 0.35. With break chance 1, the downgrade branch is unreachable unless this mod sends the roll there.
- `m_successUpgradeSteps` is 1 and `DoCrafting` never reads it.

## Re-check after a game update

1. Confirm `Version.CurrentVersion` is still the version this DLL was built for.
2. Confirm `InventoryGui.DoCrafting(Player)` still exists.
3. Confirm the break log string and the downgrade log string are unchanged, and that `Random.Range(float, float)` still has exactly one call whose result is stored before those branches.
4. If the patch fails, the plugin logs an error and does not throw on later crafts. Refinement stays vanilla until the transpiler anchors are updated.
