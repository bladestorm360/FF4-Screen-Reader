using System;
using System.Linq;
using HarmonyLib;
using MelonLoader;
using Il2CppLast.Data;
using Il2CppLast.Data.User;
using Il2CppLast.UI.KeyInput;
using Il2CppLast.Management;
using Il2CppLast.Systems;
using FFIV_ScreenReader.Core;
using static FFIV_ScreenReader.Utils.TextUtils;

namespace FFIV_ScreenReader.Patches
{
    /// <summary>
    /// Patches for battle result announcements (XP, gil, level ups, stat growth)
    /// TODO: Implement multi-phase victory screen announcements in future update
    /// </summary>

    [HarmonyPatch(typeof(ResultMenuController), nameof(ResultMenuController.Show))]
    public static class ResultMenuController_Show_Patch
    {
        internal static string lastAnnouncement = "";
        internal static BattleResultData lastBattleData = null;

        [HarmonyPostfix]
        public static void Postfix(BattleResultData data, bool isReverse)
        {
            try
            {
                if (data == null || isReverse)
                {
                    return;
                }

                ProcessBattleResult(data, "Show");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ResultMenuController.Show patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Shared method to process battle results from both Show and ShowPointsInit
        /// </summary>
        internal static void ProcessBattleResult(BattleResultData data, string source)
        {
            // Build announcement message
            var messageParts = new System.Collections.Generic.List<string>();

            // Announce gil gained
            int gil = data._GetGil_k__BackingField;
            if (gil > 0)
            {
                messageParts.Add($"{gil:N0} gil");
            }

            // Announce items dropped
            if (data._ItemList_k__BackingField != null && data._ItemList_k__BackingField.Count > 0)
            {
                var messageManager = MessageManager.Instance;
                if (messageManager != null)
                {
                    var itemContentList = ListItemFormatter.GetContentDataList(data._ItemList_k__BackingField, messageManager);
                    if (itemContentList != null && itemContentList.Count > 0)
                    {
                        foreach (var itemContent in itemContentList)
                        {
                            if (itemContent == null) continue;

                            string itemName = itemContent.Name;
                            if (string.IsNullOrEmpty(itemName)) continue;

                            itemName = StripIconMarkup(itemName);

                            if (!string.IsNullOrEmpty(itemName))
                            {
                                int quantity = itemContent.Count;
                                if (quantity > 1)
                                {
                                    messageParts.Add($"{itemName} x{quantity}");
                                }
                                else
                                {
                                    messageParts.Add(itemName);
                                }
                            }
                        }
                    }
                }
            }

            // Announce character results
            if (data._CharacterList_k__BackingField != null)
            {
                var characterResults = data._CharacterList_k__BackingField;

                foreach (var charResult in characterResults)
                {
                    if (charResult == null) continue;

                    var afterData = charResult.AfterData;
                    if (afterData == null) continue;

                    string charName = afterData.Name;
                    int charExp = charResult.GetExp;

                    // Always announce XP first
                    messageParts.Add($"{charName} gained {charExp:N0} XP");

                    // Check if leveled up - announce with stat growth
                    if (charResult.IsLevelUp)
                    {
                        int newLevel = afterData.parameter?.ConfirmedLevel() ?? 0;
                        string levelUpMessage = $"{charName} leveled up to level {newLevel}";

                        // Calculate and announce stat growth
                        string statGrowth = CalculateStatGrowth(charResult);
                        if (!string.IsNullOrEmpty(statGrowth))
                        {
                            levelUpMessage += $". {statGrowth}";
                        }

                        messageParts.Add(levelUpMessage);
                    }

                    // Check if learned any abilities
                    var learningList = charResult.LearningList;
                    if (learningList != null && learningList.Count > 0)
                    {
                        var messageManager = MessageManager.Instance;
                        if (messageManager != null && afterData.OwnedAbilityList != null)
                        {
                            foreach (int abilityId in learningList)
                            {
                                OwnedAbility ownedAbility = null;
                                for (int i = 0; i < afterData.OwnedAbilityList.Count; i++)
                                {
                                    var ability = afterData.OwnedAbilityList[i];
                                    if (ability != null && ability.Ability != null && ability.Ability.Id == abilityId)
                                    {
                                        ownedAbility = ability;
                                        break;
                                    }
                                }

                                if (ownedAbility != null)
                                {
                                    string abilityName = messageManager.GetMessage(ownedAbility.MesIdName);
                                    if (!string.IsNullOrWhiteSpace(abilityName))
                                    {
                                        messageParts.Add($"{charName} learned {abilityName}");
                                    }
                                }
                            }
                        }
                    }
                }
            }

            if (messageParts.Count == 0) return;

            string announcement = string.Join(", ", messageParts);

            // Skip duplicate
            if (data == lastBattleData && announcement == lastAnnouncement)
            {
                MelonLogger.Msg($"[Battle Results] Skipping duplicate announcement from {source} (same battle)");
                return;
            }

            lastBattleData = data;
            lastAnnouncement = announcement;
            MelonLogger.Msg($"[Battle Results {source}] {announcement}");
            FFIV_ScreenReaderMod.SpeakText(announcement, interrupt: false);
        }

        /// <summary>
        /// Calculates stat growth between before and after level-up.
        /// Returns a formatted string like "HP +25, Strength +2, Agility +1"
        /// </summary>
        private static string CalculateStatGrowth(BattleResultData.BattleResultCharacterData charResult)
        {
            try
            {
                var beforeData = charResult.BeforData; // Note: typo in original game code
                var afterData = charResult.AfterData;

                if (beforeData?.parameter == null || afterData?.parameter == null)
                {
                    return null;
                }

                var beforeParam = beforeData.parameter;
                var afterParam = afterData.parameter;

                var statChanges = new System.Collections.Generic.List<string>();

                // HP
                int hpDiff = afterParam.BaseMaxHp - beforeParam.BaseMaxHp;
                if (hpDiff > 0) statChanges.Add($"HP +{hpDiff}");

                // MP
                int mpDiff = afterParam.BaseMaxMp - beforeParam.BaseMaxMp;
                if (mpDiff > 0) statChanges.Add($"MP +{mpDiff}");

                // Strength (Power)
                int strDiff = afterParam.BasePower - beforeParam.BasePower;
                if (strDiff > 0) statChanges.Add($"Strength +{strDiff}");

                // Stamina (Vitality)
                int staDiff = afterParam.BaseVitality - beforeParam.BaseVitality;
                if (staDiff > 0) statChanges.Add($"Stamina +{staDiff}");

                // Agility
                int agiDiff = afterParam.BaseAgility - beforeParam.BaseAgility;
                if (agiDiff > 0) statChanges.Add($"Agility +{agiDiff}");

                // Intelligence
                int intDiff = afterParam.BaseIntelligence - beforeParam.BaseIntelligence;
                if (intDiff > 0) statChanges.Add($"Intelligence +{intDiff}");

                // Spirit
                int sprDiff = afterParam.BaseSpirit - beforeParam.BaseSpirit;
                if (sprDiff > 0) statChanges.Add($"Spirit +{sprDiff}");

                if (statChanges.Count == 0) return null;

                return string.Join(", ", statChanges);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error calculating stat growth: {ex.Message}");
                return null;
            }
        }
    }

    // Patch ShowPointsInit to catch cases where the controller is reused/pooled
    [HarmonyPatch(typeof(ResultMenuController), nameof(ResultMenuController.ShowPointsInit))]
    public static class ResultMenuController_ShowPointsInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ResultMenuController __instance)
        {
            try
            {
                var data = __instance.targetData;
                if (data == null) return;

                ResultMenuController_Show_Patch.ProcessBattleResult(data, "ShowPointsInit");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ResultMenuController.ShowPointsInit patch: {ex.Message}");
            }
        }
    }
}
