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
using static FFIV_ScreenReader.Utils.ModTextTranslator;

// Type aliases for IL2CPP types - Base
using BasePopup = Il2CppLast.UI.Popup;
using GameCursor = Il2CppLast.UI.Cursor;

// Type aliases for IL2CPP types - KeyInput Popups
using KeyInputCommonPopup = Il2CppLast.UI.KeyInput.CommonPopup;
using KeyInputGameOverSelectPopup = Il2CppLast.UI.KeyInput.GameOverSelectPopup;
using KeyInputGameOverLoadPopup = Il2CppLast.UI.KeyInput.GameOverLoadPopup;
using KeyInputGameOverPopupController = Il2CppLast.UI.KeyInput.GameOverPopupController;
using KeyInputInfomationPopup = Il2CppLast.UI.KeyInput.InfomationPopup;
using KeyInputShopController = Il2CppLast.UI.KeyInput.ShopController;
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
        public static bool IsConfirmationPopupActive { get; private set; }
        public static string CurrentPopupType { get; private set; }
        public static IntPtr ActivePopupPtr { get; private set; }
        public static int CommandListOffset { get; private set; }

        public static void SetActive(string typeName, IntPtr ptr, int cmdListOffset)
        {
            IsConfirmationPopupActive = true;
            CurrentPopupType = typeName;
            ActivePopupPtr = ptr;
            CommandListOffset = cmdListOffset;
        }

        public static void Clear()
        {
            IsConfirmationPopupActive = false;
            CurrentPopupType = null;
            ActivePopupPtr = IntPtr.Zero;
            CommandListOffset = -1;
        }

        public static bool ShouldSuppress() => IsConfirmationPopupActive && CommandListOffset >= 0;
    }

    /// <summary>
    /// Patches for popup dialogs - handles ALL popup reading (message + buttons).
    /// Uses TryCast for IL2CPP-safe type detection.
    /// </summary>
    public static class PopupPatches
    {
        private static bool isPatched = false;

        // Memory offsets
        private const int ICON_TEXT_VIEW_NAME_TEXT_OFFSET = 0x20;
        private const int COMMON_COMMAND_TEXT_OFFSET = 0x18;
        private const int COMMON_TITLE_OFFSET = 0x38;
        private const int COMMON_MESSAGE_OFFSET = 0x40;
        private const int COMMON_CMDLIST_OFFSET = 0x70;
        private const int GAMEOVER_CMDLIST_OFFSET = 0x40;
        private const int INFO_TITLE_OFFSET = 0x28;
        private const int INFO_MESSAGE_OFFSET = 0x30;
        private const int TOUCH_COMMON_TITLE_OFFSET = 0x28;
        private const int TOUCH_COMMON_MESSAGE_OFFSET = 0x38;

        // GameOverSelectPopup (KeyInput) - line 459177 in dump.cs
        private const int GAMEOVER_SELECT_CURSOR_OFFSET = 0x38;
        // GAMEOVER_CMDLIST_OFFSET = 0x40 (already defined above)

        // GameOverLoadPopup (KeyInput) - line 459515 in dump.cs
        private const int GAMEOVERLOAD_MESSAGE_OFFSET = 0x40;
        private const int GAMEOVERLOAD_SELECT_CURSOR_OFFSET = 0x58;
        private const int GAMEOVERLOAD_CMDLIST_OFFSET = 0x60;

        // GameOverPopupController -> View -> LoadPopup navigation
        private const int GAMEOVERPOPUPCTRL_VIEW_OFFSET = 0x30;
        private const int GAMEOVERPOPUPVIEW_LOADPOPUP_OFFSET = 0x18;

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched)
                return;

            try
            {
                TryPatchBasePopup(harmony);
                // CommonPopup.SetCommandSelectCursor (private, RVA 0x735270, unique): the popup's own
                // focus placement. Callers: Open, ResetCursor, the up/down index callback that
                // UpdateSelect passes to Cursor.NextIndex/PrevIndex (<UpdateSelect>b__2/b__4, one
                // body), and the mouse handler. Not per frame: the per-frame UpdateSelect calls
                // UpdateFocus (colours only), which is why that hook was replaced (2026-09-24).
                PatchHelper.TryPatchPostfix(harmony, typeof(KeyInputCommonPopup), "SetCommandSelectCursor",
                    typeof(PopupPatches), nameof(CommonPopup_SetCommandSelectCursor_Postfix), "[Popup]");
                TryPatchTitleScreen(harmony);
                TryPatchGameOverSelectPopupFocus(harmony);
                TryPatchGameOverLoadPopup(harmony);
                isPatched = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error applying patches: {ex.Message}");
            }
        }

        private static void TryPatchBasePopup(HarmonyLib.Harmony harmony)
        {
            PatchHelper.TryPatchPostfix(harmony, typeof(BasePopup), "Open",
                typeof(PopupPatches), nameof(PopupOpen_Postfix), "[Popup]");

            PatchHelper.TryPatchPostfix(harmony, typeof(BasePopup), "Close",
                typeof(PopupPatches), nameof(PopupClose_Postfix), "[Popup]");
        }

        private static void TryPatchTitleScreen(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Step 1: Patch SplashController.InitializeTitle to capture the text
                Type splashControllerType = typeof(SplashController);
                var initTitleMethod = AccessTools.Method(splashControllerType, "InitializeTitle");

                if (initTitleMethod != null)
                {
                    var postfix = typeof(PopupPatches).GetMethod(nameof(SplashController_InitializeTitle_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(initTitleMethod, postfix: new HarmonyMethod(postfix));
                }

                // Step 2: Patch SystemIndicator.Hide (runtime lookup - internal class)
                Type systemIndicatorType = PatchHelper.FindType("Il2CppLast.Systems.Indicator.SystemIndicator");
                if (systemIndicatorType == null)
                {
                    MelonLogger.Warning("[Popup] SystemIndicator type not found");
                    return;
                }

                PatchHelper.TryPatchPostfix(harmony, systemIndicatorType, "Hide",
                    typeof(PopupPatches), nameof(SystemIndicator_Hide_Postfix), "[Popup]");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error patching title screen: {ex.Message}");
            }
        }

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
            catch (Exception) { return null; }
        }

        private static string ReadIconTextViewText(IntPtr iconTextViewPtr)
        {
            if (iconTextViewPtr == IntPtr.Zero) return null;
            try
            {
                IntPtr nameTextPtr = Marshal.ReadIntPtr(iconTextViewPtr + ICON_TEXT_VIEW_NAME_TEXT_OFFSET);
                return ReadTextFromPointer(nameTextPtr);
            }
            catch (Exception) { return null; }
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
            return T("Game Over");
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

        // Set while a CommonPopup's open read (message + focused button) is pending, so the
        // SetCommandSelectCursor call in the popup's own Open doesn't speak the button first.
        private static bool commonPopupOpenReadPending;

        /// <summary>
        /// Postfix for CommonPopup.SetCommandSelectCursor — the popup's own focus change (open,
        /// reset, every Yes/No move). Reads the focused button for every KeyInput CommonPopup.
        /// (The save-overwrite confirmation is a SavePopup, not a CommonPopup: SaveLoadPatches
        /// reads its Yes/No.) Calls made while the popup is hidden (setup) don't speak.
        /// </summary>
        public static void CommonPopup_SetCommandSelectCursor_Postfix(KeyInputCommonPopup __instance)
        {
            try
            {
                if (__instance == null || commonPopupOpenReadPending)
                    return;

                var go = __instance.gameObject;
                if (go == null || !go.activeInHierarchy)
                    return;

                int index = __instance.selectCursor?.Index ?? -1;
                if (!AnnouncementDeduplicator.ShouldAnnounce(AnnouncementContexts.COMMON_POPUP_BUTTON, index))
                    return;

                string buttonText = ReadButtonFromCommandList(__instance.Pointer, COMMON_CMDLIST_OFFSET, index);
                if (!string.IsNullOrWhiteSpace(buttonText))
                    FFIV_ScreenReaderMod.SpeakText(TextUtils.StripIconMarkup(buttonText), interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in CommonPopup.SetCommandSelectCursor postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Open read for a CommonPopup: title/message followed by the initially focused button.
        /// In a shop (whose own patches read the popup's context) only the focused button is read,
        /// as the per-frame focus hook this replaced used to do.
        /// </summary>
        private static string ReadCommonPopupWithFocus(KeyInputCommonPopup popup, bool buttonOnly = false)
        {
            commonPopupOpenReadPending = false;

            string announcement = buttonOnly ? null : ReadCommonPopup(popup.Pointer);
            int index = popup.selectCursor?.Index ?? -1;
            string buttonText = ReadButtonFromCommandList(popup.Pointer, COMMON_CMDLIST_OFFSET, index);
            if (string.IsNullOrWhiteSpace(buttonText) ||
                !AnnouncementDeduplicator.ShouldAnnounce(AnnouncementContexts.COMMON_POPUP_BUTTON, index))
                return announcement;

            buttonText = TextUtils.StripIconMarkup(buttonText);
            return string.IsNullOrEmpty(announcement) ? buttonText : $"{announcement}. {buttonText}";
        }

        private static string ReadButtonFromCommandList(IntPtr popupPtr, int cmdListOffset, int index)
        {
            try
            {
                IntPtr listPtr = Marshal.ReadIntPtr(popupPtr + cmdListOffset);
                if (listPtr == IntPtr.Zero) return null;

                int size = Marshal.ReadInt32(listPtr + 0x18);
                if (index < 0 || index >= size) return null;

                IntPtr itemsPtr = Marshal.ReadIntPtr(listPtr + 0x10);
                if (itemsPtr == IntPtr.Zero) return null;

                IntPtr commandPtr = Marshal.ReadIntPtr(itemsPtr + 0x20 + (index * 8));
                if (commandPtr == IntPtr.Zero) return null;

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

        public static void PopupOpen_Postfix(BasePopup __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                if (IsShopActive())
                {
                    // Shop popups get no message read here, only their focused button (the shop
                    // patches speak the context); moves are read by the SetCommandSelectCursor hook.
                    var shopPopup = __instance.TryCast<KeyInputCommonPopup>();
                    if (shopPopup != null)
                    {
                        AnnouncementDeduplicator.Reset(AnnouncementContexts.COMMON_POPUP_BUTTON);
                        commonPopupOpenReadPending = true;
                        CoroutineManager.StartManaged(DelayedPopupRead(shopPopup.Pointer, "CommonPopup",
                            () => ReadCommonPopupWithFocus(shopPopup, buttonOnly: true)));
                    }
                    return;
                }

                // KeyInput types first
                var commonPopup = __instance.TryCast<KeyInputCommonPopup>();
                if (commonPopup != null)
                {
                    AnnouncementDeduplicator.Reset(AnnouncementContexts.COMMON_POPUP_BUTTON);
                    commonPopupOpenReadPending = true;
                    HandlePopupDetected("CommonPopup", commonPopup.Pointer, COMMON_CMDLIST_OFFSET,
                        () => ReadCommonPopupWithFocus(commonPopup));
                    return;
                }

                var gameOver = __instance.TryCast<KeyInputGameOverSelectPopup>();
                if (gameOver != null)
                {
                    AnnouncementDeduplicator.Reset(DEDUP_GAMEOVER_SELECT);
                    gameOverSelectOpenReadPending = true;
                    HandlePopupDetected("GameOverSelectPopup", gameOver.Pointer, GAMEOVER_CMDLIST_OFFSET,
                        () => ReadGameOverSelectPopupWithFocus(gameOver));
                    return;
                }

                var info = __instance.TryCast<KeyInputInfomationPopup>();
                if (info != null)
                {
                    HandlePopupDetected("InfomationPopup", info.Pointer, -1,
                        () => ReadInfomationPopup(info.Pointer));
                    return;
                }

                // Touch types (fallback)
                var touchCommon = __instance.TryCast<TouchCommonPopup>();
                if (touchCommon != null)
                {
                    HandlePopupDetected("TouchCommonPopup", touchCommon.Pointer, -1,
                        () => ReadTouchCommonPopup(touchCommon.Pointer));
                    return;
                }

                var touchGameOver = __instance.TryCast<TouchGameOverSelectPopup>();
                if (touchGameOver != null)
                {
                    HandlePopupDetected("TouchGameOverSelectPopup", touchGameOver.Pointer, -1,
                        () => T("Game Over"));
                    return;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in Open postfix: {ex.Message}");
            }
        }

        private static void HandlePopupDetected(string typeName, IntPtr ptr, int cmdListOffset, Func<string> readFunc)
        {
            PopupState.SetActive(typeName, ptr, cmdListOffset);
            CoroutineManager.StartManaged(DelayedPopupRead(ptr, typeName, readFunc));
        }

        private static IEnumerator DelayedPopupRead(IntPtr popupPtr, string typeName, Func<string> readFunc)
        {
            yield return null;

            try
            {
                if (popupPtr == IntPtr.Zero) yield break;

                string announcement = readFunc();
                if (!string.IsNullOrEmpty(announcement))
                {
                    FFIV_ScreenReaderMod.SpeakText(announcement, interrupt: false);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in delayed read: {ex.Message}");
            }
        }

        public static void PopupClose_Postfix()
        {
            try
            {
                // The next popup starts fresh
                commonPopupOpenReadPending = false;
                gameOverSelectOpenReadPending = false;
                AnnouncementDeduplicator.Reset(AnnouncementContexts.COMMON_POPUP_BUTTON);
                if (PopupState.IsConfirmationPopupActive)
                {
                    PopupState.Clear();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in Close postfix: {ex.Message}");
            }
        }

        #endregion

        #region GameOver Popup Patches

        // Both game-over popups used to be read from their per-frame UpdateCommand (2026-09-24:
        // replaced). GameOverSelectPopup (Load / Title) moves were ALSO read by the generic cursor
        // handler, so each move was spoken twice.

        /// <summary>
        /// GameOverSelectPopup.SetCommandSelectCursor (private, RVA 0x4D58E0, unique): callers Open,
        /// ResetCursor, the up/down index callback UpdateSelect passes to Cursor.NextIndex/PrevIndex
        /// (&lt;UpdateSelect&gt;b__12_2) and the mouse handler. Not per frame.
        /// </summary>
        private static void TryPatchGameOverSelectPopupFocus(HarmonyLib.Harmony harmony) =>
            PatchHelper.TryPatchPostfix(harmony, typeof(KeyInputGameOverSelectPopup), "SetCommandSelectCursor",
                typeof(PopupPatches), nameof(GameOverSelectPopup_SetCommandSelectCursor_Postfix), "[Popup]");

        /// <summary>
        /// GameOverPopupController.InitSaveLoadPopup announces the Load confirmation. Its Yes/No
        /// moves are read from the generic Cursor.NextIndex/PrevIndex hooks (TryReadGameOverLoadMove):
        /// GameOverLoadPopup.SetCommandSelectCursor can't be used because UpdateSelect re-runs it
        /// every frame for a single-button popup, like SavePopup's.
        /// </summary>
        private static void TryPatchGameOverLoadPopup(HarmonyLib.Harmony harmony) =>
            PatchHelper.TryPatchPostfix(harmony, typeof(KeyInputGameOverPopupController), "InitSaveLoadPopup",
                typeof(PopupPatches), nameof(GameOverPopupController_InitSaveLoadPopup_Postfix), "[Popup]");

        private const string DEDUP_GAMEOVER_SELECT = AnnouncementContexts.GAMEOVER_SELECT;

        // Set while the game-over popup's open read ("Game Over" + focused button) is pending, so the
        // SetCommandSelectCursor call inside its Open doesn't speak the button first.
        private static bool gameOverSelectOpenReadPending;

        /// <summary>Open read: "Game Over. {focused button}", claiming the button guard.</summary>
        private static string ReadGameOverSelectPopupWithFocus(KeyInputGameOverSelectPopup popup)
        {
            gameOverSelectOpenReadPending = false;
            string announcement = ReadGameOverSelectPopup(popup.Pointer);
            int index = ReadCursorIndexAt(popup.Pointer, GAMEOVER_SELECT_CURSOR_OFFSET);
            string buttonText = ReadButtonFromCommandList(popup.Pointer, GAMEOVER_CMDLIST_OFFSET, index);
            if (string.IsNullOrWhiteSpace(buttonText) ||
                !AnnouncementDeduplicator.ShouldAnnounce(DEDUP_GAMEOVER_SELECT, index))
                return announcement;
            return $"{announcement}. {TextUtils.StripIconMarkup(buttonText)}";
        }

        public static void GameOverSelectPopup_SetCommandSelectCursor_Postfix(KeyInputGameOverSelectPopup __instance)
        {
            try
            {
                if (__instance == null || gameOverSelectOpenReadPending) return;

                var go = __instance.gameObject;
                if (go == null || !go.activeInHierarchy) return;

                IntPtr ptr = __instance.Pointer;
                int index = ReadCursorIndexAt(ptr, GAMEOVER_SELECT_CURSOR_OFFSET);
                if (index < 0) return;
                if (!AnnouncementDeduplicator.ShouldAnnounce(DEDUP_GAMEOVER_SELECT, index)) return;

                string buttonText = ReadButtonFromCommandList(ptr, GAMEOVER_CMDLIST_OFFSET, index);
                if (!string.IsNullOrWhiteSpace(buttonText))
                    FFIV_ScreenReaderMod.SpeakText(TextUtils.StripIconMarkup(buttonText), interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in GameOverSelectPopup.SetCommandSelectCursor: {ex.Message}");
            }
        }

        private const string DEDUP_GAMEOVER_LOAD = AnnouncementContexts.GAMEOVER_LOAD;

        // The game-over Load confirmation whose Yes/No moves are read (set by InitSaveLoadPopup).
        // Holding the wrapper keeps the object alive while its cursor pointer is compared.
        private static KeyInputGameOverLoadPopup activeGameOverLoadPopup;
        private static bool gameOverLoadOpenReadPending;

        public static void GameOverPopupController_InitSaveLoadPopup_Postfix(KeyInputGameOverPopupController __instance)
        {
            try
            {
                if (__instance == null) return;

                // Navigate: controller->view(0x30)->loadPopup(0x18)
                IntPtr viewPtr = Marshal.ReadIntPtr(__instance.Pointer + GAMEOVERPOPUPCTRL_VIEW_OFFSET);
                IntPtr loadPopupPtr = viewPtr == IntPtr.Zero ? IntPtr.Zero
                    : Marshal.ReadIntPtr(viewPtr + GAMEOVERPOPUPVIEW_LOADPOPUP_OFFSET);
                if (loadPopupPtr == IntPtr.Zero) return;

                // Reset button tracking for fresh state
                AnnouncementDeduplicator.Reset(DEDUP_GAMEOVER_LOAD);
                activeGameOverLoadPopup = new KeyInputGameOverLoadPopup(loadPopupPtr);
                gameOverLoadOpenReadPending = true;

                // Use coroutine to delay reading until UI has populated
                CoroutineManager.StartManaged(DelayedGameOverLoadPopupRead(loadPopupPtr));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in GameOverPopupController.InitSaveLoadPopup: {ex.Message}");
            }
        }

        private static IEnumerator DelayedGameOverLoadPopupRead(IntPtr loadPopupPtr)
        {
            yield return null; // Wait one frame

            gameOverLoadOpenReadPending = false;
            try
            {
                IntPtr messageTextPtr = Marshal.ReadIntPtr(loadPopupPtr + GAMEOVERLOAD_MESSAGE_OFFSET);
                string message = ReadTextFromPointer(messageTextPtr);
                message = string.IsNullOrWhiteSpace(message) ? null : TextUtils.StripIconMarkup(message.Trim());

                // Focused button, claiming the guard so the first move away is the next thing read.
                int index = FocusedSaveStyleIndex(loadPopupPtr, GAMEOVERLOAD_SELECT_CURSOR_OFFSET, GAMEOVERLOAD_CMDLIST_OFFSET);
                string buttonText = ReadButtonFromCommandList(loadPopupPtr, GAMEOVERLOAD_CMDLIST_OFFSET, index);
                if (!string.IsNullOrWhiteSpace(buttonText) && AnnouncementDeduplicator.ShouldAnnounce(DEDUP_GAMEOVER_LOAD, index))
                {
                    buttonText = TextUtils.StripIconMarkup(buttonText);
                    message = message == null ? buttonText : $"{message}. {buttonText}";
                }

                if (!string.IsNullOrEmpty(message))
                    FFIV_ScreenReaderMod.SpeakText(message, interrupt: false);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in DelayedGameOverLoadPopupRead: {ex.Message}");
            }
        }

        /// <summary>
        /// Called from the Cursor.NextIndex/PrevIndex postfixes: when the moved cursor is the
        /// game-over Load confirmation's (selectCursor @0x58), reads the newly focused button.
        /// Cursor.NextIndex/PrevIndex update the index before invoking the move callback, so it
        /// is current here. Returns true when the cursor was that popup's.
        /// </summary>
        public static bool TryReadGameOverLoadMove(GameCursor cursor)
        {
            try
            {
                var popup = activeGameOverLoadPopup;
                if (popup == null || cursor == null) return false;
                IntPtr popupPtr = popup.Pointer;
                if (popupPtr == IntPtr.Zero || Marshal.ReadIntPtr(popupPtr + GAMEOVERLOAD_SELECT_CURSOR_OFFSET) != cursor.Pointer)
                    return false;

                if (gameOverLoadOpenReadPending) return true;
                int index = cursor.Index;
                if (!AnnouncementDeduplicator.ShouldAnnounce(DEDUP_GAMEOVER_LOAD, index)) return true;

                string buttonText = ReadButtonFromCommandList(popupPtr, GAMEOVERLOAD_CMDLIST_OFFSET, index);
                if (!string.IsNullOrWhiteSpace(buttonText))
                    FFIV_ScreenReaderMod.SpeakText(TextUtils.StripIconMarkup(buttonText), interrupt: true);
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error reading game-over load popup move: {ex.Message}");
                return false;
            }
        }

        /// <summary>Drops the game-over popup registration (scene change).</summary>
        public static void ResetSceneState()
        {
            activeGameOverLoadPopup = null;
            gameOverLoadOpenReadPending = false;
        }

        private static int ReadCursorIndexAt(IntPtr popupPtr, int cursorOffset)
        {
            if (popupPtr == IntPtr.Zero) return -1;
            IntPtr cursorPtr = Marshal.ReadIntPtr(popupPtr + cursorOffset);
            return cursorPtr == IntPtr.Zero ? -1 : new GameCursor(cursorPtr).Index;
        }

        /// <summary>
        /// The focused index of a SavePopup-style popup (GameOverLoadPopup, SavePopup). When its
        /// first command is hidden (a single-button popup), the game's UpdateSelect forces the
        /// cursor to index 1 every frame, so that is the focused button whatever the index says yet.
        /// </summary>
        internal static int FocusedSaveStyleIndex(IntPtr popupPtr, int cursorOffset, int cmdListOffset)
        {
            int index = ReadCursorIndexAt(popupPtr, cursorOffset);
            try
            {
                IntPtr listPtr = Marshal.ReadIntPtr(popupPtr + cmdListOffset);
                if (listPtr == IntPtr.Zero || Marshal.ReadInt32(listPtr + 0x18) < 2) return index;
                IntPtr itemsPtr = Marshal.ReadIntPtr(listPtr + 0x10);
                IntPtr firstPtr = itemsPtr == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(itemsPtr + 0x20);
                if (firstPtr == IntPtr.Zero) return index;
                var first = new UnityEngine.Component(firstPtr).gameObject;
                if (first != null && !first.activeSelf) return 1;
            }
            catch { }
            return index;
        }

        #endregion

        #region Title Screen

        private static string pendingTitleText = null;
        private static bool isTitleScreenTextPending = false;

        public static void SplashController_InitializeTitle_Postfix(SplashController __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                string pressText = null;

                try
                {
                    var uiMsgType = Type.GetType("Il2CppUiMessageConstants, Assembly-CSharp")
                                 ?? Type.GetType("UiMessageConstants, Assembly-CSharp");

                    if (uiMsgType != null)
                    {
                        var field = uiMsgType.GetField("MENU_TITLE_PRESS_TEXT", BindingFlags.Public | BindingFlags.Static);
                        if (field != null)
                        {
                            pressText = field.GetValue(null) as string;
                        }
                    }
                }
                catch (Exception) { }

                pendingTitleText = !string.IsNullOrWhiteSpace(pressText)
                    ? TextUtils.StripIconMarkup(pressText.Trim())
                    : T("Press any button");
                isTitleScreenTextPending = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in InitializeTitle postfix: {ex.Message}");
                pendingTitleText = T("Press any button");
                isTitleScreenTextPending = true;
            }
        }

        public static void SystemIndicator_Hide_Postfix()
        {
            try
            {
                if (isTitleScreenTextPending && !string.IsNullOrWhiteSpace(pendingTitleText))
                {
                    FFIV_ScreenReaderMod.SpeakText(pendingTitleText, interrupt: false);
                    pendingTitleText = null;
                    isTitleScreenTextPending = false;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in SystemIndicator.Hide postfix: {ex.Message}");
            }
        }

        #endregion
    }
}
