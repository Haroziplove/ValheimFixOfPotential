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
            if (!TryFindBreak(codes, out int branchIndex, out int bodyStart, out int bodyEnd, out Label mergeLabel, out Label _))
            {
                throw new InvalidOperationException("Could not find the m_breakChance branch in InventoryGui.DoCrafting. " + Describe(codes));
            }

            int resultStore = FindResultStore(codes, branchIndex);
            if (resultStore < 0 || codes[resultStore].operand == null)
            {
                throw new InvalidOperationException("Could not find the craft result local in InventoryGui.DoCrafting. " + Describe(codes));
            }

            var replacement = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Ldloca, codes[resultStore].operand),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(DoCraftingPatch), nameof(ApplyBreakBranch))),
                new CodeInstruction(OpCodes.Br, mergeLabel)
            };
            replacement[0].labels.AddRange(codes[bodyStart].labels);
            replacement[0].blocks.AddRange(codes[bodyStart].blocks);

            codes.RemoveRange(bodyStart, bodyEnd - bodyStart);
            codes.InsertRange(bodyStart, replacement);
        }

        private static void InsertRollObserver(List<CodeInstruction> codes)
        {
            int rangeCall = FindRefinementRoll(codes);
            if (rangeCall < 0 || rangeCall + 1 >= codes.Count || !IsStore(codes[rangeCall + 1]) || codes[rangeCall + 1].operand == null)
            {
                throw new InvalidOperationException("The refinement Random.Range result was not found. The roll was left unchanged and the patch was not applied. " + Describe(codes));
            }

            var observer = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldloc, codes[rangeCall + 1].operand),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(DoCraftingPatch), nameof(ObserveRoll)))
            };
            codes.InsertRange(rangeCall + 2, observer);
        }

        private static bool TryFindBreak(List<CodeInstruction> codes, out int branchIndex, out int bodyStart, out int bodyEnd, out Label mergeLabel, out Label downgradeLabel)
        {
            branchIndex = -1;
            bodyStart = -1;
            bodyEnd = -1;
            mergeLabel = default(Label);
            downgradeLabel = default(Label);
            for (int i = 0; i < codes.Count; i++)
            {
                if (!IsField(codes[i], "m_breakChance"))
                {
                    continue;
                }

                for (int j = i + 1; j < codes.Count && j < i + 8; j++)
                {
                    if (codes[j].opcode.FlowControl != FlowControl.Cond_Branch || !(codes[j].operand is Label target))
                    {
                        continue;
                    }

                    int downgrade = IndexOfLabel(codes, target);
                    int mergeAt = FindUnconditionalBranch(codes, i, 40);
                    if (downgrade <= j || mergeAt < 0 || !(codes[mergeAt].operand is Label merge))
                    {
                        continue;
                    }

                    branchIndex = j;
                    bodyStart = j + 1;
                    bodyEnd = downgrade;
                    mergeLabel = merge;
                    downgradeLabel = target;
                    return true;
                }
            }

            return false;
        }

        private static int FindRefinementRoll(List<CodeInstruction> codes)
        {
            int found = -1;
            for (int i = 0; i < codes.Count; i++)
            {
                if (!IsFloatRange(codes[i]))
                {
                    continue;
                }

                found = i;
            }

            return found;
        }

        private static int FindResultStore(List<CodeInstruction> codes, int before)
        {
            for (int i = before; i >= 0; i--)
            {
                if (!IsMethod(codes[i], "AddItem"))
                {
                    continue;
                }

                for (int j = i + 1; j < codes.Count && j < i + 4; j++)
                {
                    if (IsStore(codes[j]))
                    {
                        return j;
                    }
                }
            }

            return -1;
        }

        private static int FindUnconditionalBranch(List<CodeInstruction> codes, int from, int limit)
        {
            int stop = Math.Max(0, from - limit);
            for (int i = from; i >= stop; i--)
            {
                if (codes[i].opcode.FlowControl == FlowControl.Branch && codes[i].operand is Label)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int IndexOfLabel(List<CodeInstruction> codes, Label label)
        {
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].labels != null && codes[i].labels.Contains(label))
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool IsFloatRange(CodeInstruction code)
        {
            if ((code.opcode != OpCodes.Call && code.opcode != OpCodes.Callvirt) || !(code.operand is MethodInfo method) || method.Name != "Range")
            {
                return false;
            }

            ParameterInfo[] parameters = method.GetParameters();
            return parameters.Length == 2 && parameters[0].ParameterType == typeof(float) && parameters[1].ParameterType == typeof(float);
        }

        private static bool IsField(CodeInstruction code, string name)
        {
            if (code.operand is FieldInfo field)
            {
                return field.Name == name;
            }

            return code.operand != null && code.operand.ToString().IndexOf(name, StringComparison.Ordinal) >= 0 && (code.opcode == OpCodes.Ldfld || code.opcode == OpCodes.Ldsfld);
        }

        private static bool IsMethod(CodeInstruction code, string name)
        {
            return (code.opcode == OpCodes.Call || code.opcode == OpCodes.Callvirt) && code.operand is MethodInfo method && method.Name == name;
        }

        private static bool IsStore(CodeInstruction code)
        {
            string name = code.opcode.Name;
            return name == "stloc" || name == "stloc.s" || (name != null && name.StartsWith("stloc.", StringComparison.Ordinal));
        }

        private static string Describe(List<CodeInstruction> codes)
        {
            int strings = 0;
            int breakFields = 0;
            foreach (CodeInstruction code in codes)
            {
                if (code.operand is string)
                {
                    strings++;
                }

                if (IsField(code, "m_breakChance"))
                {
                    breakFields++;
                }
            }

            return "instructions=" + codes.Count + " strings=" + strings + " breakChanceFields=" + breakFields;
        }

        private static void ObserveRoll(Player player, InventoryGui gui, float roll)
        {
            if (!ModConfig.LogAttempts.Value || player == null || gui == null || gui.m_craftUpgradeItem == null || gui.m_craftRecipe == null)
            {
                return;
            }

            ItemDrop.ItemData idol = FindIdol(gui.m_craftRecipe);
            if (idol == null)
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

        private static void ApplyBreakBranch(InventoryGui gui, Player player, ref ItemDrop.ItemData resultItem)
        {
            ItemDrop.ItemData item = gui.m_craftUpgradeItem;
            Recipe recipe = gui.m_craftRecipe;
            if (item == null || recipe == null)
            {
                return;
            }

            ItemDrop.ItemData idol = FindIdol(recipe);
            if (idol == null)
            {
                return;
            }

            int targetQuality = item.m_quality + 1;
            int amount = recipe.GetAmount(targetQuality, out int _, out ItemDrop.ItemData _, 1);
            CraftingStation station = player.GetCurrentCraftingStation();
            ZNetView stationView = station != null ? station.GetComponent<ZNetView>() : null;
            bool stationCheated = stationView != null && stationView.GetZDO() != null && stationView.GetZDO().GetBool(ZDOVars.s_cheated);
            bool cheated = (player.GetInventory().ItemCheated(recipe.m_resources) || player.NoCostCheat() || stationCheated) && !PlayerProfile.s_bypassCheatChecks;
            FailureOutcome outcome = !ModConfig.Enabled.Value ? FailureOutcome.Vanilla : ModConfig.Outcome.Value;
            if (outcome == FailureOutcome.KeepItem)
            {
                KeepItem(player, item, ref resultItem);
                return;
            }

            if (outcome == FailureOutcome.Downgrade)
            {
                Downgrade(player, gui, item, targetQuality, amount, item.m_variant, item.m_gridPos, player.GetPlayerID(), player.GetPlayerName(), cheated, ref resultItem);
                return;
            }

            BreakVanilla(player, gui, idol, item, targetQuality, player.GetPlayerID(), player.GetPlayerName(), cheated, ref resultItem);
        }

        private static ItemDrop.ItemData FindIdol(Recipe recipe)
        {
            if (recipe.m_resources == null)
            {
                return null;
            }

            foreach (Piece.Requirement requirement in recipe.m_resources)
            {
                if (requirement.m_upgraderResource && requirement.m_resItem != null)
                {
                    return requirement.m_resItem.m_itemData;
                }
            }

            return null;
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
    }
}
