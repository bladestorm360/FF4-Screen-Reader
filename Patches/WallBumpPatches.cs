using System;
using HarmonyLib;
using Il2CppLast.Entity.Field;
using Il2CppLast.Map;
using Il2CppLast.Management;
using UnityEngine;

namespace FFIV_ScreenReader.Patches
{
    /// <summary>
    /// Patches for playing sound effects when player hits a wall/obstacle.
    /// Uses FieldController.OnPlayerHitCollider which fires on collision events.
    /// </summary>
    [HarmonyPatch]
    public static class WallBumpPatches
    {
        // Sound ID for wall bump
        private static readonly int BUMP_SOUND_ID = 4;

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
                float currentTime = Time.time;
                if (currentTime - lastBumpTime < BUMP_COOLDOWN)
                    return;

                lastBumpTime = currentTime;
                PlayBumpSound();
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Error($"Error in OnPlayerHitCollider_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Plays the wall bump sound effect
        /// </summary>
        private static void PlayBumpSound()
        {
            try
            {
                var audioManager = AudioManager.Instance;
                if (audioManager != null)
                {
                    audioManager.PlaySe(BUMP_SOUND_ID);
                }
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Error($"Error playing bump sound: {ex.Message}");
            }
        }
    }
}
