using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace SafeRefinement
{
    internal static class DoCraftingPatch
    {
        private const string BreakLog = "Upgrader <color=red>BROKE</color> {0} level {1} and was destroyed!";
        private const string DowngradeLog = "Upgrader <color=orange>FAILED</color> {0} level {1}, level was reduced!";

        public static bool TryApply(Harmony harmony, ManualLogSource log)
        {
            MethodInfo method = AccessTools.Method(typeof(InventoryGui), "DoCrafting", new[] { typeof(Player) });
            if (method == null)
            {
                log.LogError("InventoryGui.DoCrafting(Player) was not found. SafeRefinement will not change refinement.");
                return false;
            }

            try
            {
                harmony.Patch(method, transpiler: new HarmonyMethod(typeof(DoCraftingPatch), nameof(Transpiler)));
                return true;
            }
            catch (Exception ex)
            {
                log.LogError("Patching InventoryGui.DoCrafting failed. SafeRefinement is disabled. " + ex);
                return false;
            }
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            ReplaceBreakBody(codes);
            InsertRollObserver(codes);
            return codes;
        }

        private static void ReplaceBreakBody(List<CodeInstruction> codes)
        {
            int broke = FindString(codes, BreakLog);
            int downgrade = FindString(codes, DowngradeLog);
            if (broke < 0 || downgrade < 0 || downgrade <= broke)
            {
                throw new InvalidOperationException("DoCrafting break branch was not found. The game method no longer matches Valheim 1.0.17.");
            }

            int successFlagStore = FindLastLocalStore(codes, 16, broke);
            if (successFlagStore < 0 || successFlagStore + 1 >= codes.Count)
            {
                throw new InvalidOperationException("DoCrafting success branch exit was not found.");
            }

            CodeInstruction successExit = codes[successFlagStore + 1];
            if (successExit.opcode != OpCodes.Br && successExit.opcode != OpCodes.Br_S)
            {
                throw new InvalidOperationException("DoCrafting success branch does not branch to the shared exit.");
            }

            var replacement = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldarg_1),
                LoadLocal(codes, 20),
                LoadLocal(codes, 0),
                LoadLocal(codes, 4),
                LoadLocal(codes, 8),
                LoadLocal(codes, 9),
                LoadLocal(codes, 10),
                LoadLocal(codes, 11),
                LoadLocal(codes, 14),
                AddressOf(codes, 15),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(DoCraftingPatch), nameof(ApplyBreakBranch))),
                new CodeInstruction(OpCodes.Br, successExit.operand)
            };
            replacement[0].labels.AddRange(codes[broke].labels);
            replacement[0].blocks.AddRange(codes[broke].blocks);

            codes.RemoveRange(broke, downgrade - broke);
            codes.InsertRange(broke, replacement);
        }

        private static void InsertRollObserver(List<CodeInstruction> codes)
        {
            int rangeCall = -1;
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode != OpCodes.Call || !(codes[i].operand is MethodInfo method) || method.Name != "Range")
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 2 && parameters[0].ParameterType == typeof(float) && parameters[1].ParameterType == typeof(float))
                {
                    if (rangeCall >= 0)
                    {
                        throw new InvalidOperationException("DoCrafting contains more than one Random.Range(float, float) call.");
                    }

                    rangeCall = i;
                }
            }

            if (rangeCall < 0 || rangeCall + 1 >= codes.Count || !IsStore(codes[rangeCall + 1], 24))
            {
                throw new InvalidOperationException("The refinement Random.Range result was not found. The roll was left unchanged and the patch was not applied.");
            }

            int insertAt = rangeCall + 2;
            var observer = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Ldarg_0),
                LoadLocal(codes, 20),
                LoadLocal(codes, 0),
                LoadLocal(codes, 24),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(DoCraftingPatch), nameof(ObserveRoll)))
            };
            codes.InsertRange(insertAt, observer);
        }

        private static void ObserveRoll(Player player, InventoryGui gui, ItemDrop.ItemData idol, int targetQuality, float roll)
        {
            if (!ModConfig.LogAttempts.Value || player == null || gui == null || idol == null || gui.m_craftUpgradeItem == null)
            {
                return;
            }

            ItemDrop.ItemData item = gui.m_craftUpgradeItem;
            bool success = idol.m_shared.m_upgradeChance >= roll;
            bool vanillaBreak = idol.m_shared.m_breakChance >= 1f - roll;
            string outcome;
            if (success)
            {
                outcome = "Success";
            }
            else if (!vanillaBreak)
            {
                outcome = "Downgrade";
            }
            else if (!ModConfig.Enabled.Value)
            {
                outcome = "Vanilla";
            }
            else
            {
                outcome = ModConfig.Outcome.Value.ToString();
            }

            Plugin.Log.LogInfo(
                player.GetPlayerName()
                + " item=" + item.m_shared.m_name
                + " level=" + item.m_quality
                + " roll=" + roll.ToString("R", CultureInfo.InvariantCulture)
                + " outcome=" + outcome);
        }

        private static void ApplyBreakBranch(
            InventoryGui gui,
            Player player,
            ItemDrop.ItemData idol,
            int targetQuality,
            int amount,
            int variant,
            Vector2i position,
            long playerId,
            string playerName,
            bool cheated,
            ref ItemDrop.ItemData resultItem)
        {
            ItemDrop.ItemData item = gui.m_craftUpgradeItem;
            FailureOutcome outcome = !ModConfig.Enabled.Value ? FailureOutcome.Vanilla : ModConfig.Outcome.Value;
            if (outcome == FailureOutcome.KeepItem)
            {
                KeepItem(player, item, ref resultItem);
                return;
            }

            if (outcome == FailureOutcome.Downgrade)
            {
                Downgrade(player, gui, item, targetQuality, amount, variant, position, playerId, playerName, cheated, ref resultItem);
                return;
            }

            BreakVanilla(player, gui, idol, item, targetQuality, playerId, playerName, cheated, ref resultItem);
        }

        private static void KeepItem(Player player, ItemDrop.ItemData item, ref ItemDrop.ItemData resultItem)
        {
            string name = item.m_shared.m_name;
            int level = item.m_quality;
            Inventory inventory = player.GetInventory();
            bool added = false;
            if (item.m_gridPos.x >= 0 && item.m_gridPos.y >= 0 && inventory.GetItemAt(item.m_gridPos.x, item.m_gridPos.y) == null)
            {
                added = inventory.AddItem(item, item.m_gridPos);
            }

            if (!added)
            {
                added = inventory.AddItem(item);
            }

            if (!added)
            {
                ZLog.LogError("SafeRefinement failed to return " + name + " to the inventory after a failed refinement.");
                player.Message(MessageHud.MessageType.Center, Localization.instance.Localize(name) + " refinement failed and the item could not be returned.");
                return;
            }

            resultItem = item;
            ZLog.Log("Upgrader kept " + name + " at level " + level + ".");
            player.Message(
                MessageHud.MessageType.Center,
                Localization.instance.Localize(name) + " refinement failed and was kept at level " + level + ".",
                0,
                null,
                true);
        }

        private static void Downgrade(
            Player player,
            InventoryGui gui,
            ItemDrop.ItemData item,
            int targetQuality,
            int amount,
            int variant,
            Vector2i position,
            long playerId,
            string playerName,
            bool cheated,
            ref ItemDrop.ItemData resultItem)
        {
            int reducedLevel = targetQuality - 2;
            ZLog.Log($"Upgrader <color=orange>FAILED</color> {item.m_shared.m_name} level {targetQuality - 1}, level was reduced!");
            player.Message(
                MessageHud.MessageType.Center,
                Localization.instance.Localize("$msg_upgrader_failed", item.m_shared.m_name, reducedLevel.ToString()),
                0,
                null,
                true);
            resultItem = player.GetInventory().AddItem(
                gui.m_craftRecipe.m_item.gameObject.name,
                amount,
                reducedLevel,
                variant,
                playerId,
                playerName,
                position,
                cheated);
        }

        private static void BreakVanilla(
            Player player,
            InventoryGui gui,
            ItemDrop.ItemData idol,
            ItemDrop.ItemData item,
            int targetQuality,
            long playerId,
            string playerName,
            bool cheated,
            ref ItemDrop.ItemData resultItem)
        {
            ZLog.Log($"Upgrader <color=red>BROKE</color> {item.m_shared.m_name} level {targetQuality - 1} and was destroyed!");
            player.Message(
                MessageHud.MessageType.Center,
                Localization.instance.Localize("$msg_upgrader_broke", item.m_shared.m_name, (targetQuality - 1).ToString()),
                0,
                null,
                true);
            if (idol.m_shared.m_breakReturnIngreientsAmount <= 0f)
            {
                return;
            }

            foreach (Piece.Requirement requirement in gui.m_craftRecipe.m_resources)
            {
                if (!requirement.m_recover)
                {
                    continue;
                }

                int count = Mathf.CeilToInt((requirement.GetAmount(1) + requirement.GetAmount(targetQuality - 1)) * idol.m_shared.m_breakReturnIngreientsAmount);
                if (count <= 0)
                {
                    continue;
                }

                resultItem = player.GetInventory().AddItem(
                    requirement.m_resItem.name,
                    count,
                    requirement.m_resItem.m_itemData.m_quality,
                    requirement.m_resItem.m_itemData.m_variant,
                    playerId,
                    playerName,
                    new Vector2i(-1, -1),
                    cheated);
                ZLog.Log($"Upgrader returned {requirement.m_resItem.m_itemData.m_shared.m_name} x{count} to inventory");
            }
        }

        private static int FindString(List<CodeInstruction> codes, string text)
        {
            int found = -1;
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldstr && (string)codes[i].operand == text)
                {
                    if (found >= 0)
                    {
                        throw new InvalidOperationException("DoCrafting contains more than one copy of \"" + text + "\".");
                    }

                    found = i;
                }
            }

            return found;
        }

        private static int FindLastLocalStore(List<CodeInstruction> codes, int index, int before)
        {
            int found = -1;
            for (int i = 0; i < before; i++)
            {
                if (IsStore(codes[i], index))
                {
                    found = i;
                }
            }

            return found;
        }

        private static bool IsStore(CodeInstruction instruction, int index)
        {
            return instruction.opcode == OpCodes.Stloc_S || instruction.opcode == OpCodes.Stloc
                ? LocalIndex(instruction) == index
                : false;
        }

        private static CodeInstruction LoadLocal(List<CodeInstruction> codes, int index)
        {
            CodeInstruction existing = FindLocalInstruction(codes, index);
            if (existing.opcode == OpCodes.Ldloc_S || existing.opcode == OpCodes.Ldloc)
            {
                return new CodeInstruction(existing.opcode, existing.operand);
            }

            if (existing.opcode == OpCodes.Stloc_S)
            {
                return new CodeInstruction(OpCodes.Ldloc_S, existing.operand);
            }

            if (existing.opcode == OpCodes.Stloc)
            {
                return new CodeInstruction(OpCodes.Ldloc, existing.operand);
            }

            throw new InvalidOperationException("Cannot load local " + index + " from opcode " + existing.opcode);
        }

        private static CodeInstruction AddressOf(List<CodeInstruction> codes, int index)
        {
            foreach (CodeInstruction code in codes)
            {
                if (LocalIndex(code) == index && (code.opcode == OpCodes.Ldloca_S || code.opcode == OpCodes.Ldloca))
                {
                    return new CodeInstruction(code.opcode, code.operand);
                }
            }

            CodeInstruction existing = FindLocalInstruction(codes, index);
            if (existing.opcode == OpCodes.Ldloc_S || existing.opcode == OpCodes.Stloc_S)
            {
                return new CodeInstruction(OpCodes.Ldloca_S, existing.operand);
            }

            if (existing.opcode == OpCodes.Ldloc || existing.opcode == OpCodes.Stloc)
            {
                return new CodeInstruction(OpCodes.Ldloca, existing.operand);
            }

            throw new InvalidOperationException("Cannot address local " + index + ".");
        }

        private static CodeInstruction FindLocalInstruction(List<CodeInstruction> codes, int index)
        {
            foreach (CodeInstruction code in codes)
            {
                if (LocalIndex(code) == index)
                {
                    return code;
                }
            }

            throw new InvalidOperationException("Local " + index + " was not found in DoCrafting.");
        }

        private static int LocalIndex(CodeInstruction instruction)
        {
            object operand = instruction.operand;
            if (operand == null)
            {
                return -1;
            }

            if (operand is LocalBuilder builder)
            {
                return builder.LocalIndex;
            }

            if (operand is LocalVariableInfo variable)
            {
                return variable.LocalIndex;
            }

            if (operand is byte unsigned)
            {
                return unsigned;
            }

            if (operand is sbyte signed)
            {
                return signed;
            }

            if (operand is short shortIndex)
            {
                return shortIndex;
            }

            if (operand is int intIndex)
            {
                return intIndex;
            }

            PropertyInfo property = operand.GetType().GetProperty("LocalIndex") ?? operand.GetType().GetProperty("Index");
            if (property != null && property.PropertyType == typeof(int))
            {
                return (int)property.GetValue(operand, null);
            }

            return -1;
        }
    }
}
