using System;
using System.Collections;
using HarmonyLib;
using MelonLoader;
using Il2CppLast.UI.KeyInput;
using Il2CppLast.Data.User;
using Il2CppSerial.FF4.UI.KeyInput;
using Il2CppSerial.Template.UI.KeyInput;
using FFIV_ScreenReader.Core;
using FFIV_ScreenReader.Menus;
using FFIV_ScreenReader.Utils;
using Il2CppSystem.Collections.Generic;
using UnityEngine;
using StatusDetailsController = Il2CppSerial.FF4.UI.KeyInput.StatusDetailsController;

namespace FFIV_ScreenReader.Patches
{
    /// <summary>
    /// Tracks whether the status menu was opened by user action.
    /// Prevents status announcements during game initialization.
    /// </summary>
    public static class StatusMenuTracker
    {
        /// <summary>
        /// Set to true when user explicitly opens the status menu.
        /// This prevents InitDisplay from announcing during map load.
        /// </summary>
        public static bool IsUserOpened { get; set; } = false;

        /// <summary>
        /// Time of last user interaction with status menu.
        /// Used for timeout-based auto-reset.
        /// </summary>
        private static float lastInteractionTime = 0f;

        /// <summary>
        /// Mark that user has opened the status menu.
        /// </summary>
        public static void MarkUserOpened()
        {
            IsUserOpened = true;
            lastInteractionTime = UnityEngine.Time.time;
        }

        /// <summary>
        /// Reset the tracker (e.g., when leaving status menu).
        /// </summary>
        public static void Reset()
        {
            IsUserOpened = false;
        }

        /// <summary>
        /// Check if menu access should be considered user-initiated.
        /// Auto-resets after 5 seconds of no interaction.
        /// </summary>
        public static bool ShouldAnnounce()
        {
            // Auto-reset after 5 seconds
            if (IsUserOpened && (UnityEngine.Time.time - lastInteractionTime) > 5f)
            {
                IsUserOpened = false;
            }
            return IsUserOpened;
        }
    }

    /// <summary>
    /// Tracks navigation state within the status screen for arrow key navigation.
    /// Singleton pattern for global access from InputManager.
    /// </summary>
    public class StatusNavigationTracker
    {
        private static StatusNavigationTracker instance = null;
        public static StatusNavigationTracker Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new StatusNavigationTracker();
                }
                return instance;
            }
        }

        public bool IsNavigationActive { get; set; }
        public int CurrentStatIndex { get; set; }
        public OwnedCharacterData CurrentCharacterData { get; set; }
        public StatusDetailsController ActiveController { get; set; }

        private StatusNavigationTracker()
        {
            Reset();
        }

        public void Reset()
        {
            IsNavigationActive = false;
            CurrentStatIndex = 0;
            CurrentCharacterData = null;
            ActiveController = null;
        }

        public bool ValidateState()
        {
            return IsNavigationActive &&
                   CurrentCharacterData != null &&
                   ActiveController != null &&
                   ActiveController.gameObject != null &&
                   ActiveController.gameObject.activeInHierarchy;
        }
    }

    /// <summary>
    /// Helper methods for status screen patches.
    /// </summary>
    public static class StatusDetailsHelpers
    {
        /// <summary>
        /// Extract character data from the StatusDetailsController.
        /// </summary>
        public static OwnedCharacterData GetCharacterDataFromController(StatusDetailsController controller)
        {
            try
            {
                var statusController = controller?.statusController;
                if (statusController != null)
                {
                    // Try direct access first
                    try
                    {
                        var targetData = statusController.targetData;
                        if (targetData != null)
                        {
                            MelonLogger.Msg("[Status] Successfully accessed targetData directly");
                            return targetData;
                        }
                    }
                    catch
                    {
                        // Direct access failed, try Traverse
                    }

                    // Try Traverse if field is private
                    try
                    {
                        var traversed = Traverse.Create(statusController).Field("targetData").GetValue<OwnedCharacterData>();
                        if (traversed != null)
                        {
                            MelonLogger.Msg("[Status] Successfully accessed targetData via Traverse");
                            return traversed;
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"[Status] Traverse access failed: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error accessing character data: {ex.Message}");
            }
            return null;
        }
    }

    /// <summary>
    /// Controller-based patches for the character status menu.
    /// Announces character names when navigating the selection list and status details when viewing.
    /// Provides hotkeys for detailed stat announcements ([=physical, ]=magical).
    /// </summary>

    /// <summary>
    /// Patch for character selection list navigation.
    /// Announces character names when navigating up/down in the status character list.
    /// </summary>
    [HarmonyPatch(typeof(StatusWindowController), nameof(StatusWindowController.SelectContent))]
    public static class StatusWindowController_SelectContent_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(StatusWindowController __instance, List<StatusWindowContentControllerBase> contents, int index, Il2CppLast.UI.Cursor targetCursor)
        {
            try
            {
                // Safety checks
                if (__instance == null || contents == null)
                {
                    return;
                }

                if (index < 0 || index >= contents.Count)
                {
                    return;
                }

                // IMPORTANT: Filter out initialization/background calls
                // Only announce when the status window is actually visible and active
                if (__instance.gameObject == null || !__instance.gameObject.activeInHierarchy)
                {
                    return;
                }

                // Also check if the cursor is active - if not, this is likely initialization
                if (targetCursor == null || targetCursor.gameObject == null || !targetCursor.gameObject.activeInHierarchy)
                {
                    return;
                }

                // Mark that user has opened the status menu
                StatusMenuTracker.MarkUserOpened();

                // Use coroutine for one-frame delay to ensure UI has updated
                CoroutineManager.StartManaged(DelayedCharacterAnnouncement(contents, index));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in StatusWindowController.SelectContent patch: {ex.Message}");
            }
        }

        private static IEnumerator DelayedCharacterAnnouncement(List<StatusWindowContentControllerBase> contents, int index)
        {
            // Wait one frame for UI to update
            yield return null;

            try
            {
                if (contents == null || index < 0 || index >= contents.Count)
                {
                    yield break;
                }

                var selectedContent = contents[index];
                if (selectedContent == null)
                {
                    yield break;
                }

                // Use CharacterSelectionReader to get character info from text components
                string characterInfo = CharacterSelectionReader.TryReadCharacterSelection(selectedContent.transform, index);

                if (!string.IsNullOrWhiteSpace(characterInfo))
                {
                    MelonLogger.Msg($"[Status Select] {characterInfo}");
                    FFIV_ScreenReaderMod.SpeakText(characterInfo);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in delayed character announcement: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(StatusDetailsController), nameof(StatusDetailsController.InitDisplay))]
    public static class StatusDetailsController_InitDisplay_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(StatusDetailsController __instance)
        {
            try
            {
                // Safety checks
                if (__instance == null)
                {
                    return;
                }

                // Register the controller in GameObjectCache (always do this)
                Utils.GameObjectCache.Register(__instance);

                // CRITICAL: Only announce if user explicitly opened the status menu
                // This prevents stats being announced during game initialization/map load
                if (!StatusMenuTracker.ShouldAnnounce())
                {
                    return;
                }

                // IMPORTANT: Filter out initialization/background calls
                // Only announce when the status details screen is actually visible
                if (__instance.gameObject == null || !__instance.gameObject.activeInHierarchy)
                {
                    return;
                }

                // Check if we're in a valid menu scene (not during map load)
                // The status screen should have a visible canvas parent
                var canvas = __instance.GetComponentInParent<Canvas>();
                if (canvas == null || !canvas.enabled)
                {
                    return;
                }

                // Use coroutine for one-frame delay to ensure UI has updated
                CoroutineManager.StartManaged(DelayedStatusAnnouncement(__instance));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in StatusDetailsController.InitDisplay patch: {ex.Message}");
            }
        }

        private static IEnumerator DelayedStatusAnnouncement(StatusDetailsController controller)
        {
            // Wait one frame for UI to update
            yield return null;

            try
            {
                if (controller == null)
                {
                    yield break;
                }

                // Double-check visibility after the frame delay
                if (controller.gameObject == null || !controller.gameObject.activeInHierarchy)
                {
                    yield break;
                }

                // Read all status details
                string statusText = StatusDetailsReader.ReadStatusDetails(controller);

                if (string.IsNullOrWhiteSpace(statusText))
                {
                    yield break;
                }

                MelonLogger.Msg($"[Status Details] {statusText}");
                FFIV_ScreenReaderMod.SpeakText(statusText);

                // Initialize navigation state for arrow key browsing
                try
                {
                    var characterData = StatusDetailsHelpers.GetCharacterDataFromController(controller);
                    if (characterData != null)
                    {
                        var tracker = StatusNavigationTracker.Instance;
                        tracker.IsNavigationActive = true;
                        tracker.CurrentStatIndex = 0;  // Start at top
                        tracker.ActiveController = controller;
                        tracker.CurrentCharacterData = characterData;

                        // Also set for existing stat reading methods (hotkeys)
                        StatusDetailsReader.SetCurrentCharacterData(characterData);

                        // Initialize the stat list
                        StatusNavigationReader.InitializeStatList();

                        MelonLogger.Msg("[Status] Navigation initialized - use Up/Down arrows to browse stats");
                    }
                    else
                    {
                        MelonLogger.Warning("[Status] Could not get character data for navigation");
                    }
                }
                catch (Exception navEx)
                {
                    MelonLogger.Warning($"Error initializing navigation: {navEx.Message}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in delayed status announcement: {ex.Message}");
            }
        }
    }

    // NOTE: SetNextPlayer, SetPrevPlayer, and SetParameter methods do not exist in FF4
    // These were FF6-specific methods - removed to prevent patch failures

    /// <summary>
    /// Patch ExitDisplay to clear character data when leaving status screen.
    /// </summary>
    [HarmonyPatch(typeof(StatusDetailsController), nameof(StatusDetailsController.ExitDisplay))]
    public static class StatusDetailsController_ExitDisplay_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                // Clear character data when leaving status screen
                StatusDetailsReader.ClearCurrentCharacterData();

                // Reset navigation state
                StatusNavigationTracker.Instance.Reset();

                // Reset the status menu tracker
                StatusMenuTracker.Reset();

                MelonLogger.Msg("[Status] Menu exited, state cleared");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in StatusDetailsController.ExitDisplay patch: {ex.Message}");
            }
        }
    }

}
