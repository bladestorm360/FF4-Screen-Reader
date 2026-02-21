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
                TryPatchTitleScreen(harmony);
                TryPatchGameOverSelectPopupUpdateCommand(harmony);
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

        public static void ReadCurrentButton(GameCursor cursor)
        {
            try
            {
                if (PopupState.ActivePopupPtr == IntPtr.Zero || PopupState.CommandListOffset < 0)
                    return;

                string buttonText = ReadButtonFromCommandList(
                    PopupState.ActivePopupPtr,
                    PopupState.CommandListOffset,
                    cursor.Index);

                if (!string.IsNullOrWhiteSpace(buttonText))
                {
                    buttonText = TextUtils.StripIconMarkup(buttonText);
                    FFIV_ScreenReaderMod.SpeakText(buttonText, interrupt: true);
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
                if (__instance == null || IsShopActive())
                    return;

                // KeyInput types first
                var commonPopup = __instance.TryCast<KeyInputCommonPopup>();
                if (commonPopup != null)
                {
                    HandlePopupDetected("CommonPopup", commonPopup.Pointer, COMMON_CMDLIST_OFFSET,
                        () => ReadCommonPopup(commonPopup.Pointer));
                    return;
                }

                var gameOver = __instance.TryCast<KeyInputGameOverSelectPopup>();
                if (gameOver != null)
                {
                    HandlePopupDetected("GameOverSelectPopup", gameOver.Pointer, GAMEOVER_CMDLIST_OFFSET,
                        () => ReadGameOverSelectPopup(gameOver.Pointer));
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
                        () => "Game Over");
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

        /// <summary>
        /// Patch GameOverSelectPopup.UpdateCommand to announce button navigation (Load/Title options).
        /// </summary>
        private static void TryPatchGameOverSelectPopupUpdateCommand(HarmonyLib.Harmony harmony) =>
            PatchHelper.TryPatchPostfix(harmony, typeof(KeyInputGameOverSelectPopup), "UpdateCommand",
                typeof(PopupPatches), nameof(GameOverSelectPopup_UpdateCommand_Postfix), "[Popup]");

        /// <summary>
        /// Patch GameOverLoadPopup.UpdateCommand for Yes/No button navigation
        /// and GameOverPopupController.InitSaveLoadPopup to announce the popup message.
        /// </summary>
        private static void TryPatchGameOverLoadPopup(HarmonyLib.Harmony harmony)
        {
            PatchHelper.TryPatchPostfix(harmony, typeof(KeyInputGameOverLoadPopup), "UpdateCommand",
                typeof(PopupPatches), nameof(GameOverLoadPopup_UpdateCommand_Postfix), "[Popup]");

            PatchHelper.TryPatchPostfix(harmony, typeof(KeyInputGameOverPopupController), "InitSaveLoadPopup",
                typeof(PopupPatches), nameof(GameOverPopupController_InitSaveLoadPopup_Postfix), "[Popup]");
        }

        private const string DEDUP_GAMEOVER_SELECT = AnnouncementContexts.GAMEOVER_SELECT;

        public static void GameOverSelectPopup_UpdateCommand_Postfix(KeyInputGameOverSelectPopup __instance)
        {
            try
            {
                if (__instance == null) return;

                IntPtr ptr = __instance.Pointer;
                if (ptr == IntPtr.Zero) return;

                // Read cursor index from offset 0x38
                IntPtr cursorPtr = Marshal.ReadIntPtr(ptr + GAMEOVER_SELECT_CURSOR_OFFSET);
                if (cursorPtr == IntPtr.Zero) return;

                var cursor = new GameCursor(cursorPtr);
                int index = cursor.Index;

                // Deduplicate by index
                if (!AnnouncementDeduplicator.ShouldAnnounce(DEDUP_GAMEOVER_SELECT, index)) return;

                // Read button text from commandList at offset 0x40
                string buttonText = ReadButtonFromCommandList(ptr, GAMEOVER_CMDLIST_OFFSET, index);
                if (!string.IsNullOrWhiteSpace(buttonText))
                {
                    buttonText = TextUtils.StripIconMarkup(buttonText);
                    FFIV_ScreenReaderMod.SpeakText(buttonText, interrupt: true);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in GameOverSelectPopup.UpdateCommand: {ex.Message}");
            }
        }

        private const string DEDUP_GAMEOVER_LOAD = AnnouncementContexts.GAMEOVER_LOAD;

        public static void GameOverLoadPopup_UpdateCommand_Postfix(KeyInputGameOverLoadPopup __instance)
        {
            try
            {
                if (__instance == null) return;

                IntPtr ptr = __instance.Pointer;
                if (ptr == IntPtr.Zero) return;

                // Read cursor index from offset 0x58
                IntPtr cursorPtr = Marshal.ReadIntPtr(ptr + GAMEOVERLOAD_SELECT_CURSOR_OFFSET);
                if (cursorPtr == IntPtr.Zero) return;

                var cursor = new GameCursor(cursorPtr);
                int index = cursor.Index;

                // Deduplicate by index
                if (!AnnouncementDeduplicator.ShouldAnnounce(DEDUP_GAMEOVER_LOAD, index)) return;

                // Read button text from commandList at offset 0x60
                string buttonText = ReadButtonFromCommandList(ptr, GAMEOVERLOAD_CMDLIST_OFFSET, index);
                if (!string.IsNullOrWhiteSpace(buttonText))
                {
                    buttonText = TextUtils.StripIconMarkup(buttonText);
                    FFIV_ScreenReaderMod.SpeakText(buttonText, interrupt: true);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in GameOverLoadPopup.UpdateCommand: {ex.Message}");
            }
        }

        public static void GameOverPopupController_InitSaveLoadPopup_Postfix(KeyInputGameOverPopupController __instance)
        {
            try
            {
                if (__instance == null) return;

                // Reset button tracking for fresh state
                AnnouncementDeduplicator.Reset(DEDUP_GAMEOVER_LOAD);

                // Use coroutine to delay reading until UI has populated
                CoroutineManager.StartManaged(DelayedGameOverLoadPopupRead(__instance.Pointer));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in GameOverPopupController.InitSaveLoadPopup: {ex.Message}");
            }
        }

        private static IEnumerator DelayedGameOverLoadPopupRead(IntPtr controllerPtr)
        {
            yield return null; // Wait one frame

            try
            {
                if (controllerPtr == IntPtr.Zero) yield break;

                // Navigate: controller->view(0x30)->loadPopup(0x18)->messageText(0x40)
                IntPtr viewPtr = Marshal.ReadIntPtr(controllerPtr + GAMEOVERPOPUPCTRL_VIEW_OFFSET);
                if (viewPtr == IntPtr.Zero) yield break;

                IntPtr loadPopupPtr = Marshal.ReadIntPtr(viewPtr + GAMEOVERPOPUPVIEW_LOADPOPUP_OFFSET);
                if (loadPopupPtr == IntPtr.Zero) yield break;

                IntPtr messageTextPtr = Marshal.ReadIntPtr(loadPopupPtr + GAMEOVERLOAD_MESSAGE_OFFSET);
                string message = ReadTextFromPointer(messageTextPtr);

                if (!string.IsNullOrWhiteSpace(message))
                {
                    message = TextUtils.StripIconMarkup(message.Trim());
                    FFIV_ScreenReaderMod.SpeakText(message, interrupt: false);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in DelayedGameOverLoadPopupRead: {ex.Message}");
            }
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
                    : "Press any button";
                isTitleScreenTextPending = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in InitializeTitle postfix: {ex.Message}");
                pendingTitleText = "Press any button";
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
