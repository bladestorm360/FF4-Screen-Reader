using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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

                    // The character list whose tally ends the counter: pointController @0x20 →
                    // characterListConteroller @0x30 (key_battle prefab: point_root / character_list).
                    ResultCharacterListController_PerformanceEnd_Patch.ExpectedList = ReadCharacterListPtr(__instance.Pointer);

                    // Keep the beep stream fed; ResultCharacterListController_PerformanceEnd_Patch
                    // stops it when the tally animation ends (and the later pages are safety nets).
                    CoroutineManager.StartUntracked(FeedExpCounter());
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ResultMenuController.ShowPointsInit patch: {ex.Message}");
            }
        }

        /// <summary>
        /// ResultMenuController (KeyInput) → pointController @0x20 (Serial.FF0.UI.KeyInput
        /// ResultPointController) → characterListConteroller @0x30. Zero if the chain can't be read.
        /// </summary>
        private static IntPtr ReadCharacterListPtr(IntPtr resultMenuPtr)
        {
            try
            {
                if (resultMenuPtr == IntPtr.Zero) return IntPtr.Zero;
                IntPtr point = Marshal.ReadIntPtr(resultMenuPtr, 0x20);
                return point == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(point, 0x30);
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// The EXP counter's audio loop: tops the Counter stream up each frame while the counter
        /// plays (TopUpExpCounter only submits when fewer than two beep buffers are queued). It
        /// reads no game state and has no timer: the stop comes from the game's own
        /// end-of-performance events (2026-09-24; this used to poll perormanceEndCount every
        /// 0.1 s with WaitForSeconds).
        /// </summary>
        private static IEnumerator FeedExpCounter()
        {
            while (BattleResultState.ExpCounterPlaying)
            {
                SoundPlayer.TopUpExpCounter();
                yield return null;
            }
        }
    }

    /// <summary>
    /// Stops the EXP counter when the victory screen's EXP tally ends, from the game's own events
    /// on the KeyInput ResultCharacterListController (Serial.FF0.UI.KeyInput, reached from
    /// ResultMenuController → pointController @0x20 → characterListConteroller @0x30):
    ///   &lt;PlayPerformance&gt;b__6_0 (RVA 0x40CC70, shared only with the Touch class's identical
    ///     lambda): each character's tally coroutine calls it when it finishes; its body is
    ///     perormanceEndCount++ (@0x30). The tally is over when that reaches performanceList.Count
    ///     (@0x28, List._size @0x18) — the game's own IsEndPerformance test.
    ///   ForcedEndPerformance (RVA 0x48A4C0, unique; called from ResultPointController.PointsUpdate
    ///     when the player skips): stops the coroutines and completes every row at once
    ///     (ResultCharacterListController_ForcedEndPerformance_Patch).
    /// Prepare() skips the lambda patch (instead of failing PatchAll) if the method is missing.
    /// </summary>
    [HarmonyPatch]
    public static class ResultCharacterListController_PerformanceEnd_Patch
    {
        private const int OFFSET_PERFORMANCE_LIST = 0x28;
        private const int OFFSET_PERFORMANCE_END_COUNT = 0x30;
        private const int LIST_SIZE_OFFSET = 0x18;

        /// <summary>Set by ShowPointsInit: the character list whose tally ends the counter (0 = any).</summary>
        internal static IntPtr ExpectedList;

        private static MethodBase CountUp => AccessTools.Method(
            typeof(Il2CppSerial.FF0.UI.KeyInput.ResultCharacterListController), "_PlayPerformance_b__6_0");

        static bool Prepare()
        {
            bool ok = CountUp != null;
            if (!ok)
                MelonLogger.Warning("[BattleResult] ResultCharacterListController <PlayPerformance>b__6_0 not found; the EXP counter stops on the next result page");
            return ok;
        }

        static MethodBase TargetMethod() => CountUp;

        [HarmonyPostfix]
        public static void Postfix(Il2CppSerial.FF0.UI.KeyInput.ResultCharacterListController __instance)
        {
            try
            {
                if (!BattleResultState.ExpCounterPlaying || __instance == null) return;

                IntPtr ptr = __instance.Pointer;
                if (ptr == IntPtr.Zero) return;
                // key_battle has two ResultCharacterListController objects (character_list and
                // character_view); only the one the results page tallies on ends the counter.
                if (ExpectedList != IntPtr.Zero && ptr != ExpectedList) return;
                IntPtr listPtr = Marshal.ReadIntPtr(ptr, OFFSET_PERFORMANCE_LIST);
                if (listPtr == IntPtr.Zero) return;
                int total = Marshal.ReadInt32(listPtr, LIST_SIZE_OFFSET);
                int ended = Marshal.ReadInt32(ptr, OFFSET_PERFORMANCE_END_COUNT);
                if (ended >= total)
                    BattleResultState.StopExpCounterIfPlaying();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[BattleResult] Error in performance-end patch: {ex.Message}");
            }
        }
    }

    /// <summary>The player skipped the EXP tally (see ResultCharacterListController_PerformanceEnd_Patch).</summary>
    [HarmonyPatch(typeof(Il2CppSerial.FF0.UI.KeyInput.ResultCharacterListController),
        nameof(Il2CppSerial.FF0.UI.KeyInput.ResultCharacterListController.ForcedEndPerformance))]
    public static class ResultCharacterListController_ForcedEndPerformance_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            BattleResultState.StopExpCounterIfPlaying();
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
