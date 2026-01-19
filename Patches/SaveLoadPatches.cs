using System;
using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using MelonLoader;
using FFIV_ScreenReader.Core;
using FFIV_ScreenReader.Utils;

// FF4 Save/Load UI types
// All controllers use SavePopup with messageText at 0x40, commandList at 0x60
using TitleLoadController = Il2CppLast.UI.KeyInput.LoadGameWindowController;  // Title screen load (savePopup at 0x58)
using MainMenuLoadController = Il2CppLast.UI.KeyInput.LoadWindowController;   // Main menu load (savePopup at 0x28)
using MainMenuSaveController = Il2CppLast.UI.KeyInput.SaveWindowController;   // Main menu save (savePopup at 0x28)
using InterruptionController = Il2CppLast.UI.KeyInput.InterruptionWindowController;  // QuickSave (savePopup at 0x38)
using SaveListController = Il2CppLast.UI.KeyInput.SaveListController;  // Save slot list navigation
using GameCursor = Il2CppLast.UI.Cursor;

namespace FFIV_ScreenReader.Patches
{
    /// <summary>
    /// Tracks save/load menu state for suppression.
    /// </summary>
    public static class SaveLoadMenuState
    {
        public static bool IsActive { get; set; } = false;
        public static bool IsInConfirmation { get; set; } = false;

        public static bool ShouldSuppress()
        {
            return IsActive && IsInConfirmation;
        }

        public static void ResetState()
        {
            IsActive = false;
            IsInConfirmation = false;
        }
    }

    /// <summary>
    /// Patches for Save/Load confirmation popups.
    ///
    /// Hooks SetPopupActive(bool isEnable) on three controllers:
    /// - LoadGameWindowController (title screen load)
    /// - LoadWindowController (main menu load)
    /// - SaveWindowController (main menu save)
    ///
    /// Hooks SetEnablePopup(bool isEnable) on:
    /// - InterruptionWindowController (QuickSave)
    ///
    /// All use SavePopup with messageText at 0x40, commandList at 0x60.
    /// </summary>
    public static class SaveLoadPatches
    {
        // SavePopup field offsets (from dump.cs line 455076)
        // titleText (Text): 0x38
        // messageText (Text): 0x40
        // commandList (List<CommonCommand>): 0x60
        private const int SAVE_POPUP_TITLE_TEXT_OFFSET = 0x38;
        private const int SAVE_POPUP_MESSAGE_TEXT_OFFSET = 0x40;
        private const int SAVE_POPUP_COMMAND_LIST_OFFSET = 0x60;

        // Controller-specific savePopup field offsets
        private const int TITLE_LOAD_SAVE_POPUP_OFFSET = 0x58;   // LoadGameWindowController.savePopup
        private const int MAIN_MENU_SAVE_POPUP_OFFSET = 0x28;    // Both LoadWindowController and SaveWindowController
        private const int INTERRUPTION_SAVE_POPUP_OFFSET = 0x38; // InterruptionWindowController.savePopup

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Patch SetPopupActive(bool) on save/load controllers
                TryPatchTitleLoad(harmony);
                TryPatchMainMenuLoad(harmony);
                TryPatchMainMenuSave(harmony);

                // Patch SetEnablePopup(bool) on QuickSave controller
                TryPatchInterruption(harmony);

                // Patch SaveListController.SelectContent for slot navigation
                TryPatchSaveListSelectContent(harmony);

                // Patch SetActive(bool) on window controllers to clear state when windows close
                TryPatchTitleLoadSetActive(harmony);
                TryPatchMainMenuLoadSetActive(harmony);
                TryPatchMainMenuSaveSetActive(harmony);

            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[SaveLoad] Failed to apply patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Patches SaveListController.SelectContent for save slot navigation.
        /// </summary>
        private static void TryPatchSaveListSelectContent(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(SaveListController);
                var method = AccessTools.Method(controllerType, "SelectContent");

                if (method != null)
                {
                    var postfix = typeof(SaveLoadPatches).GetMethod(nameof(SaveListSelectContent_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[SaveLoad] SaveListController.SelectContent not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Failed to patch SaveListController.SelectContent: {ex.Message}");
            }
        }

        /// <summary>
        /// Patches LoadGameWindowController.SetPopupActive (title screen load).
        /// </summary>
        private static void TryPatchTitleLoad(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(TitleLoadController);
                var method = AccessTools.Method(controllerType, "SetPopupActive");

                if (method != null)
                {
                    var postfix = typeof(SaveLoadPatches).GetMethod(nameof(TitleLoadSetPopupActive_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[SaveLoad] TitleLoadController.SetPopupActive not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Failed to patch TitleLoadController: {ex.Message}");
            }
        }

        /// <summary>
        /// Patches LoadWindowController.SetPopupActive (main menu load).
        /// </summary>
        private static void TryPatchMainMenuLoad(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(MainMenuLoadController);
                var method = AccessTools.Method(controllerType, "SetPopupActive");

                if (method != null)
                {
                    var postfix = typeof(SaveLoadPatches).GetMethod(nameof(MainMenuLoadSetPopupActive_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[SaveLoad] MainMenuLoadController.SetPopupActive not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Failed to patch MainMenuLoadController: {ex.Message}");
            }
        }

        /// <summary>
        /// Patches SaveWindowController.SetPopupActive (main menu save).
        /// </summary>
        private static void TryPatchMainMenuSave(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(MainMenuSaveController);
                var method = AccessTools.Method(controllerType, "SetPopupActive");

                if (method != null)
                {
                    var postfix = typeof(SaveLoadPatches).GetMethod(nameof(MainMenuSaveSetPopupActive_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[SaveLoad] MainMenuSaveController.SetPopupActive not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Failed to patch MainMenuSaveController: {ex.Message}");
            }
        }

        /// <summary>
        /// Patches InterruptionWindowController.SetEnablePopup (QuickSave).
        /// </summary>
        private static void TryPatchInterruption(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(InterruptionController);
                var method = AccessTools.Method(controllerType, "SetEnablePopup");

                if (method != null)
                {
                    var postfix = typeof(SaveLoadPatches).GetMethod(nameof(InterruptionSetEnablePopup_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[SaveLoad] InterruptionController.SetEnablePopup not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Failed to patch InterruptionController: {ex.Message}");
            }
        }

        /// <summary>
        /// Patches LoadGameWindowController.SetActive (title screen load) to clear state when window closes.
        /// </summary>
        private static void TryPatchTitleLoadSetActive(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(TitleLoadController);
                var method = AccessTools.Method(controllerType, "SetActive", new Type[] { typeof(bool) });

                if (method != null)
                {
                    var postfix = typeof(SaveLoadPatches).GetMethod(nameof(TitleLoadSetActive_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[SaveLoad] TitleLoadController.SetActive not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Failed to patch TitleLoadController.SetActive: {ex.Message}");
            }
        }

        /// <summary>
        /// Patches LoadWindowController.SetActive (main menu load) to clear state when window closes.
        /// </summary>
        private static void TryPatchMainMenuLoadSetActive(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(MainMenuLoadController);
                var method = AccessTools.Method(controllerType, "SetActive", new Type[] { typeof(bool) });

                if (method != null)
                {
                    var postfix = typeof(SaveLoadPatches).GetMethod(nameof(MainMenuLoadSetActive_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[SaveLoad] MainMenuLoadController.SetActive not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Failed to patch MainMenuLoadController.SetActive: {ex.Message}");
            }
        }

        /// <summary>
        /// Patches SaveWindowController.SetActive (main menu save) to clear state when window closes.
        /// </summary>
        private static void TryPatchMainMenuSaveSetActive(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(MainMenuSaveController);
                var method = AccessTools.Method(controllerType, "SetActive", new Type[] { typeof(bool) });

                if (method != null)
                {
                    var postfix = typeof(SaveLoadPatches).GetMethod(nameof(MainMenuSaveSetActive_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[SaveLoad] MainMenuSaveController.SetActive not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Failed to patch MainMenuSaveController.SetActive: {ex.Message}");
            }
        }

        // SaveContentView field offsets (from dump.cs line 454468)
        private const int SAVE_CONTENT_VIEW_SLOT_NAME_TEXT = 0x28;    // Text - "File", "Autosave", etc.
        private const int SAVE_CONTENT_VIEW_SLOT_NUM_TEXT = 0x38;     // Text - slot number
        private const int SAVE_CONTENT_VIEW_CHARA_NAME_TEXT = 0x40;   // Text - character name
        private const int SAVE_CONTENT_VIEW_LEVEL_TEXT = 0x50;        // Text - level value
        private const int SAVE_CONTENT_VIEW_AREA_NAME_TEXT = 0x58;    // Text - location
        private const int SAVE_CONTENT_VIEW_FLOOR_NAME_TEXT = 0x60;   // Text - floor
        private const int SAVE_CONTENT_VIEW_HOUR_TEXT = 0x70;         // Text - hours
        private const int SAVE_CONTENT_VIEW_MINUTE_TEXT = 0x80;       // Text - minutes
        private const int SAVE_CONTENT_VIEW_EMPTY_TEXT = 0x88;        // Text - "Empty"
        private const int SAVE_CONTENT_VIEW_TIMESTAMP_DATE = 0xD0;    // Text - date
        private const int SAVE_CONTENT_VIEW_TIMESTAMP_TIME = 0xD8;    // Text - time

        // SaveListController field offsets
        private const int SAVE_LIST_CONTENT_LIST = 0x68;              // List<SaveContentController>

        // SaveContentController field offset for view (from dump.cs line 454301)
        private const int SAVE_CONTENT_CONTROLLER_VIEW = 0x20;        // SaveContentView

        // ============ Postfix Methods ============

        /// <summary>
        /// Postfix for SaveListController.SelectContent - announces save slot info when navigating.
        /// </summary>
        public static void SaveListSelectContent_Postfix(object __instance, GameCursor targetCursor)
        {
            try
            {
                if (targetCursor == null) return;

                var controller = __instance as SaveListController;
                if (controller == null) return;

                // Check if the save/load window is actually visible (not just initialized in background)
                // During title screen init, SelectContent is called but the window isn't visible yet
                var gameObject = controller.gameObject;
                if (gameObject == null || !gameObject.activeInHierarchy)
                {
                    return;
                }

                // Check if the cursor is visible (indicates user is actually navigating)
                if (targetCursor.gameObject == null || !targetCursor.gameObject.activeInHierarchy)
                {
                    return;
                }

                // Mark that we're in the save/load menu (suppresses MenuTextDiscovery)
                MenuState.ClearOtherMenuStates("SaveLoad");
                SaveLoadMenuState.IsActive = true;

                int index = targetCursor.Index;

                // Start coroutine to read slot after UI updates
                CoroutineManager.StartManaged(ReadSaveSlotDelayed(controller.Pointer, index));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Error in SaveListSelectContent_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Clears the save/load menu active state.
        /// Called on scene change or when backing out of save/load menu.
        /// </summary>
        public static void ClearSaveLoadMenuState()
        {
            if (SaveLoadMenuState.IsActive)
            {
                SaveLoadMenuState.IsActive = false;
                SaveLoadMenuState.IsInConfirmation = false;
            }
        }

        /// <summary>
        /// Coroutine to read save slot info after a frame delay.
        /// </summary>
        private static IEnumerator ReadSaveSlotDelayed(IntPtr controllerPtr, int index)
        {
            yield return null; // Wait 1 frame for UI to update

            try
            {
                string slotInfo = ReadSaveSlotInfo(controllerPtr, index);
                if (!string.IsNullOrEmpty(slotInfo))
                {
                    FFIV_ScreenReaderMod.SpeakText(slotInfo, interrupt: true);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Error reading save slot: {ex.Message}");
            }
        }

        /// <summary>
        /// Reads save slot information from SaveListController.contentList[index].
        /// Format matches visual display: "File 2, 01/17/2026 8:10, Moon, Edge Level 45, Time 13:06"
        /// </summary>
        private static string ReadSaveSlotInfo(IntPtr controllerPtr, int index)
        {
            try
            {
                unsafe
                {
                    // Read contentList from controller (offset 0x68)
                    IntPtr contentListPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + SAVE_LIST_CONTENT_LIST);
                    if (contentListPtr == IntPtr.Zero)
                    {
                        return null;
                    }

                    // IL2CPP List: _size at 0x18, _items at 0x10
                    int size = *(int*)((byte*)contentListPtr.ToPointer() + 0x18);
                    if (index < 0 || index >= size)
                    {
                        return null;
                    }

                    IntPtr itemsPtr = *(IntPtr*)((byte*)contentListPtr.ToPointer() + 0x10);
                    if (itemsPtr == IntPtr.Zero) return null;

                    // Array elements start at 0x20, 8 bytes per pointer
                    IntPtr contentControllerPtr = *(IntPtr*)((byte*)itemsPtr.ToPointer() + 0x20 + (index * 8));
                    if (contentControllerPtr == IntPtr.Zero) return null;

                    // Get SaveContentView from SaveContentController (offset 0x18)
                    IntPtr viewPtr = *(IntPtr*)((byte*)contentControllerPtr.ToPointer() + SAVE_CONTENT_CONTROLLER_VIEW);
                    if (viewPtr == IntPtr.Zero)
                    {
                        return null;
                    }

                    return ReadSaveContentView(viewPtr);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Error reading slot info: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Reads all fields from SaveContentView and formats the announcement.
        /// </summary>
        private static string ReadSaveContentView(IntPtr viewPtr)
        {
            try
            {
                unsafe
                {
                    // Read slot name ("File", "Autosave", "Quicksave")
                    string slotName = ReadTextAtOffset(viewPtr, SAVE_CONTENT_VIEW_SLOT_NAME_TEXT);
                    string slotNum = ReadTextAtOffset(viewPtr, SAVE_CONTENT_VIEW_SLOT_NUM_TEXT);

                    // Build slot identifier (e.g., "File 2" or "Autosave")
                    string slotId = slotName ?? "";
                    if (!string.IsNullOrEmpty(slotNum) && slotName != "Autosave" && slotName != "Quicksave")
                    {
                        slotId = $"{slotName} {slotNum}".Trim();
                    }

                    // Check if empty - read emptyText and check if it has content
                    string emptyText = ReadTextAtOffset(viewPtr, SAVE_CONTENT_VIEW_EMPTY_TEXT);
                    string charaName = ReadTextAtOffset(viewPtr, SAVE_CONTENT_VIEW_CHARA_NAME_TEXT);

                    // If no character name, slot is empty
                    if (string.IsNullOrEmpty(charaName) && !string.IsNullOrEmpty(emptyText))
                    {
                        return $"{slotId}, {emptyText}";
                    }

                    // Read timestamp (date and time)
                    string date = ReadTextAtOffset(viewPtr, SAVE_CONTENT_VIEW_TIMESTAMP_DATE);
                    string time = ReadTextAtOffset(viewPtr, SAVE_CONTENT_VIEW_TIMESTAMP_TIME);

                    // Read location
                    string area = ReadTextAtOffset(viewPtr, SAVE_CONTENT_VIEW_AREA_NAME_TEXT);
                    string floor = ReadTextAtOffset(viewPtr, SAVE_CONTENT_VIEW_FLOOR_NAME_TEXT);

                    // Read character info
                    string level = ReadTextAtOffset(viewPtr, SAVE_CONTENT_VIEW_LEVEL_TEXT);

                    // Read play time
                    string hours = ReadTextAtOffset(viewPtr, SAVE_CONTENT_VIEW_HOUR_TEXT);
                    string minutes = ReadTextAtOffset(viewPtr, SAVE_CONTENT_VIEW_MINUTE_TEXT);

                    // Build announcement matching visual display order:
                    // "File 2, 01/17/2026 8:10, Moon, Edge Level 45, Time 13:06"
                    var parts = new System.Collections.Generic.List<string>();

                    // Slot identifier
                    if (!string.IsNullOrEmpty(slotId))
                        parts.Add(slotId);

                    // Timestamp (date time)
                    if (!string.IsNullOrEmpty(date) && !string.IsNullOrEmpty(time))
                        parts.Add($"{date} {time}");
                    else if (!string.IsNullOrEmpty(date))
                        parts.Add(date);

                    // Location (area + floor if present)
                    if (!string.IsNullOrEmpty(area))
                    {
                        if (!string.IsNullOrEmpty(floor))
                            parts.Add($"{area} {floor}");
                        else
                            parts.Add(area);
                    }

                    // Character name and level
                    if (!string.IsNullOrEmpty(charaName))
                    {
                        if (!string.IsNullOrEmpty(level))
                            parts.Add($"{charaName} Level {level}");
                        else
                            parts.Add(charaName);
                    }

                    // Play time
                    if (!string.IsNullOrEmpty(hours) && !string.IsNullOrEmpty(minutes))
                        parts.Add($"Time {hours}:{minutes}");

                    return string.Join(", ", parts);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Error reading SaveContentView: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Helper to read Text component at a given offset.
        /// </summary>
        private static string ReadTextAtOffset(IntPtr basePtr, int offset)
        {
            try
            {
                unsafe
                {
                    IntPtr textPtr = *(IntPtr*)((byte*)basePtr.ToPointer() + offset);
                    if (textPtr == IntPtr.Zero) return null;

                    var textComponent = new UnityEngine.UI.Text(textPtr);
                    string text = textComponent.text;

                    if (string.IsNullOrWhiteSpace(text)) return null;

                    return StripRichTextTags(text.Trim());
                }
            }
            catch
            {
                return null;
            }
        }

        public static void TitleLoadSetPopupActive_Postfix(object __instance, bool isEnable)
        {
            try
            {
                if (isEnable)
                {
                    var controller = __instance as TitleLoadController;
                    if (controller != null)
                    {
                        ReadSavePopup(controller.Pointer, TITLE_LOAD_SAVE_POPUP_OFFSET, "TitleLoad");
                    }
                }
                else
                {
                    ClearPopupState();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Error in TitleLoadSetPopupActive_Postfix: {ex.Message}");
            }
        }

        public static void MainMenuLoadSetPopupActive_Postfix(object __instance, bool isEnable)
        {
            try
            {
                if (isEnable)
                {
                    var controller = __instance as MainMenuLoadController;
                    if (controller != null)
                    {
                        ReadSavePopup(controller.Pointer, MAIN_MENU_SAVE_POPUP_OFFSET, "MainMenuLoad");
                    }
                }
                else
                {
                    ClearPopupState();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Error in MainMenuLoadSetPopupActive_Postfix: {ex.Message}");
            }
        }

        public static void MainMenuSaveSetPopupActive_Postfix(object __instance, bool isEnable)
        {
            try
            {
                if (isEnable)
                {
                    var controller = __instance as MainMenuSaveController;
                    if (controller != null)
                    {
                        ReadSavePopup(controller.Pointer, MAIN_MENU_SAVE_POPUP_OFFSET, "MainMenuSave");
                    }
                }
                else
                {
                    ClearPopupState();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Error in MainMenuSaveSetPopupActive_Postfix: {ex.Message}");
            }
        }

        public static void InterruptionSetEnablePopup_Postfix(object __instance, bool isEnable)
        {
            try
            {
                if (isEnable)
                {
                    var controller = __instance as InterruptionController;
                    if (controller != null)
                    {
                        ReadSavePopup(controller.Pointer, INTERRUPTION_SAVE_POPUP_OFFSET, "QuickSave");
                    }
                }
                else
                {
                    ClearPopupState();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Error in InterruptionSetEnablePopup_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for LoadGameWindowController.SetActive - clears state when window is deactivated.
        /// </summary>
        public static void TitleLoadSetActive_Postfix(bool isActive)
        {
            try
            {
                if (!isActive && SaveLoadMenuState.IsActive)
                {
                    SaveLoadMenuState.ResetState();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Error in TitleLoadSetActive_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for LoadWindowController.SetActive - clears state when window is deactivated.
        /// </summary>
        public static void MainMenuLoadSetActive_Postfix(bool isActive)
        {
            try
            {
                if (!isActive && SaveLoadMenuState.IsActive)
                {
                    SaveLoadMenuState.ResetState();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Error in MainMenuLoadSetActive_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for SaveWindowController.SetActive - clears state when window is deactivated.
        /// </summary>
        public static void MainMenuSaveSetActive_Postfix(bool isActive)
        {
            try
            {
                if (!isActive && SaveLoadMenuState.IsActive)
                {
                    SaveLoadMenuState.ResetState();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Error in MainMenuSaveSetActive_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Starts a coroutine to read SavePopup message after a short delay.
        /// The delay allows the UI to populate the text before we read it.
        /// </summary>
        private static void ReadSavePopup(IntPtr controllerPtr, int savePopupOffset, string context)
        {
            if (controllerPtr == IntPtr.Zero)
            {
                return;
            }

            try
            {
                unsafe
                {
                    // Read savePopup pointer from controller
                    IntPtr popupPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + savePopupOffset);
                    if (popupPtr == IntPtr.Zero)
                    {
                        return;
                    }

                    // Set state for button navigation immediately
                    SaveLoadMenuState.IsActive = true;
                    SaveLoadMenuState.IsInConfirmation = true;
                    PopupState.SetActive($"{context}Popup", popupPtr, SAVE_POPUP_COMMAND_LIST_OFFSET);

                    // Start coroutine to read text after delay (allows UI to populate)
                    CoroutineManager.StartManaged(ReadPopupTextDelayed(popupPtr, context));
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Error reading {context} popup: {ex.Message}");
            }
        }

        /// <summary>
        /// Coroutine that waits a frame then reads the popup text.
        /// </summary>
        private static IEnumerator ReadPopupTextDelayed(IntPtr popupPtr, string context)
        {
            // Wait 2 frames to let UI populate
            yield return null;
            yield return null;

            try
            {
                unsafe
                {
                    // Try reading title first
                    IntPtr titleTextPtr = *(IntPtr*)((byte*)popupPtr.ToPointer() + SAVE_POPUP_TITLE_TEXT_OFFSET);
                    string title = null;
                    if (titleTextPtr != IntPtr.Zero)
                    {
                        var titleComponent = new UnityEngine.UI.Text(titleTextPtr);
                        title = titleComponent.text;
                    }

                    // Read messageText at offset 0x40
                    IntPtr messageTextPtr = *(IntPtr*)((byte*)popupPtr.ToPointer() + SAVE_POPUP_MESSAGE_TEXT_OFFSET);
                    if (messageTextPtr == IntPtr.Zero)
                    {
                        // Still try to announce title if we have it
                        if (!string.IsNullOrWhiteSpace(title))
                        {
                            title = StripRichTextTags(title);
                            FFIV_ScreenReaderMod.SpeakText(title);
                        }
                        yield break;
                    }

                    var textComponent = new UnityEngine.UI.Text(messageTextPtr);
                    string message = textComponent.text;

                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        // Strip Unity rich text tags (like <color=#ff4040>...</color>)
                        message = StripRichTextTags(message);

                        // Combine title and message if both exist
                        string announcement;
                        if (!string.IsNullOrWhiteSpace(title))
                        {
                            title = StripRichTextTags(title);
                            announcement = $"{title}. {message}";
                        }
                        else
                        {
                            announcement = message;
                        }

                        FFIV_ScreenReaderMod.SpeakText(announcement);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Error reading {context} popup text: {ex.Message}");
            }
        }

        /// <summary>
        /// Strips Unity rich text tags from a string.
        /// Removes tags like <color=#xxxxxx>, </color>, <b>, </b>, etc.
        /// </summary>
        private static string StripRichTextTags(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            // Remove all XML-style tags: <tagname>, </tagname>, <tagname=value>, etc.
            return Regex.Replace(text, @"<[^>]+>", string.Empty);
        }

        private static void ClearPopupState()
        {
            SaveLoadMenuState.ResetState();
            PopupState.Clear();
        }
    }
}
