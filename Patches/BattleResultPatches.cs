using System;
using System.Collections;
using System.Linq;
using System.Runtime.InteropServices;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Il2CppLast.Data;
using Il2CppLast.Data.User;
using Il2CppLast.UI.KeyInput;
using Il2CppLast.Management;
using Il2CppLast.Systems;
using FFIV_ScreenReader.Core;
using FFIV_ScreenReader.Utils;
using static FFIV_ScreenReader.Utils.TextUtils;

namespace FFIV_ScreenReader.Patches
{
    /// <summary>
    /// Shared state for the EXP-counter sound across the battle-result patch classes.
    /// </summary>
    internal static class BattleResultState
    {
        // True only while the EXP counter sound is actually playing.
        internal static bool ExpCounterPlaying;

        /// <summary>
        /// Stops the EXP counter sound if it is currently playing.
        /// Safe to call from any phase-init postfix; the flag ensures it only fires once.
        /// </summary>
        internal static void StopExpCounterIfPlaying()
        {
            if (!ExpCounterPlaying) return;
            ExpCounterPlaying = false;
            SoundPlayer.StopExpCounter();
            MelonLogger.Msg("[BattleResult] EXP counter stopped");
        }
    }

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
                // NOTE: Do NOT call BattleState.Reset() here!
                // The victory screen is still part of the Battle scene.
                // BattleState.Reset() is called in OnSceneLoaded when transitioning
                // to a non-battle scene, which properly restores navigation features.

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
                return;
            }

            lastBattleData = data;
            lastAnnouncement = announcement;
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

    // Patch ShowPointsInit to catch cases where the controller is reused/pooled, and to start
    // the EXP counter sound when the EXP tally animation begins.
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

                // Start the EXP counter sound if enabled and this battle awarded EXP.
                int totalExp = data.GetExp;
                if (FFIV_ScreenReaderMod.ExpCounterEnabled && totalExp > 0)
                {
                    SoundPlayer.PlayExpCounter();
                    BattleResultState.ExpCounterPlaying = true;

                    // Stop the counter when the tally animation finishes.
                    CoroutineManager.StartUntracked(MonitorExpCounterAnimation(__instance.Pointer));
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ResultMenuController.ShowPointsInit patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Polls the unsafe pointer chain from ResultMenuController to detect when the EXP
        /// counting animation finishes, then stops the counter sound. FF4's KeyInput result
        /// graph mirrors FF5's exactly, so the offsets are identical:
        /// Chain: instance -> +0x20 (pointController) -> +0x30 (characterListConteroller)
        ///   -> +0x20 (contentList, count at +0x18) ; perormanceEndCount at +0x30.
        /// Animation done when: perormanceEndCount >= contentList.Count &amp;&amp; Count > 0.
        /// </summary>
        private static IEnumerator MonitorExpCounterAnimation(IntPtr instancePtr)
        {
            var wait = new WaitForSeconds(0.1f);
            bool loggedOnce = false;

            if (instancePtr == IntPtr.Zero)
            {
                MelonLogger.Warning("[BattleResult] MonitorExp: instancePtr is null");
                yield break;
            }

            IntPtr pointControllerPtr = Marshal.ReadIntPtr(instancePtr, 0x20);
            if (pointControllerPtr == IntPtr.Zero)
            {
                MelonLogger.Warning("[BattleResult] MonitorExp: pointController is null");
                yield break;
            }

            IntPtr charListCtrlPtr = Marshal.ReadIntPtr(pointControllerPtr, 0x30);
            if (charListCtrlPtr == IntPtr.Zero)
            {
                MelonLogger.Warning("[BattleResult] MonitorExp: characterListController is null");
                yield break;
            }

            IntPtr contentListPtr = Marshal.ReadIntPtr(charListCtrlPtr, 0x20);
            if (contentListPtr == IntPtr.Zero)
            {
                MelonLogger.Warning("[BattleResult] MonitorExp: contentList is null");
                yield break;
            }

            // contentList.Count (List._size) at contentListPtr + 0x18
            int contentCount = Marshal.ReadInt32(contentListPtr, 0x18);
            if (contentCount <= 0)
            {
                MelonLogger.Warning($"[BattleResult] MonitorExp: contentCount={contentCount}, aborting");
                yield break;
            }

            MelonLogger.Msg($"[BattleResult] MonitorExp: chain OK. charListCtrl=0x{charListCtrlPtr:X}, contentCount={contentCount}");

            // Poll until animation finishes or the counter was already stopped by a safety net.
            while (BattleResultState.ExpCounterPlaying)
            {
                yield return wait;

                // Keep the Counter stream fed so the loop never drains between ticks.
                SoundPlayer.TopUpExpCounter();

                try
                {
                    int endCount = Marshal.ReadInt32(charListCtrlPtr, 0x30);

                    if (!loggedOnce)
                    {
                        MelonLogger.Msg($"[BattleResult] MonitorExp: first poll endCount={endCount}/{contentCount}");
                        loggedOnce = true;
                    }

                    if (endCount >= contentCount)
                    {
                        MelonLogger.Msg($"[BattleResult] MonitorExp: animation done (endCount={endCount} >= contentCount={contentCount})");
                        BattleResultState.StopExpCounterIfPlaying();
                        yield break;
                    }
                }
                catch
                {
                    // Pointer became invalid -- bail out; the phase-init safety nets will stop it.
                    yield break;
                }
            }
        }
    }

    // ----------------------------------------------------------------
    //  Safety-net stop hooks: once the result screen advances past the
    //  EXP-tally phase, the counter sound must stop.
    // ----------------------------------------------------------------
    [HarmonyPatch(typeof(ResultMenuController), nameof(ResultMenuController.ShowStatusUpInit))]
    public static class ResultMenuController_ShowStatusUpInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix() => BattleResultState.StopExpCounterIfPlaying();
    }

    [HarmonyPatch(typeof(ResultMenuController), nameof(ResultMenuController.ShowGetAbilitysInit))]
    public static class ResultMenuController_ShowGetAbilitysInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix() => BattleResultState.StopExpCounterIfPlaying();
    }

    [HarmonyPatch(typeof(ResultMenuController), nameof(ResultMenuController.ShowGetItemsInit))]
    public static class ResultMenuController_ShowGetItemsInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix() => BattleResultState.StopExpCounterIfPlaying();
    }

    // EndWaitInit: results dismissed -- guaranteed backstop that the tone never sticks.
    [HarmonyPatch(typeof(ResultMenuController), nameof(ResultMenuController.EndWaitInit))]
    public static class ResultMenuController_EndWaitInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix() => BattleResultState.StopExpCounterIfPlaying();
    }
}
