using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Il2CppLast.Data;
using Il2CppLast.Data.User;
using Il2CppLast.UI.KeyInput;
using Il2CppLast.Management;
using FFIV_ScreenReader.Core;
using FFIV_ScreenReader.Utils;
using static FFIV_ScreenReader.Utils.ModTextTranslator;
using static FFIV_ScreenReader.Utils.TextUtils;

namespace FFIV_ScreenReader.Patches
{
    /// <summary>
    /// Shared state for the battle-result patch classes: the EXP-counter sound and the
    /// per-battle one-shot guards of the phased victory screen.
    /// </summary>
    internal static class BattleResultState
    {
        // True only while the EXP counter sound is actually playing.
        internal static bool ExpCounterPlaying;

        // Per-battle one-shots. Each phase's *Init can fire more than once while its page is up,
        // so each phase announces the first time and stays quiet afterwards. Cleared by ResetState
        // at battle start (BattleController.StartBattle), and by ResetIfNewResult whenever the
        // result screen is showing a different BattleResultData than last time -- so a battle
        // that skips that StartBattle overload still gets its victory screen read.
        internal static bool PointsAnnounced;
        internal static bool ItemsAnnounced;
        internal static bool AbilitiesAnnounced;
        internal static readonly HashSet<string> AnnouncedLevelUps = new HashSet<string>();

        // BattleResultData the guards above currently belong to.
        private static IntPtr lastResultDataPtr = IntPtr.Zero;

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

        /// <summary>
        /// Clears the per-battle guards. Called at battle start.
        /// </summary>
        internal static void ResetState()
        {
            ResetGuards();
            // Hard stop any counter left over from an abnormally-ended result screen.
            StopExpCounterIfPlaying();
        }

        /// <summary>
        /// Clears the guards when the result screen's data object differs from the one they were
        /// set for. Called by every phase postfix before its guard check.
        /// </summary>
        internal static void ResetIfNewResult(ResultMenuController instance)
        {
            try
            {
                var data = instance?.targetData;
                if (data == null) return;
                IntPtr ptr = data.Pointer;
                if (ptr == lastResultDataPtr) return;
                lastResultDataPtr = ptr;
                ResetGuards();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[BattleResult] ResetIfNewResult error: {ex.Message}");
            }
        }

        private static void ResetGuards()
        {
            PointsAnnounced = false;
            ItemsAnnounced = false;
            AbilitiesAnnounced = false;
            AnnouncedLevelUps.Clear();
        }
    }

    // ----------------------------------------------------------------
    //  Phased victory screen. Each page announces what it shows, when it shows it:
    //    ShowPointsInit      → gil + per-character EXP (and starts the EXP counter)
    //    ShowStatusUpInit    → level-ups with stat gains
    //    ShowGetItemsInit    → dropped items
    //    ShowGetAbilitysInit → spells learned (ResultSkillController.Show → SetLearningList)
    //  The later phases also stop the EXP counter as safety nets.
    // ----------------------------------------------------------------

    [HarmonyPatch(typeof(ResultMenuController), nameof(ResultMenuController.ShowPointsInit))]
    public static class ResultMenuController_ShowPointsInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ResultMenuController __instance)
        {
            try
            {
                BattleResultState.ResetIfNewResult(__instance);
                if (BattleResultState.PointsAnnounced) return;

                var data = __instance.targetData;
                if (data == null) return;
                BattleResultState.PointsAnnounced = true;

                int gil = data._GetGil_k__BackingField;
                if (gil > 0)
                    FFIV_ScreenReaderMod.SpeakText(string.Format(T("Gained {0} gil"), gil.ToString("N0")), interrupt: false);

                var characterList = data._CharacterList_k__BackingField;
                if (characterList != null)
                {
                    foreach (var charResult in characterList)
                    {
                        string charName = charResult?.AfterData?.Name;
                        if (string.IsNullOrEmpty(charName)) continue;

                        int charExp = charResult.GetExp;
                        if (charExp > 0)
                            FFIV_ScreenReaderMod.SpeakText(string.Format(T("{0} gained {1} XP"), charName, charExp.ToString("N0")), interrupt: false);
                    }
                }

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

    /// <summary>
    /// Level-up page: announces each character that leveled up, with the new level and stat gains.
    /// </summary>
    [HarmonyPatch(typeof(ResultMenuController), nameof(ResultMenuController.ShowStatusUpInit))]
    public static class ResultMenuController_ShowStatusUpInit_Patch
    {
        // Stat label key (existing status-screen keys) with its Base* reader and Confirmed*() fallback.
        private static readonly (string Label, Func<CharacterParameterBase, int> Base, Func<CharacterParameterBase, int> Confirmed)[] Stats =
        {
            ("HP", p => p.BaseMaxHp, p => p.ConfirmedMaxHp()),
            ("MP", p => p.BaseMaxMp, p => p.ConfirmedMaxMp()),
            ("Strength", p => p.BasePower, p => p.ConfirmedPower()),
            ("Agility", p => p.BaseAgility, p => p.ConfirmedAgility()),
            ("Stamina", p => p.BaseVitality, p => p.ConfirmedVitality()),
            ("Intellect", p => p.BaseIntelligence, p => p.ConfirmedIntelligence()),
            ("Spirit", p => p.BaseSpirit, p => p.ConfirmedSpirit()),
        };

        [HarmonyPostfix]
        public static void Postfix(ResultMenuController __instance)
        {
            // Safety net: the EXP tally is finished by the time this phase begins.
            BattleResultState.StopExpCounterIfPlaying();

            try
            {
                BattleResultState.ResetIfNewResult(__instance);
                var characterList = __instance.targetData?._CharacterList_k__BackingField;
                if (characterList == null) return;

                foreach (var charResult in characterList)
                {
                    if (charResult == null || !charResult.IsLevelUp) continue;

                    var afterData = charResult.AfterData;
                    string charName = afterData?.Name;
                    if (string.IsNullOrEmpty(charName)) continue;
                    if (!BattleResultState.AnnouncedLevelUps.Add(charName)) continue;

                    var afterParam = afterData.parameter;
                    int newLevel = afterParam?.ConfirmedLevel() ?? 0;
                    string announcement = string.Format(T("{0} leveled up to level {1}"), charName, newLevel);

                    string statGains = CalculateStatGains(charResult.BeforData?.parameter, afterParam); // BeforData: game typo
                    if (!string.IsNullOrEmpty(statGains))
                        announcement += ". " + statGains;

                    FFIV_ScreenReaderMod.SpeakText(announcement, interrupt: false);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ResultMenuController.ShowStatusUpInit patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Stat gains between the before/after snapshots, e.g. "HP +25, Strength +2".
        /// Base* values are the exact level-up growth (Confirmed*() folds in equipment and caps);
        /// Confirmed*() is the fallback for builds where the Base* auto-property getter throws.
        /// </summary>
        private static string CalculateStatGains(CharacterParameterBase before, CharacterParameterBase after)
        {
            if (before == null || after == null) return null;

            var gains = new List<string>();
            foreach (var stat in Stats)
            {
                int diff = ReadStat(after, stat.Base, stat.Confirmed) - ReadStat(before, stat.Base, stat.Confirmed);
                if (diff > 0)
                    gains.Add(string.Format(T("{0} +{1}"), T(stat.Label), diff));
            }
            return gains.Count > 0 ? string.Join(", ", gains) : null;
        }

        private static int ReadStat(CharacterParameterBase param, Func<CharacterParameterBase, int> baseValue, Func<CharacterParameterBase, int> confirmed)
        {
            try { return baseValue(param); }
            catch
            {
                try { return confirmed(param); }
                catch { return 0; }
            }
        }
    }

    /// <summary>
    /// Items page: announces each dropped item.
    /// </summary>
    [HarmonyPatch(typeof(ResultMenuController), nameof(ResultMenuController.ShowGetItemsInit))]
    public static class ResultMenuController_ShowGetItemsInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ResultMenuController __instance)
        {
            // Safety net: the EXP tally is finished by the time this phase begins.
            BattleResultState.StopExpCounterIfPlaying();

            try
            {
                BattleResultState.ResetIfNewResult(__instance);
                if (BattleResultState.ItemsAnnounced) return;

                var itemList = __instance.targetData?._ItemList_k__BackingField;
                var messageManager = MessageManager.Instance;
                if (itemList == null || itemList.Count == 0 || messageManager == null) return;
                BattleResultState.ItemsAnnounced = true;

                var itemContentList = ListItemFormatter.GetContentDataList(itemList, messageManager);
                if (itemContentList == null) return;

                foreach (var itemContent in itemContentList)
                {
                    string itemName = StripIconMarkup(itemContent?.Name);
                    if (string.IsNullOrEmpty(itemName)) continue;

                    int quantity = itemContent.Count;
                    string announcement = quantity > 1
                        ? string.Format(T("Found {0} x{1}"), itemName, quantity)
                        : string.Format(T("Found {0}"), itemName);
                    FFIV_ScreenReaderMod.SpeakText(announcement, interrupt: false);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ResultMenuController.ShowGetItemsInit patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Learned-abilities page: FF4 learns spells on level-up (BattleResultCharacterData.LearningList),
    /// which the skill controller lists on this page.
    /// </summary>
    [HarmonyPatch(typeof(ResultMenuController), nameof(ResultMenuController.ShowGetAbilitysInit))]
    public static class ResultMenuController_ShowGetAbilitysInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ResultMenuController __instance)
        {
            // Safety net: the EXP tally is finished by the time this phase begins.
            BattleResultState.StopExpCounterIfPlaying();

            try
            {
                BattleResultState.ResetIfNewResult(__instance);
                if (BattleResultState.AbilitiesAnnounced) return;

                var characterList = __instance.targetData?._CharacterList_k__BackingField;
                var messageManager = MessageManager.Instance;
                if (characterList == null || messageManager == null) return;
                BattleResultState.AbilitiesAnnounced = true;

                foreach (var charResult in characterList)
                {
                    var afterData = charResult?.AfterData;
                    var learningList = charResult?.LearningList;
                    if (afterData?.OwnedAbilityList == null || learningList == null) continue;

                    foreach (int abilityId in learningList)
                    {
                        string abilityName = GetOwnedAbilityName(afterData, abilityId, messageManager);
                        if (!string.IsNullOrWhiteSpace(abilityName))
                            FFIV_ScreenReaderMod.SpeakText(string.Format(T("{0} learned {1}"), afterData.Name, abilityName), interrupt: false);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ResultMenuController.ShowGetAbilitysInit patch: {ex.Message}");
            }
        }

        private static string GetOwnedAbilityName(OwnedCharacterData character, int abilityId, MessageManager messageManager)
        {
            var abilities = character.OwnedAbilityList;
            for (int i = 0; i < abilities.Count; i++)
            {
                var owned = abilities[i];
                if (owned?.Ability != null && owned.Ability.Id == abilityId)
                    return StripIconMarkup(messageManager.GetMessage(owned.MesIdName));
            }
            return null;
        }
    }

    // ----------------------------------------------------------------
    //  Safety-net stop hooks: once the result screen advances past the
    //  EXP-tally phase, the counter sound must stop.
    // ----------------------------------------------------------------
    [HarmonyPatch(typeof(ResultMenuController), nameof(ResultMenuController.ShowLevelUpAbilitysInit))]
    public static class ResultMenuController_ShowLevelUpAbilitysInit_Patch
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
