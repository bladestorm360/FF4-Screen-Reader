using System;
using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using FFIV_ScreenReader.Core;
using FFIV_ScreenReader.Utils;

// Type aliases for IL2CPP types - Base
using BasePopup = Il2CppLast.UI.Popup;
using GameCursor = Il2CppLast.UI.Cursor;

// Type aliases for IL2CPP types - KeyInput Popups
using KeyInputCommonPopup = Il2CppLast.UI.KeyInput.CommonPopup;
using KeyInputGameOverSelectPopup = Il2CppLast.UI.KeyInput.GameOverSelectPopup;
using KeyInputInfomationPopup = Il2CppLast.UI.KeyInput.InfomationPopup;
using KeyInputShopController = Il2CppLast.UI.KeyInput.ShopController;
using KeyInputTitleWindowController = Il2CppLast.UI.KeyInput.TitleWindowController;

// Type aliases for IL2CPP types - Touch Popups
using TouchCommonPopup = Il2CppLast.UI.Touch.CommonPopup;
using TouchGameOverSelectPopup = Il2CppLast.UI.Touch.GameOverSelectPopup;

// Splash/Title screen
using SplashController = Il2CppLast.UI.SplashController;

namespace FFIV_ScreenReader.Patches
{
    /// <summary>
    /// Tracks popup state for handling in CursorNavigation.
    /// </summary>
    public static class PopupState
    {
        /// <summary>
        /// True when a confirmation popup is active.
        /// </summary>
        public static bool IsConfirmationPopupActive { get; private set; }

        /// <summary>
        /// The type name of the current popup.
        /// </summary>
        public static string CurrentPopupType { get; private set; }

        /// <summary>
        /// Pointer to the active popup instance.
        /// </summary>
        public static IntPtr ActivePopupPtr { get; private set; }

        /// <summary>
        /// Offset to commandList field (-1 if popup has no buttons).
        /// </summary>
        public static int CommandListOffset { get; private set; }

        public static void SetActive(string typeName, IntPtr ptr, int cmdListOffset)
        {
            IsConfirmationPopupActive = true;
            CurrentPopupType = typeName;
            ActivePopupPtr = ptr;
            CommandListOffset = cmdListOffset;
            MelonLogger.Msg($"[Popup] State set: {typeName}, hasButtons={cmdListOffset >= 0}");
        }

        public static void Clear()
        {
            IsConfirmationPopupActive = false;
            CurrentPopupType = null;
            ActivePopupPtr = IntPtr.Zero;
            CommandListOffset = -1;
        }

        /// <summary>
        /// Returns true if popup with buttons is active (suppress MenuTextDiscovery).
        /// </summary>
        public static bool ShouldSuppress() => IsConfirmationPopupActive && CommandListOffset >= 0;
    }

    /// <summary>
    /// Patches for popup dialogs - handles ALL popup reading (message + buttons).
    /// Uses TryCast for IL2CPP-safe type detection.
    ///
    /// Supported popup types:
    /// - CommonPopup: General confirmations (save/load, return to title, exit game, font/language changes)
    /// - GameOverSelectPopup: Game over options
    /// - InfomationPopup: Info-only (no buttons)
    ///
    /// EXCLUDES: Shop popups (handled by ShopPatches), Save/Load popups (handled by SaveLoadPatches).
    /// </summary>
    public static class PopupPatches
    {
        private static bool isPatched = false;

        // --- Memory offsets for FF4 ---

        // IconTextView.nameText offset
        private const int ICON_TEXT_VIEW_NAME_TEXT_OFFSET = 0x20;

        // CommonCommand.text offset
        private const int COMMON_COMMAND_TEXT_OFFSET = 0x18;

        // KeyInput CommonPopup (from dump.cs line 458986)
        // titleText (IconTextView): 0x38
        // messageText (Text): 0x40
        // commandList (List<CommonCommand>): 0x70
        private const int COMMON_TITLE_OFFSET = 0x38;      // IconTextView
        private const int COMMON_MESSAGE_OFFSET = 0x40;    // Text
        private const int COMMON_CMDLIST_OFFSET = 0x70;    // List<CommonCommand>

        // KeyInput GameOverSelectPopup (from dump.cs line 459177)
        // commandList (List<CommonCommand>): 0x40
        private const int GAMEOVER_CMDLIST_OFFSET = 0x40;

        // KeyInput InfomationPopup (from dump.cs line 459297)
        // Info-only popup, no buttons
        private const int INFO_TITLE_OFFSET = 0x28;        // IconTextView (estimated)
        private const int INFO_MESSAGE_OFFSET = 0x30;      // Text (estimated)

        // Touch CommonPopup (from dump.cs line 423816)
        // titleText (Text): 0x28
        // messageText (Text): 0x38
        private const int TOUCH_COMMON_TITLE_OFFSET = 0x28;
        private const int TOUCH_COMMON_MESSAGE_OFFSET = 0x38;

        /// <summary>
        /// Apply manual Harmony patches for popups.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched)
                return;

            try
            {
                TryPatchBasePopup(harmony);
                TryPatchTitleScreen(harmony);
                isPatched = true;
                MelonLogger.Msg("[Popup] All popup patches applied successfully");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Patch base Popup.Open() and Popup.Close() methods.
        /// </summary>
        private static void TryPatchBasePopup(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type popupType = typeof(BasePopup);

                // Use AccessTools.Method for IL2CPP compatibility
                var openMethod = AccessTools.Method(popupType, "Open");
                if (openMethod != null)
                {
                    var openPostfix = typeof(PopupPatches).GetMethod(nameof(PopupOpen_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(openMethod, postfix: new HarmonyMethod(openPostfix));
                    MelonLogger.Msg("[Popup] Patched base Popup.Open");
                }

                var closeMethod = AccessTools.Method(popupType, "Close");
                if (closeMethod != null)
                {
                    var closePostfix = typeof(PopupPatches).GetMethod(nameof(PopupClose_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(closeMethod, postfix: new HarmonyMethod(closePostfix));
                    MelonLogger.Msg("[Popup] Patched base Popup.Close");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error patching base Popup: {ex.Message}");
            }
        }

        /// <summary>
        /// Patch title screen "Press any button" using combination approach:
        /// 1. SplashController.InitializeTitle - stores text silently (fires early during state init)
        /// 2. TitleWindowController.SetEnableStartObject - speaks stored text (fires when start text is shown)
        /// </summary>
        private static void TryPatchTitleScreen(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Step 1: Patch SplashController.InitializeTitle to capture and store the text
                Type splashControllerType = typeof(SplashController);
                var initTitleMethod = AccessTools.Method(splashControllerType, "InitializeTitle");

                if (initTitleMethod != null)
                {
                    var postfix = typeof(PopupPatches).GetMethod(nameof(SplashController_InitializeTitle_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(initTitleMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Popup] Patched SplashController.InitializeTitle (stores text)");
                }
                else
                {
                    MelonLogger.Warning("[Popup] SplashController.InitializeTitle method not found");
                }

                // Step 2: Patch TitleWindowController.SetEnableStartObject to speak the stored text
                // This is called when the "Press any button" text is enabled/shown
                Type titleWindowControllerType = typeof(KeyInputTitleWindowController);
                var setEnableStartMethod = AccessTools.Method(titleWindowControllerType, "SetEnableStartObject");

                if (setEnableStartMethod != null)
                {
                    var postfix = typeof(PopupPatches).GetMethod(nameof(TitleWindowController_SetEnableStartObject_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(setEnableStartMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Popup] Patched TitleWindowController.SetEnableStartObject (speaks text when shown)");
                }
                else
                {
                    MelonLogger.Warning("[Popup] TitleWindowController.SetEnableStartObject method not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error patching title screen: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if shop is active.
        /// </summary>
        private static bool IsShopActive()
        {
            try
            {
                var shopController = UnityEngine.Object.FindObjectOfType<KeyInputShopController>();
                return shopController != null && shopController.IsOpne;
            }
            catch
            {
                return false;
            }
        }

        #region Text Reading Helpers

        private static string ReadTextFromPointer(IntPtr textPtr)
        {
            if (textPtr == IntPtr.Zero) return null;
            try
            {
                var text = new Text(textPtr);
                return text?.text;
            }
            catch { return null; }
        }

        private static string ReadIconTextViewText(IntPtr iconTextViewPtr)
        {
            if (iconTextViewPtr == IntPtr.Zero) return null;
            try
            {
                IntPtr nameTextPtr = Marshal.ReadIntPtr(iconTextViewPtr + ICON_TEXT_VIEW_NAME_TEXT_OFFSET);
                return ReadTextFromPointer(nameTextPtr);
            }
            catch { return null; }
        }

        private static string BuildAnnouncement(string title, string message)
        {
            title = string.IsNullOrWhiteSpace(title) ? null : TextUtils.StripIconMarkup(title.Trim());
            message = string.IsNullOrWhiteSpace(message) ? null : TextUtils.StripIconMarkup(message.Trim());

            if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(message))
                return $"{title}. {message}";
            else if (!string.IsNullOrEmpty(title))
                return title;
            else if (!string.IsNullOrEmpty(message))
                return message;
            return null;
        }

        #endregion

        #region Type-Specific Readers

        private static string ReadCommonPopup(IntPtr ptr)
        {
            IntPtr titleViewPtr = Marshal.ReadIntPtr(ptr + COMMON_TITLE_OFFSET);
            string title = ReadIconTextViewText(titleViewPtr);
            IntPtr messagePtr = Marshal.ReadIntPtr(ptr + COMMON_MESSAGE_OFFSET);
            string message = ReadTextFromPointer(messagePtr);
            return BuildAnnouncement(title, message);
        }

        private static string ReadGameOverSelectPopup(IntPtr ptr)
        {
            // GameOverSelectPopup has no title/message, just buttons
            // Announce "Game Over" as context
            return "Game Over";
        }

        private static string ReadInfomationPopup(IntPtr ptr)
        {
            IntPtr titleViewPtr = Marshal.ReadIntPtr(ptr + INFO_TITLE_OFFSET);
            string title = ReadIconTextViewText(titleViewPtr);
            IntPtr messagePtr = Marshal.ReadIntPtr(ptr + INFO_MESSAGE_OFFSET);
            string message = ReadTextFromPointer(messagePtr);
            return BuildAnnouncement(title, message);
        }

        private static string ReadTouchCommonPopup(IntPtr ptr)
        {
            IntPtr titlePtr = Marshal.ReadIntPtr(ptr + TOUCH_COMMON_TITLE_OFFSET);
            string title = ReadTextFromPointer(titlePtr);
            IntPtr msgPtr = Marshal.ReadIntPtr(ptr + TOUCH_COMMON_MESSAGE_OFFSET);
            string msg = ReadTextFromPointer(msgPtr);
            return BuildAnnouncement(title, msg);
        }

        #endregion

        #region Button Reading

        /// <summary>
        /// Read current button label from active popup.
        /// Called by CursorNavigation_Postfix when popup is active.
        /// </summary>
        public static void ReadCurrentButton(GameCursor cursor)
        {
            try
            {
                if (PopupState.ActivePopupPtr == IntPtr.Zero)
                {
                    MelonLogger.Msg("[Popup] ReadCurrentButton - no active popup");
                    return;
                }

                if (PopupState.CommandListOffset < 0)
                {
                    MelonLogger.Msg("[Popup] ReadCurrentButton - popup has no buttons");
                    return;
                }

                string buttonText = ReadButtonFromCommandList(
                    PopupState.ActivePopupPtr,
                    PopupState.CommandListOffset,
                    cursor.Index);

                if (!string.IsNullOrWhiteSpace(buttonText))
                {
                    buttonText = TextUtils.StripIconMarkup(buttonText);
                    MelonLogger.Msg($"[Popup] Button: {buttonText}");
                    FFIV_ScreenReaderMod.SpeakText(buttonText, interrupt: true);
                }
                else
                {
                    MelonLogger.Msg($"[Popup] No button text at index {cursor.Index}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error reading button: {ex.Message}");
            }
        }

        private static string ReadButtonFromCommandList(IntPtr popupPtr, int cmdListOffset, int index)
        {
            try
            {
                IntPtr listPtr = Marshal.ReadIntPtr(popupPtr + cmdListOffset);
                if (listPtr == IntPtr.Zero) return null;

                // IL2CPP List: _size at 0x18, _items at 0x10
                int size = Marshal.ReadInt32(listPtr + 0x18);
                if (index < 0 || index >= size) return null;

                IntPtr itemsPtr = Marshal.ReadIntPtr(listPtr + 0x10);
                if (itemsPtr == IntPtr.Zero) return null;

                // Array elements start at 0x20, 8 bytes per pointer
                IntPtr commandPtr = Marshal.ReadIntPtr(itemsPtr + 0x20 + (index * 8));
                if (commandPtr == IntPtr.Zero) return null;

                // CommonCommand.text at offset 0x18
                IntPtr textPtr = Marshal.ReadIntPtr(commandPtr + COMMON_COMMAND_TEXT_OFFSET);
                return ReadTextFromPointer(textPtr);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error reading command list: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Popup Open/Close Postfixes

        /// <summary>
        /// Postfix for base Popup.Open - uses TryCast for type detection.
        /// </summary>
        public static void PopupOpen_Postfix(BasePopup __instance)
        {
            try
            {
                MelonLogger.Msg($"[Popup] ========== PopupOpen_Postfix CALLED ==========");
                MelonLogger.Msg($"[Popup] Instance pointer: 0x{__instance?.Pointer.ToInt64():X}");

                if (__instance == null)
                {
                    MelonLogger.Msg("[Popup] Instance is null, returning");
                    return;
                }
                if (IsShopActive())
                {
                    MelonLogger.Msg("[Popup] Skipping - shop is active");
                    return;
                }

                MelonLogger.Msg("[Popup] Starting TryCast type detection...");

                // KeyInput types first (more common for keyboard/gamepad)

                // CommonPopup - general confirmations
                MelonLogger.Msg("[Popup] TryCast: KeyInputCommonPopup...");
                var commonPopup = __instance.TryCast<KeyInputCommonPopup>();
                MelonLogger.Msg($"[Popup] TryCast result: {(commonPopup != null ? "MATCH" : "null")}");
                if (commonPopup != null)
                {
                    HandlePopupDetected("CommonPopup", commonPopup.Pointer, COMMON_CMDLIST_OFFSET,
                        () => ReadCommonPopup(commonPopup.Pointer));
                    return;
                }

                // GameOverSelectPopup
                MelonLogger.Msg("[Popup] TryCast: KeyInputGameOverSelectPopup...");
                var gameOver = __instance.TryCast<KeyInputGameOverSelectPopup>();
                MelonLogger.Msg($"[Popup] TryCast result: {(gameOver != null ? "MATCH" : "null")}");
                if (gameOver != null)
                {
                    HandlePopupDetected("GameOverSelectPopup", gameOver.Pointer, GAMEOVER_CMDLIST_OFFSET,
                        () => ReadGameOverSelectPopup(gameOver.Pointer));
                    return;
                }

                // InfomationPopup (no buttons)
                MelonLogger.Msg("[Popup] TryCast: KeyInputInfomationPopup...");
                var info = __instance.TryCast<KeyInputInfomationPopup>();
                MelonLogger.Msg($"[Popup] TryCast result: {(info != null ? "MATCH" : "null")}");
                if (info != null)
                {
                    HandlePopupDetected("InfomationPopup", info.Pointer, -1,
                        () => ReadInfomationPopup(info.Pointer));
                    return;
                }

                // Touch types (fallback)
                MelonLogger.Msg("[Popup] TryCast: TouchCommonPopup...");
                var touchCommon = __instance.TryCast<TouchCommonPopup>();
                MelonLogger.Msg($"[Popup] TryCast result: {(touchCommon != null ? "MATCH" : "null")}");
                if (touchCommon != null)
                {
                    // Touch CommonPopup uses SimpleButton, not commandList
                    HandlePopupDetected("TouchCommonPopup", touchCommon.Pointer, -1,
                        () => ReadTouchCommonPopup(touchCommon.Pointer));
                    return;
                }

                MelonLogger.Msg("[Popup] TryCast: TouchGameOverSelectPopup...");
                var touchGameOver = __instance.TryCast<TouchGameOverSelectPopup>();
                MelonLogger.Msg($"[Popup] TryCast result: {(touchGameOver != null ? "MATCH" : "null")}");
                if (touchGameOver != null)
                {
                    HandlePopupDetected("TouchGameOverSelectPopup", touchGameOver.Pointer, -1,
                        () => "Game Over");
                    return;
                }

                // Unknown popup type
                MelonLogger.Msg($"[Popup] Unknown popup type opened (base Popup)");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in Open postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle a detected popup - set state and start delayed read.
        /// </summary>
        private static void HandlePopupDetected(string typeName, IntPtr ptr, int cmdListOffset, Func<string> readFunc)
        {
            MelonLogger.Msg($"[Popup] Detected: {typeName}");
            PopupState.SetActive(typeName, ptr, cmdListOffset);
            CoroutineManager.StartManaged(DelayedPopupRead(ptr, typeName, readFunc));
        }

        /// <summary>
        /// Coroutine to read popup text after 1 frame delay.
        /// </summary>
        private static IEnumerator DelayedPopupRead(IntPtr popupPtr, string typeName, Func<string> readFunc)
        {
            yield return null; // Wait 1 frame

            try
            {
                if (popupPtr == IntPtr.Zero) yield break;

                string announcement = readFunc();
                if (!string.IsNullOrEmpty(announcement))
                {
                    MelonLogger.Msg($"[Popup] {typeName}: {announcement}");
                    FFIV_ScreenReaderMod.SpeakText(announcement, interrupt: false);
                }
                else
                {
                    MelonLogger.Msg($"[Popup] {typeName} - no text found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in delayed read: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for base Popup.Close - clears state.
        /// </summary>
        public static void PopupClose_Postfix()
        {
            try
            {
                if (PopupState.IsConfirmationPopupActive)
                {
                    MelonLogger.Msg($"[Popup] Close - clearing state for {PopupState.CurrentPopupType}");
                    PopupState.Clear();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in Close postfix: {ex.Message}");
            }
        }

        #endregion

        #region Title Screen (Press Any Button) - Combination Approach

        /// <summary>
        /// Stores the "Press any button" text captured during InitializeTitle.
        /// Spoken later when SetEnableStartObject is called with true.
        /// </summary>
        private static string pendingTitleText = null;

        /// <summary>
        /// Postfix for SplashController.InitializeTitle.
        /// Called when entering the Title state - captures and stores the text but does NOT speak.
        /// The text will be spoken later by SetEnableStartObject when the start text is shown.
        /// </summary>
        public static void SplashController_InitializeTitle_Postfix(SplashController __instance)
        {
            try
            {
                MelonLogger.Msg("[Popup] SplashController_InitializeTitle_Postfix called - storing text silently");

                if (__instance == null)
                {
                    MelonLogger.Msg("[Popup] SplashController is null");
                    return;
                }

                // Try to read the localized "Press any button" text from UiMessageConstants
                string pressText = null;

                try
                {
                    // Access UiMessageConstants via reflection since it's in root namespace
                    var uiMsgType = Type.GetType("Il2CppUiMessageConstants, Assembly-CSharp")
                                 ?? Type.GetType("UiMessageConstants, Assembly-CSharp");

                    if (uiMsgType != null)
                    {
                        var field = uiMsgType.GetField("MENU_TITLE_PRESS_TEXT", BindingFlags.Public | BindingFlags.Static);
                        if (field != null)
                        {
                            pressText = field.GetValue(null) as string;
                            MelonLogger.Msg($"[Popup] MENU_TITLE_PRESS_TEXT = \"{pressText}\"");
                        }
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Msg($"[Popup] Could not read MENU_TITLE_PRESS_TEXT: {ex.Message}");
                }

                if (!string.IsNullOrWhiteSpace(pressText))
                {
                    pendingTitleText = TextUtils.StripIconMarkup(pressText.Trim());
                    MelonLogger.Msg($"[Popup] Stored pending title text: {pendingTitleText}");
                }
                else
                {
                    // Fallback to hardcoded text
                    pendingTitleText = "Press any button";
                    MelonLogger.Msg("[Popup] Using fallback, stored: Press any button");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in SplashController.InitializeTitle postfix: {ex.Message}");
                pendingTitleText = "Press any button";
            }
        }

        /// <summary>
        /// Postfix for TitleWindowController.SetEnableStartObject.
        /// Called when the "Press any button" start object is enabled/disabled.
        /// Only speaks when isEnable is true (showing the text).
        /// </summary>
        public static void TitleWindowController_SetEnableStartObject_Postfix(bool isEnable)
        {
            try
            {
                MelonLogger.Msg($"[Popup] TitleWindowController_SetEnableStartObject_Postfix called with isEnable={isEnable}");

                // Only speak when the start object is being enabled (text shown)
                if (!isEnable)
                {
                    MelonLogger.Msg("[Popup] SetEnableStartObject=false, skipping announcement");
                    return;
                }

                if (!string.IsNullOrWhiteSpace(pendingTitleText))
                {
                    MelonLogger.Msg($"[Popup] Speaking stored title text: {pendingTitleText}");
                    FFIV_ScreenReaderMod.SpeakText(pendingTitleText, interrupt: false);
                    pendingTitleText = null; // Clear after speaking
                }
                else
                {
                    // Fallback if text wasn't stored for some reason
                    MelonLogger.Msg("[Popup] No pending text, using fallback: Press any button");
                    FFIV_ScreenReaderMod.SpeakText("Press any button", interrupt: false);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in SetEnableStartObject postfix: {ex.Message}");
                FFIV_ScreenReaderMod.SpeakText("Press any button", interrupt: false);
            }
        }

        #endregion
    }
}
