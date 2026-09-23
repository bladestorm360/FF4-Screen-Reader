using System;
using System.Collections;
using HarmonyLib;
using MelonLoader;
using Il2CppLast.UI.KeyInput;
using FFIV_ScreenReader.Core;
using FFIV_ScreenReader.Utils;


namespace FFIV_ScreenReader.Patches
{
    /// <summary>
    /// Controller-based patches for the title menu.
    /// Announces menu items directly from TitleMenuCommandController instead of hierarchy walking.
    /// </summary>

    [HarmonyPatch(typeof(TitleMenuCommandController), nameof(TitleMenuCommandController.SetCursor))]
    public static class TitleMenuCommandController_SetCursor_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(TitleMenuCommandController __instance, int index)
        {
            try
            {
                Announce(__instance, index);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in TitleMenuCommandController.SetCursor patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Announces the title command at <paramref name="index"/>, deduplicated by command id.
        /// Shared by cursor movement and the menu-entry reader.
        /// </summary>
        internal static void Announce(TitleMenuCommandController controller, int index)
        {
            if (controller == null)
                return;

            // Get the active contents list
            var activeContents = controller.activeContents;
            if (activeContents == null || index < 0 || index >= activeContents.Count)
                return;

            // Get the view at the cursor position - no hierarchy walking!
            var contentView = activeContents[index];

            // Get the localized name from the command data
            string commandName = contentView?.Data?.Name;
            if (string.IsNullOrWhiteSpace(commandName))
                return;

            // Skip duplicate announcements (keyed on command id; reset on each title menu entry)
            if (!AnnouncementDeduplicator.ShouldAnnounce(AnnouncementContexts.TITLE_MENU_COMMAND, (int)contentView.CommandId))
                return;

            // Set title menu state active
            MenuStates.Title.SetActive();

            commandName = MenuPosition.Format(commandName, index, activeContents.Count);
            FFIV_ScreenReaderMod.SpeakText(commandName);
        }
    }

    /// <summary>
    /// Announces the focused title command when a title menu (main menu, Options, Extras) is
    /// entered — on first entry and on back-out. The prefix clears the command guard so a
    /// SetCursor fired inside the Init body announces; the postfix reads the focus one frame
    /// later for the case where the cursor is placed without SetCursor (deduplicated, so the
    /// two paths never double up).
    /// </summary>
    [HarmonyPatch]
    public static class TitleWindowController_MenuInit_Patch
    {
        static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (var name in new[] { "InitSelect", "InitializeOption", "InitializeExtra" })
            {
                var method = AccessTools.Method(typeof(TitleWindowController), name);
                if (method != null)
                    yield return method;
                else
                    MelonLogger.Warning($"[Title] TitleWindowController.{name} not found");
            }
        }

        [HarmonyPrefix]
        public static void Prefix()
        {
            AnnouncementDeduplicator.Reset(AnnouncementContexts.TITLE_MENU_COMMAND);
        }

        [HarmonyPostfix]
        public static void Postfix(TitleWindowController __instance)
        {
            CoroutineManager.StartManaged(AnnounceFocusAfterFrame(__instance));
        }

        private static IEnumerator AnnounceFocusAfterFrame(TitleWindowController window)
        {
            yield return null; // let the cursor settle

            try
            {
                if (window == null || window.gameObject == null || !window.gameObject.activeInHierarchy)
                    yield break;

                var controller = window.commandController;
                var cursor = controller?.selectCursor;
                if (cursor != null)
                    TitleMenuCommandController_SetCursor_Patch.Announce(controller, cursor.Index);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Title] Error reading title menu focus: {ex.Message}");
            }
        }
    }
}
