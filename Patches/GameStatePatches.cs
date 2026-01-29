using System;
using HarmonyLib;
using MelonLoader;
using FFIV_ScreenReader.Core;
using FFIV_ScreenReader.Utils;
using FFIV_ScreenReader.Field;
using SubSceneManagerMainGame = Il2CppLast.Management.SubSceneManagerMainGame;
using UserDataManager = Il2CppLast.Management.UserDataManager;

namespace FFIV_ScreenReader.Patches
{
    /// <summary>
    /// Patches for game state transitions (field, battle, menu, etc.).
    /// Hooks SubSceneManagerMainGame.ChangeState for event-driven map transition
    /// instead of per-frame polling.
    /// </summary>
    public static class GameStatePatches
    {
        // Field states that indicate player is on the field map
        private const int STATE_CHANGE_MAP = 1;
        private const int STATE_FIELD_READY = 2;
        private const int STATE_PLAYER = 3;
        private const int STATE_BATTLE = 13;

        private static int lastAnnouncedMapId = -1;

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Patch SubSceneManagerMainGame.ChangeState(State state)
                var changeStateMethod = AccessTools.Method(
                    typeof(SubSceneManagerMainGame),
                    "ChangeState",
                    new Type[] { typeof(SubSceneManagerMainGame.State) }
                );

                if (changeStateMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(GameStatePatches), nameof(ChangeState_Postfix));
                    harmony.Patch(changeStateMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[GameState] Patched SubSceneManagerMainGame.ChangeState (event-driven map transitions)");
                }
                else
                {
                    MelonLogger.Warning("[GameState] Could not find SubSceneManagerMainGame.ChangeState method");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[GameState] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Called when game state changes (field, battle, menu, etc.).
        /// Handles map transition announcements and battle state clearing.
        /// </summary>
        public static void ChangeState_Postfix(SubSceneManagerMainGame.State state)
        {
            try
            {
                int stateValue = (int)state;

                // When transitioning to field states, check for map changes and clear battle state
                if (stateValue == STATE_FIELD_READY || stateValue == STATE_PLAYER || stateValue == STATE_CHANGE_MAP)
                {
                    // Clear battle state when returning to field
                    if (BattleState.IsInBattle)
                    {
                        MelonLogger.Msg($"[GameState] Clearing battle state on transition to {state}");
                        BattleState.Reset();
                    }

                    // Check for map transition
                    CheckMapTransition();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[GameState] Error in ChangeState_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks for map transitions and triggers entity rescan when map changes.
        /// Announces new map name and clears stale entity cache.
        /// </summary>
        private static void CheckMapTransition()
        {
            try
            {
                var userDataManager = UserDataManager.Instance();
                if (userDataManager == null)
                    return;

                int currentMapId = userDataManager.CurrentMapId;

                if (currentMapId != lastAnnouncedMapId && lastAnnouncedMapId != -1)
                {
                    // Map has changed - announce new map
                    string mapName = MapNameResolver.GetCurrentMapName();
                    string fullMessage = $"Entering {mapName}";

                    FFIV_ScreenReaderMod.SpeakText(fullMessage, interrupt: false);
                    lastAnnouncedMapId = currentMapId;

                    // Record for deduplication with SystemMessage
                    LocationMessageTracker.SetLastFadeMessage(fullMessage);

                    // Check if entering interior map - if so, switch to on-foot state
                    bool isWorldMap = FFIV_ScreenReaderMod.Instance?.IsCurrentMapWorldMap() ?? false;
                    MoveStateHelper.OnMapTransition(isWorldMap);

                    // Clear VehicleTypeMap to prevent stale vehicle entries
                    FieldNavigationHelper.ResetVehicleTypeMap();

                    // Force entity rescan to clear stale entities from previous map
                    FFIV_ScreenReaderMod.Instance?.ForceEntityRescan();

                    MelonLogger.Msg($"[GameState] Map changed to {mapName} (ID: {currentMapId}), entities rescanned");
                }
                else if (lastAnnouncedMapId == -1)
                {
                    // First run - store current map without announcing
                    lastAnnouncedMapId = currentMapId;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[GameState] Error in CheckMapTransition: {ex.Message}");
            }
        }

        /// <summary>
        /// Reset the map tracking state (called on game reset/title screen).
        /// </summary>
        public static void ResetMapTracking()
        {
            lastAnnouncedMapId = -1;
        }
    }
}
