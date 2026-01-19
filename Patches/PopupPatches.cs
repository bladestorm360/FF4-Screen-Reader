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

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched)
                return;

            try
            {
                TryPatchBasePopup(harmony);
                TryPatchTitleScreen(harmony);
                isPatched = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error applying patches: {ex.Message}");
            }
        }

        private static void TryPatchBasePopup(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type popupType = typeof(BasePopup);

                var openMethod = AccessTools.Method(popupType, "Open");
                if (openMethod != null)
                {
                    var openPostfix = typeof(PopupPatches).GetMethod(nameof(PopupOpen_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(openMethod, postfix: new HarmonyMethod(openPostfix));
                }

                var closeMethod = AccessTools.Method(popupType, "Close");
                if (closeMethod != null)
                {
                    var closePostfix = typeof(PopupPatches).GetMethod(nameof(PopupClose_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(closeMethod, postfix: new HarmonyMethod(closePostfix));
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error patching base Popup: {ex.Message}");
            }
        }

        private static void TryPatchTitleScreen(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type splashControllerType = typeof(SplashController);
                var initTitleMethod = AccessTools.Method(splashControllerType, "InitializeTitle");

                if (initTitleMethod != null)
                {
                    var postfix = typeof(PopupPatches).GetMethod(nameof(SplashController_InitializeTitle_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(initTitleMethod, postfix: new HarmonyMethod(postfix));
                }

                Type titleWindowControllerType = typeof(KeyInputTitleWindowController);
                var setEnableStartMethod = AccessTools.Method(titleWindowControllerType, "SetEnableStartObject");

                if (setEnableStartMethod != null)
                {
                    var postfix = typeof(PopupPatches).GetMethod(nameof(TitleWindowController_SetEnableStartObject_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(setEnableStartMethod, postfix: new HarmonyMethod(postfix));
                }
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

        #region Title Screen

        private static string pendingTitleText = null;

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
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in InitializeTitle postfix: {ex.Message}");
                pendingTitleText = "Press any button";
            }
        }

        public static void TitleWindowController_SetEnableStartObject_Postfix(bool isEnable)
        {
            try
            {
                if (!isEnable)
                    return;

                string text = !string.IsNullOrWhiteSpace(pendingTitleText) ? pendingTitleText : "Press any button";
                FFIV_ScreenReaderMod.SpeakText(text, interrupt: false);
                pendingTitleText = null;
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
