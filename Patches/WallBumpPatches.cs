using System;
using HarmonyLib;
using Il2CppLast.Entity.Field;
using Il2CppLast.Map;
using UnityEngine;
using FFIV_ScreenReader.Core;
using FFIV_ScreenReader.Utils;

namespace FFIV_ScreenReader.Patches
{
    /// <summary>
    /// Patches for playing sound effects when player hits a wall/obstacle.
    /// Uses FieldController.OnPlayerHitCollider which fires on collision events.
    /// Plays procedural wall bump tone via SoundPlayer (distinct from in-game audio).
    /// </summary>
    [HarmonyPatch]
    public static class WallBumpPatches
    {
        // Cooldown to prevent sound spam when holding direction against a wall
        private static float lastBumpTime = 0f;
        private const float BUMP_COOLDOWN = 0.3f; // 300ms

        /// <summary>
        /// Fires when the player collides with an obstacle.
        /// </summary>
        [HarmonyPatch(typeof(FieldController), nameof(FieldController.OnPlayerHitCollider))]
        [HarmonyPostfix]
        private static void OnPlayerHitCollider_Postfix(FieldPlayer playerEntity)
        {
            try
            {
                // Suppress wall bumps during battle or dialogue
                if (BattleState.IsInBattle || DialogueTracker.IsInDialogue)
                    return;

                float currentTime = Time.time;
                if (currentTime - lastBumpTime < BUMP_COOLDOWN)
                    return;

                lastBumpTime = currentTime;
                SoundPlayer.PlayWallBump();
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Error($"Error in OnPlayerHitCollider_Postfix: {ex.Message}");
            }
        }
    }
}
