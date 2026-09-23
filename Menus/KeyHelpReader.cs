using System;
using System.Collections.Generic;
using Il2CppLast.UI.KeyInput;
using UnityEngine;
using UnityEngine.UI;
using FFIV_ScreenReader.Core;
using FFIV_ScreenReader.Utils;
using static FFIV_ScreenReader.Utils.ModTextTranslator;

namespace FFIV_ScreenReader.Menus
{
    /// <summary>
    /// Reads the controls display. Two independent features:
    ///   • Shift+I — reads the visible key help tooltips (button icons + action labels) at once.
    ///     Uses GameObjectCache + transform navigation + GetComponentsInChildren&lt;Text&gt;()
    ///     to avoid IL2CPP Cast constraint errors from array-based access on game-specific types.
    ///   • Arrows/WASD — step through the config "Gamepad/Keyboard Controls" pop-up one entry at a
    ///     time (KeyContext.KeyHelp). ConfigMenuPatches renders the pop-up's entries on open and
    ///     hands them over via <see cref="OpenControlsHelp"/>.
    /// </summary>
    public static class KeyHelpReader
    {
        // KeyHelpController.view (KeyHelpView) — private field, no public accessor
        private const int OFFSET_VIEW = 0x18;

        // Controls pop-up entries (action + binding) and the focused entry.
        private static List<string> helpEntries = null;
        private static int helpIndex = 0;
        // Owner validates the pop-up is still on screen, so a missed close can't leave
        // KeyContext.KeyHelp stuck.
        private static ConfigKeysSettingController helpOwner = null;

        /// <summary>
        /// Called when the Gamepad/Keyboard Controls pop-up opens, with its rendered entries.
        /// Announces the first entry as the initial focus.
        /// </summary>
        public static void OpenControlsHelp(ConfigKeysSettingController owner, List<string> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                CloseControlsHelp();
                return;
            }

            helpOwner = owner;
            helpEntries = entries;
            helpIndex = 0;
            SpeakHelpEntry();
        }

        /// <summary>Called when the pop-up closes / returns to the controls list.</summary>
        public static void CloseControlsHelp()
        {
            helpOwner = null;
            helpEntries = null;
            helpIndex = 0;
        }

        /// <summary>True while the controls pop-up is on screen — drives KeyContext.KeyHelp.</summary>
        public static bool IsScreenActive
        {
            get
            {
                if (helpEntries == null)
                    return false;
                try
                {
                    if (helpOwner != null && helpOwner.gameObject != null && helpOwner.gameObject.activeInHierarchy)
                        return true;
                }
                catch { } // Owner destroyed
                CloseControlsHelp();
                return false;
            }
        }

        public static void NavigateNext()
        {
            if (helpEntries == null) return;
            helpIndex = (helpIndex + 1) % helpEntries.Count;
            SpeakHelpEntry();
        }

        public static void NavigatePrevious()
        {
            if (helpEntries == null) return;
            helpIndex = (helpIndex + helpEntries.Count - 1) % helpEntries.Count;
            SpeakHelpEntry();
        }

        public static void JumpToTop()
        {
            if (helpEntries == null) return;
            helpIndex = 0;
            SpeakHelpEntry();
        }

        public static void JumpToBottom()
        {
            if (helpEntries == null) return;
            helpIndex = helpEntries.Count - 1;
            SpeakHelpEntry();
        }

        private static void SpeakHelpEntry()
        {
            FFIV_ScreenReaderMod.SpeakText(
                MenuPosition.Format(helpEntries[helpIndex], helpIndex, helpEntries.Count), interrupt: true);
        }

        /// <summary>
        /// Public entry point — reads all visible key help controls and speaks them.
        /// </summary>
        public static void AnnounceKeyHelp()
        {
            try
            {
                string result = ReadVisibleKeyHelp();
                FFIV_ScreenReaderMod.SpeakText(result, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Error($"Error in AnnounceKeyHelp: {ex.Message}");
                FFIV_ScreenReaderMod.SpeakText(T("Error reading controls"), interrupt: true);
            }
        }

        /// <summary>
        /// Gets the active KeyHelpController via GameObjectCache (single instance, no Cast-based
        /// array indexer), navigates to its ContentsParent via the private view field, then reads
        /// all visible control entries using GetComponentsInChildren&lt;Text&gt;().
        /// </summary>
        private static unsafe string ReadVisibleKeyHelp()
        {
            // Get KeyHelpController via GameObjectCache (same pattern as AnnounceConfigTooltip)
            var controller = GameObjectCache.Get<KeyHelpController>();
            if (controller == null)
                controller = GameObjectCache.Refresh<KeyHelpController>();

            if (controller == null || controller.gameObject == null || !controller.gameObject.activeInHierarchy)
                return T("No controls displayed");

            // Read private 'view' field (KeyHelpView) at offset 0x18 via unsafe pointer
            IntPtr controllerPtr = controller.Pointer;
            IntPtr viewPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + OFFSET_VIEW);
            if (viewPtr == IntPtr.Zero)
                return T("No controls displayed");

            var view = new KeyHelpView(viewPtr);

            // Get ContentsParent via public property
            var contentsParent = view.ContentsParent;
            if (contentsParent == null)
                return T("No controls displayed");

            var contentsTransform = contentsParent.transform;
            if (contentsTransform == null || contentsTransform.childCount == 0)
                return T("No controls displayed");

            var entries = new List<string>();

            // Iterate children of ContentsParent — each is a control entry (KeyIconController).
            // The game deactivates entries on other pages, so activeInHierarchy filters to
            // the visible page only.
            for (int i = 0; i < contentsTransform.childCount; i++)
            {
                var child = contentsTransform.GetChild(i);
                if (child == null || child.gameObject == null || !child.gameObject.activeInHierarchy)
                    continue;

                // Get all Text components within this entry (same pattern as KeyboardGamepadReader)
                var texts = child.GetComponentsInChildren<Text>(false);
                if (texts == null)
                    continue;

                var parts = new List<string>();
                foreach (var txt in texts)
                {
                    if (txt != null && !string.IsNullOrWhiteSpace(txt.text))
                        parts.Add(txt.text.Trim());
                }

                if (parts.Count > 0)
                    entries.Add(string.Join(": ", parts));
            }

            if (entries.Count == 0)
                return T("No controls displayed");

            return string.Join(", ", entries);
        }
    }
}
