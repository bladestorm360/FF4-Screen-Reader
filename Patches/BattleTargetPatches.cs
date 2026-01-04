using System;
using HarmonyLib;
using MelonLoader;
using Il2CppLast.Battle;
using Il2CppLast.Management;
using Il2CppLast.UI.KeyInput;
using FFIV_ScreenReader.Core;
using BattlePlayerData = Il2Cpp.BattlePlayerData;

namespace FFIV_ScreenReader.Patches
{
    /// <summary>
    /// Manages target selection state and provides patches for all-target announcements.
    /// The IsTargetSelectionActive flag prevents command patches from announcing
    /// during target selection (e.g., prevents "Attack" from interrupting target announcements).
    /// </summary>
    public static class BattleTargetPatches
    {
        private static bool lastWasAllPlayers = false;
        private static bool lastWasAllEnemies = false;

        /// <summary>
        /// Indicates whether target selection is currently active.
        /// Used by BattleCommandPatches to suppress command announcements during targeting.
        /// </summary>
        public static bool IsTargetSelectionActive { get; private set; } = false;

        /// <summary>
        /// Resets the announcement state. Called when entering/exiting target selection.
        /// </summary>
        public static void ResetState()
        {
            lastWasAllPlayers = false;
            lastWasAllEnemies = false;
        }

        /// <summary>
        /// Sets the target selection active state.
        /// </summary>
        public static void SetTargetSelectionActive(bool active)
        {
            IsTargetSelectionActive = active;
            MelonLogger.Msg($"[Battle Target] Target selection active: {active}");
        }

        /// <summary>
        /// Announces all players being targeted.
        /// </summary>
        public static void AnnounceAllPlayers()
        {
            if (lastWasAllPlayers) return;
            lastWasAllPlayers = true;
            lastWasAllEnemies = false;

            // Reset individual target tracking
            BattleTargetSelectController_SelectContent_Player_Patch.lastAnnouncedIndex = -1;
            BattleTargetSelectController_SelectContent_Player_Patch.lastAnnouncement = "";

            MelonLogger.Msg("[Battle Target] All allies");
            FFIV_ScreenReaderMod.SpeakText("All allies");
        }

        /// <summary>
        /// Announces all enemies being targeted.
        /// </summary>
        public static void AnnounceAllEnemies()
        {
            if (lastWasAllEnemies) return;
            lastWasAllEnemies = true;
            lastWasAllPlayers = false;

            // Reset individual target tracking
            BattleTargetSelectController_SelectContent_Enemy_Patch.lastAnnouncedIndex = -1;

            MelonLogger.Msg("[Battle Target] All enemies");
            FFIV_ScreenReaderMod.SpeakText("All enemies");
        }
    }

    /// <summary>
    /// Patch to set IsTargetSelectionActive when target selection window shows/hides.
    /// </summary>
    [HarmonyPatch(typeof(BattleTargetSelectController), nameof(BattleTargetSelectController.ShowWindow))]
    public static class BattleTargetSelectController_ShowWindow_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BattleTargetSelectController __instance, bool isShow)
        {
            try
            {
                MelonLogger.Msg($"[Battle Target] ShowWindow({isShow})");
                BattleTargetPatches.SetTargetSelectionActive(isShow);
                BattleTargetPatches.ResetState();

                // Reset individual target tracking when window state changes
                if (isShow)
                {
                    BattleTargetSelectController_SelectContent_Enemy_Patch.lastAnnouncedIndex = -1;
                    BattleTargetSelectController_SelectContent_Player_Patch.lastAnnouncedIndex = -1;
                    BattleTargetSelectController_SelectContent_Player_Patch.lastAnnouncement = "";
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ShowWindow patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch for PlayerAllInit - called when all players are targeted (e.g., party-wide heal).
    /// </summary>
    [HarmonyPatch(typeof(BattleTargetSelectController), nameof(BattleTargetSelectController.PlayerAllInit))]
    public static class BattleTargetSelectController_PlayerAllInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BattleTargetSelectController __instance)
        {
            try
            {
                MelonLogger.Msg("[Battle Target] PlayerAllInit called");
                BattleTargetPatches.AnnounceAllPlayers();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in PlayerAllInit patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch for EnemyAllInit - called when all enemies are targeted (e.g., group spells).
    /// </summary>
    [HarmonyPatch(typeof(BattleTargetSelectController), nameof(BattleTargetSelectController.EnemyAllInit))]
    public static class BattleTargetSelectController_EnemyAllInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BattleTargetSelectController __instance)
        {
            try
            {
                MelonLogger.Msg("[Battle Target] EnemyAllInit called");
                BattleTargetPatches.AnnounceAllEnemies();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in EnemyAllInit patch: {ex.Message}");
            }
        }
    }
}
