using System;
using System.Collections;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Il2CppLast.Map;
using Il2CppLast.Entity.Field;
using FFIV_ScreenReader.Utils;
using FFIV_ScreenReader.Core;

namespace FFIV_ScreenReader.Patches
{
    /// <summary>
    /// Patches for playing footstep sounds during player movement.
    /// Hooks FieldPlayerKeyController.OnTouchPadCallback to detect tile changes.
    /// Does NOT include wall bump logic - FF4 uses OnPlayerHitCollider for that.
    /// </summary>
    [HarmonyPatch]
    public static class FootstepPatches
    {
        private const float TILE_SIZE = Constants.CellSize;
        private const float FOOTSTEP_COOLDOWN = 0.15f;

        private static float lastFootstepTime = 0f;
        private static Vector2Int lastTilePosition = Vector2Int.zero;
        private static bool tileTrackingInitialized = false;
        private static bool checkPending = false;

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

        /// <summary>
        /// Prefix patch on FieldPlayerKeyController.OnTouchPadCallback.
        /// Captures pre-movement position and starts a coroutine to check for tile change.
        /// </summary>
        [HarmonyPatch(typeof(FieldPlayerKeyController), nameof(FieldPlayerKeyController.OnTouchPadCallback))]
        [HarmonyPrefix]
        private static void OnTouchPadCallback_Prefix(FieldPlayerKeyController __instance, Vector2 axis)
        {
            try
            {
                // Only check if there's actual movement input
                if (Mathf.Abs(axis.x) < 0.1f && Mathf.Abs(axis.y) < 0.1f)
                    return;

                if (__instance?.fieldPlayer?.transform == null)
                    return;

                // Don't stack coroutines
                if (checkPending)
                    return;

                checkPending = true;
                Vector3 positionBefore = __instance.fieldPlayer.transform.localPosition;
                CoroutineManager.StartManaged(CheckFootstepAfterFrame(__instance.fieldPlayer, positionBefore));
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in FootstepPatches.OnTouchPadCallback_Prefix: {ex.Message}");
            }
        }

        /// <summary>
        /// Coroutine that waits one frame for movement to process, then checks tile change.
        /// </summary>
        private static IEnumerator CheckFootstepAfterFrame(FieldPlayer player, Vector3 positionBefore)
        {
            // Wait for movement animation to complete
            yield return new WaitForSeconds(0.08f);

            try
            {
                if (player == null || player.transform == null)
                {
                    checkPending = false;
                    yield break;
                }

                Vector3 positionAfter = player.transform.localPosition;
                float distanceMoved = Vector3.Distance(positionBefore, positionAfter);

                // Only check footsteps if player actually moved
                if (distanceMoved < 0.1f)
                {
                    checkPending = false;
                    yield break;
                }

                Vector2Int currentTile = GetTilePosition(positionAfter);

                // Initialize tile tracking if needed
                if (!tileTrackingInitialized)
                {
                    lastTilePosition = currentTile;
                    tileTrackingInitialized = true;
                    checkPending = false;
                    yield break;
                }

                if (currentTile != lastTilePosition)
                {
                    lastTilePosition = currentTile;

                    if (AudioLoopManager.FootstepsEnabled)
                    {
                        float currentTime = Time.time;
                        if (currentTime - lastFootstepTime >= FOOTSTEP_COOLDOWN)
                        {
                            SoundPlayer.PlayFootstep();
                            lastFootstepTime = currentTime;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in CheckFootstepAfterFrame: {ex.Message}");
            }
            finally
            {
                checkPending = false;
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
            checkPending = false;
        }
    }
}
