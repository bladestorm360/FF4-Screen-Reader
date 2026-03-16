using System;
using System.Collections;
using MelonLoader;
using UnityEngine;
using FFIV_ScreenReader.Utils;
using static FFIV_ScreenReader.Utils.ModTextTranslator;

namespace FFIV_ScreenReader.Core
{
    /// <summary>
    /// Simple Yes/No confirmation dialog using Windows API focus stealing.
    /// Used for waypoint deletion confirmations.
    /// </summary>
    public static class ConfirmationDialog
    {
        /// <summary>
        /// Whether the confirmation dialog is currently open.
        /// </summary>
        public static bool IsOpen { get; private set; }

        private static string prompt = "";
        private static Action onYesCallback;
        private static Action onNoCallback;
        private static bool selectedYes = true; // Default selection is Yes

        /// <summary>
        /// Opens the confirmation dialog.
        /// </summary>
        /// <param name="promptText">Prompt to display to user (spoken via TTS)</param>
        /// <param name="onYes">Callback when user confirms Yes</param>
        /// <param name="onNo">Callback when user confirms No</param>
        public static void Open(string promptText, Action onYes, Action onNo = null)
        {
            bool wasAlreadyOpen = IsOpen;

            IsOpen = true;
            prompt = promptText ?? "";
            onYesCallback = onYes;
            onNoCallback = onNo;
            selectedYes = true; // Default to Yes

            WindowsFocusHelper.InitializeKeyStates(new int[] { WindowsFocusHelper.VK_RETURN, WindowsFocusHelper.VK_ESCAPE, WindowsFocusHelper.VK_LEFT, WindowsFocusHelper.VK_RIGHT, WindowsFocusHelper.VK_Y, WindowsFocusHelper.VK_N });

            if (!wasAlreadyOpen)
            {
                // First open - steal focus from game
                WindowsFocusHelper.StealFocus("FFIV_ConfirmDialog");

                // Announce prompt with delay to avoid NVDA window title interruption
                CoroutineManager.StartManaged(DelayedPromptAnnouncement(string.Format(T("{0} Yes or No"), prompt)));
            }
            else
            {
                // Continuation - dialog already open, just announce new prompt immediately
                FFIV_ScreenReaderMod.SpeakText(string.Format(T("{0} Yes or No"), prompt), interrupt: true);
            }
        }

        /// <summary>
        /// Announces the prompt after a short delay to avoid NVDA announcing the window title first.
        /// </summary>
        private static IEnumerator DelayedPromptAnnouncement(string text)
        {
            yield return new WaitForSeconds(0.1f);
            FFIV_ScreenReaderMod.SpeakText(text, interrupt: true);
        }

        /// <summary>
        /// Closes the confirmation dialog and restores focus to game.
        /// </summary>
        public static void Close()
        {
            if (!IsOpen) return;

            IsOpen = false;
            WindowsFocusHelper.RestoreFocus();

            // Clear callbacks
            onYesCallback = null;
            onNoCallback = null;
        }

        /// <summary>
        /// Closes the dialog and announces text after focus is restored.
        /// Uses a coroutine delay to prevent NVDA window title from interrupting.
        /// </summary>
        public static void CloseWithAnnouncement(string text)
        {
            if (!IsOpen) return;

            IsOpen = false;
            WindowsFocusHelper.RestoreFocus();

            // Clear callbacks
            onYesCallback = null;
            onNoCallback = null;

            // Announce after focus restoration settles
            if (!string.IsNullOrEmpty(text))
            {
                CoroutineManager.StartManaged(DelayedPromptAnnouncement(text));
            }
        }

        /// <summary>
        /// Clears callbacks, invokes the given callback, and closes the dialog
        /// unless the callback opened a new dialog (continuation).
        /// </summary>
        private static bool InvokeAndClose(Action callback)
        {
            var cb = callback;
            onYesCallback = null;
            onNoCallback = null;
            cb?.Invoke();
            if (onYesCallback == null) Close();
            return true;
        }

        /// <summary>
        /// Handles keyboard input for the confirmation dialog.
        /// Should be called from InputManager.Update() before any other input handling.
        /// Returns true if input was consumed (dialog is open).
        /// </summary>
        public static bool HandleInput()
        {
            if (!IsOpen) return false;

            // Y key - confirm Yes immediately
            if (WindowsFocusHelper.IsKeyDown(WindowsFocusHelper.VK_Y))
                return InvokeAndClose(onYesCallback);

            // N key - confirm No immediately
            if (WindowsFocusHelper.IsKeyDown(WindowsFocusHelper.VK_N))
                return InvokeAndClose(onNoCallback);

            // Escape - same as No
            if (WindowsFocusHelper.IsKeyDown(WindowsFocusHelper.VK_ESCAPE))
                return InvokeAndClose(onNoCallback);

            // Enter - confirm current selection
            if (WindowsFocusHelper.IsKeyDown(WindowsFocusHelper.VK_RETURN))
                return InvokeAndClose(selectedYes ? onYesCallback : onNoCallback);

            // Left/Right arrows - toggle selection
            if (WindowsFocusHelper.IsKeyDown(WindowsFocusHelper.VK_LEFT) || WindowsFocusHelper.IsKeyDown(WindowsFocusHelper.VK_RIGHT))
            {
                selectedYes = !selectedYes;
                string selection = selectedYes ? T("Yes") : T("No");
                FFIV_ScreenReaderMod.SpeakText(selection, interrupt: true);
                return true;
            }

            return true; // Consume all input while dialog is open
        }

    }
}
