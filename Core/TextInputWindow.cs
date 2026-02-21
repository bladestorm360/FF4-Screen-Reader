using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using MelonLoader;
using UnityEngine;
using FFIV_ScreenReader.Utils;

namespace FFIV_ScreenReader.Core
{
    /// <summary>
    /// Modal text input dialog using Windows API focus stealing.
    /// Creates an invisible window to capture keyboard input, preventing keys from reaching the game.
    /// Used for waypoint naming and other text input scenarios.
    /// </summary>
    public static class TextInputWindow
    {
        /// <summary>
        /// Whether the text input window is currently open.
        /// </summary>
        public static bool IsOpen { get; private set; }

        private static StringBuilder inputBuffer = new StringBuilder();
        private static string prompt = "";
        private static Action<string> onConfirmCallback;
        private static Action onCancelCallback;

        // Cursor position for navigation
        private static int cursorPosition = 0;

        /// <summary>
        /// Opens the text input dialog.
        /// </summary>
        /// <param name="promptText">Prompt to display to user (spoken via TTS)</param>
        /// <param name="initialText">Initial text in the input field</param>
        /// <param name="onConfirm">Callback when user presses Enter (receives final text)</param>
        /// <param name="onCancel">Callback when user presses Escape</param>
        public static void Open(string promptText, string initialText, Action<string> onConfirm, Action onCancel = null)
        {
            if (IsOpen) return;

            IsOpen = true;
            prompt = promptText ?? "";
            inputBuffer.Clear();
            if (!string.IsNullOrEmpty(initialText))
                inputBuffer.Append(initialText);

            onConfirmCallback = onConfirm;
            onCancelCallback = onCancel;

            // Initialize cursor position to end of text
            cursorPosition = inputBuffer.Length;

            // Initialize key states to prevent keys from triggering immediately
            var trackedKeys = new List<int> {
                WindowsFocusHelper.VK_BACK, WindowsFocusHelper.VK_RETURN, WindowsFocusHelper.VK_SHIFT, WindowsFocusHelper.VK_ESCAPE, WindowsFocusHelper.VK_SPACE,
                WindowsFocusHelper.VK_LEFT, WindowsFocusHelper.VK_UP, WindowsFocusHelper.VK_RIGHT, WindowsFocusHelper.VK_DOWN, WindowsFocusHelper.VK_HOME, WindowsFocusHelper.VK_END,
                WindowsFocusHelper.VK_OEM_MINUS, WindowsFocusHelper.VK_OEM_PERIOD, WindowsFocusHelper.VK_OEM_COMMA, WindowsFocusHelper.VK_OEM_7,
                WindowsFocusHelper.VK_OEM_1, WindowsFocusHelper.VK_OEM_2, WindowsFocusHelper.VK_OEM_3, WindowsFocusHelper.VK_OEM_4, WindowsFocusHelper.VK_OEM_5, WindowsFocusHelper.VK_OEM_6, WindowsFocusHelper.VK_OEM_PLUS
            };
            for (int vk = WindowsFocusHelper.VK_A; vk <= WindowsFocusHelper.VK_Z; vk++) trackedKeys.Add(vk);
            for (int vk = WindowsFocusHelper.VK_0; vk <= WindowsFocusHelper.VK_9; vk++) trackedKeys.Add(vk);
            WindowsFocusHelper.InitializeKeyStates(trackedKeys.ToArray());

            // Steal focus from game
            WindowsFocusHelper.StealFocus("FFIV_TextInput");

            // Delay prompt announcement to let NVDA finish announcing window title
            CoroutineManager.StartManaged(DelayedPromptAnnouncement(prompt, inputBuffer.ToString()));
        }

        /// <summary>
        /// Delays the prompt announcement to avoid being interrupted by NVDA's window title announcement.
        /// </summary>
        private static IEnumerator DelayedPromptAnnouncement(string promptText, string initialText)
        {
            // Wait for NVDA focus announcement to complete
            yield return new WaitForSeconds(0.1f);

            string announcement = promptText;
            if (!string.IsNullOrEmpty(initialText))
            {
                announcement += $": {initialText}";
            }
            FFIV_ScreenReaderMod.SpeakText(announcement, interrupt: true);
        }

        /// <summary>
        /// Closes the dialog and announces the result after a delay to avoid NVDA window title interruption.
        /// </summary>
        /// <param name="closeMessage">Message to speak after dialog closes</param>
        /// <param name="confirmCallback">Optional confirm callback to invoke</param>
        /// <param name="confirmArg">Argument to pass to confirm callback</param>
        /// <param name="cancelCallback">Optional cancel callback to invoke</param>
        private static void CloseWithDelayedAnnouncement(string closeMessage,
            Action<string> confirmCallback = null, string confirmArg = null,
            Action cancelCallback = null)
        {
            Close();
            CoroutineManager.StartManaged(DelayedCloseAnnouncement(
                closeMessage, confirmCallback, confirmArg, cancelCallback));
        }

        /// <summary>
        /// Coroutine that waits for NVDA to finish window title announcement,
        /// then speaks the close message and invokes the callback.
        /// </summary>
        private static IEnumerator DelayedCloseAnnouncement(string closeMessage,
            Action<string> confirmCallback, string confirmArg, Action cancelCallback)
        {
            // Wait for NVDA focus announcement to complete
            yield return new WaitForSeconds(0.1f);
            FFIV_ScreenReaderMod.SpeakText(closeMessage, interrupt: true);

            // Brief pause before callback (which may speak its own message)
            yield return new WaitForSeconds(0.15f);
            confirmCallback?.Invoke(confirmArg);
            cancelCallback?.Invoke();
        }

        private static readonly Dictionary<char, string> CharacterNames = new Dictionary<char, string>
        {
            [' '] = "space", ['.'] = "period", [','] = "comma",
            ['\''] = "apostrophe", ['"'] = "quote", ['-'] = "dash",
            ['_'] = "underscore", [';'] = "semicolon", [':'] = "colon",
            ['!'] = "exclamation", ['?'] = "question", ['/'] = "slash",
            ['\\'] = "backslash", ['('] = "open paren", [')'] = "close paren",
            ['['] = "open bracket", [']'] = "close bracket", ['{'] = "open brace",
            ['}'] = "close brace", ['`'] = "backtick", ['~'] = "tilde",
            ['='] = "equals", ['+'] = "plus", ['|'] = "pipe",
            ['<'] = "less than", ['>'] = "greater than",
        };

        /// <summary>
        /// Converts a character to a speakable name for screen readers.
        /// Letters and numbers are returned as-is; punctuation gets descriptive names.
        /// </summary>
        private static string GetCharacterName(char c)
            => CharacterNames.TryGetValue(c, out var name) ? name : c.ToString();

        /// <summary>
        /// Inserts a character at the current cursor position and advances the cursor.
        /// </summary>
        private static void InsertChar(char c)
        {
            inputBuffer.Insert(cursorPosition, c);
            cursorPosition++;
        }

        /// <summary>
        /// Mapping of virtual key codes to their normal and shifted punctuation characters.
        /// </summary>
        private static readonly (int vk, char normal, char shifted)[] PunctuationKeys = new[]
        {
            (WindowsFocusHelper.VK_OEM_MINUS,  '-', '_'),
            (WindowsFocusHelper.VK_OEM_PERIOD, '.', '>'),
            (WindowsFocusHelper.VK_OEM_COMMA,  ',', '<'),
            (WindowsFocusHelper.VK_OEM_7,      '\'', '"'),
            (WindowsFocusHelper.VK_OEM_1,      ';', ':'),
            (WindowsFocusHelper.VK_OEM_2,      '/', '?'),
            (WindowsFocusHelper.VK_OEM_3,      '`', '~'),
            (WindowsFocusHelper.VK_OEM_4,      '[', '{'),
            (WindowsFocusHelper.VK_OEM_5,      '\\', '|'),
            (WindowsFocusHelper.VK_OEM_6,      ']', '}'),
            (WindowsFocusHelper.VK_OEM_PLUS,   '=', '+'),
        };

        /// <summary>
        /// Closes the text input dialog and restores focus to game.
        /// </summary>
        public static void Close()
        {
            if (!IsOpen) return;

            IsOpen = false;
            WindowsFocusHelper.RestoreFocus();

            // Clear callbacks
            onConfirmCallback = null;
            onCancelCallback = null;
        }

        /// <summary>
        /// Handles keyboard input for the text input dialog.
        /// Should be called from InputManager.Update() before any other input handling.
        /// Returns true if input was consumed (dialog is open).
        /// </summary>
        public static bool HandleInput()
        {
            if (!IsOpen) return false;

            // Enter - confirm
            if (WindowsFocusHelper.IsKeyDown(WindowsFocusHelper.VK_RETURN))
            {
                string finalText = inputBuffer.ToString().Trim();
                if (string.IsNullOrEmpty(finalText))
                {
                    FFIV_ScreenReaderMod.SpeakText("Name cannot be empty", interrupt: true);
                    return true;
                }

                CloseWithDelayedAnnouncement($"Confirmed: {finalText}", onConfirmCallback, finalText);
                return true;
            }

            // Escape - cancel
            if (WindowsFocusHelper.IsKeyDown(WindowsFocusHelper.VK_ESCAPE))
            {
                CloseWithDelayedAnnouncement("Cancelled", cancelCallback: onCancelCallback);
                return true;
            }

            // Backspace - delete character before cursor
            if (WindowsFocusHelper.IsKeyDown(WindowsFocusHelper.VK_BACK))
            {
                if (cursorPosition > 0)
                {
                    char deletedChar = inputBuffer[cursorPosition - 1];
                    inputBuffer.Remove(cursorPosition - 1, 1);
                    cursorPosition--;
                    FFIV_ScreenReaderMod.SpeakText(GetCharacterName(deletedChar), interrupt: true);
                }
                return true;
            }

            // Left Arrow - move cursor left
            if (WindowsFocusHelper.IsKeyDown(WindowsFocusHelper.VK_LEFT))
            {
                if (cursorPosition > 0)
                {
                    cursorPosition--;
                    FFIV_ScreenReaderMod.SpeakText(GetCharacterName(inputBuffer[cursorPosition]), interrupt: true);
                }
                return true;
            }

            // Right Arrow - move cursor right
            if (WindowsFocusHelper.IsKeyDown(WindowsFocusHelper.VK_RIGHT))
            {
                if (cursorPosition < inputBuffer.Length)
                {
                    FFIV_ScreenReaderMod.SpeakText(GetCharacterName(inputBuffer[cursorPosition]), interrupt: true);
                    cursorPosition++;
                }
                return true;
            }

            // Up Arrow - read full text
            if (WindowsFocusHelper.IsKeyDown(WindowsFocusHelper.VK_UP))
            {
                string text = inputBuffer.Length > 0 ? inputBuffer.ToString() : "empty";
                FFIV_ScreenReaderMod.SpeakText(text, interrupt: true);
                return true;
            }

            // Down Arrow - read full text
            if (WindowsFocusHelper.IsKeyDown(WindowsFocusHelper.VK_DOWN))
            {
                string text = inputBuffer.Length > 0 ? inputBuffer.ToString() : "empty";
                FFIV_ScreenReaderMod.SpeakText(text, interrupt: true);
                return true;
            }

            // Home - move cursor to start
            if (WindowsFocusHelper.IsKeyDown(WindowsFocusHelper.VK_HOME))
            {
                cursorPosition = 0;
                if (inputBuffer.Length > 0)
                {
                    FFIV_ScreenReaderMod.SpeakText(GetCharacterName(inputBuffer[0]), interrupt: true);
                }
                return true;
            }

            // End - move cursor to end
            if (WindowsFocusHelper.IsKeyDown(WindowsFocusHelper.VK_END))
            {
                cursorPosition = inputBuffer.Length;
                return true;
            }

            // Space - silent when typing
            if (WindowsFocusHelper.IsKeyDown(WindowsFocusHelper.VK_SPACE))
            {
                InsertChar(' ');
                return true;
            }

            bool shiftHeld = WindowsFocusHelper.IsKeyPressed(WindowsFocusHelper.VK_SHIFT);

            // Letters A-Z - silent when typing
            for (int vk = WindowsFocusHelper.VK_A; vk <= WindowsFocusHelper.VK_Z; vk++)
            {
                if (WindowsFocusHelper.IsKeyDown(vk))
                {
                    char c = (char)('a' + (vk - WindowsFocusHelper.VK_A));
                    InsertChar(shiftHeld ? char.ToUpper(c) : c);
                    return true;
                }
            }

            // Numbers 0-9 - silent when typing
            for (int vk = WindowsFocusHelper.VK_0; vk <= WindowsFocusHelper.VK_9; vk++)
            {
                if (WindowsFocusHelper.IsKeyDown(vk))
                {
                    char c = (char)('0' + (vk - WindowsFocusHelper.VK_0));
                    InsertChar(c);
                    return true;
                }
            }

            // Punctuation - all silent when typing
            foreach (var (vk, normal, shifted) in PunctuationKeys)
            {
                if (WindowsFocusHelper.IsKeyDown(vk))
                {
                    InsertChar(shiftHeld ? shifted : normal);
                    return true;
                }
            }

            return true; // Consume all input while dialog is open
        }

    }
}
