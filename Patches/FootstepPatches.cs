using System;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Il2CppLast.Map;
using Il2CppLast.Management;
using Il2CppLast.Entity.Field;
using FFIV_ScreenReader.Utils;
using FFIV_ScreenReader.Core;

namespace FFIV_ScreenReader.Patches
{
    /// <summary>
    /// Plays a footstep sound each time the player steps onto a new tile.
    ///
    /// Hook (2026-09-24): FieldController.OnPlayerMoveFinished(FieldEntity) (RVA 0x289400, unique),
    /// the controlled player's move-finished callback. FieldController.SetControlPlayer registers it
    /// with FieldEntity.AddDelegateMoveFinished, and its body runs the per-step logic (foot monitors,
    /// hidden passages, UpdateStateSwitchLandable), so it fires once per completed step. It replaces
    /// a prefix on the per-frame input callback FieldPlayerKeyController.OnTouchPadCallback that
    /// started a coroutine waiting 0.08 s (WaitForSeconds) before comparing positions.
    ///
    /// The old hook only ran on player input, so steps are gated on the game's own Player state
    /// (SubSceneManagerMainGame.State.Player = 3): scripted moves in events don't click.
    /// Does NOT include wall bump logic - FF4 uses OnPlayerHitCollider for that.
    /// </summary>
    [HarmonyPatch]
    public static class FootstepPatches
    {
        private const float TILE_SIZE = Constants.CellSize;
        private const float FOOTSTEP_COOLDOWN = 0.15f;
        private const int MAIN_GAME_STATE_PLAYER = 3;

        private static float lastFootstepTime = 0f;
        private static Vector2Int lastTilePosition = Vector2Int.zero;
        private static bool tileTrackingInitialized = false;

        /// <summary>
        /// Converts world position to tile coordinates.
        /// </summary>
        private static Vector2Int GetTilePosition(Vector3 worldPos)
        {
            return new Vector2Int(
                Mathf.FloorToInt(worldPos.x / TILE_SIZE),
                Mathf.FloorToInt(worldPos.y / TILE_SIZE)
            );
        }

        /// <summary>True unless the game's main sub-scene is readable and not in its Player state.</summary>
        private static bool IsPlayerControlled()
        {
            try
            {
                var sub = Il2CppLast.Management.SceneManager.Instance?.GetCurrentSubSceneManager();
                var mainGame = sub?.TryCast<SubSceneManagerMainGame>();
                if (mainGame == null) return true;
                return (int)mainGame.GetCurrentState() == MAIN_GAME_STATE_PLAYER;
            }
            catch
            {
                return true; // state unreadable: keep footsteps working
            }
        }

        /// <summary>
        /// Postfix on FieldController.OnPlayerMoveFinished: the player finished a move.
        /// </summary>
        [HarmonyPatch(typeof(FieldController), nameof(FieldController.OnPlayerMoveFinished))]
        [HarmonyPostfix]
        private static void OnPlayerMoveFinished_Postfix(FieldEntity __0)
        {
            try
            {
                if (!AudioLoopManager.FootstepsEnabled)
                    return;

                if (__0 == null || __0.transform == null)
                    return;

                Vector2Int currentTile = GetTilePosition(__0.transform.localPosition);

                // Initialize tile tracking if needed
                if (!tileTrackingInitialized)
                {
                    lastTilePosition = currentTile;
                    tileTrackingInitialized = true;
                    return;
                }

                if (currentTile == lastTilePosition)
                    return;
                lastTilePosition = currentTile;

                if (!IsPlayerControlled())
                    return;

                float currentTime = Time.time;
                if (currentTime - lastFootstepTime >= FOOTSTEP_COOLDOWN)
                {
                    SoundPlayer.PlayFootstep();
                    lastFootstepTime = currentTime;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in FootstepPatches.OnPlayerMoveFinished_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Resets all static state. Called on map transitions.
        /// </summary>
        public static void ResetState()
        {
            lastFootstepTime = 0f;
            lastTilePosition = Vector2Int.zero;
            tileTrackingInitialized = false;
        }
    }
}
