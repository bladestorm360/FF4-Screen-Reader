using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using Il2CppLast.UI.KeyInput;
using Il2CppSerial.FF4.UI.KeyInput;
using Il2CppLast.UI;
using Il2CppLast.Data.Master;
using Il2CppLast.Data.User;
using FFIV_ScreenReader.Core;
using FFIV_ScreenReader.Utils;
using static FFIV_ScreenReader.Utils.TextUtils;
using static FFIV_ScreenReader.Utils.ModTextTranslator;


// Type alias for window controller (FF4-specific namespace)
using AbilityWindowController = Il2CppSerial.FF4.UI.KeyInput.AbilityWindowController;

namespace FFIV_ScreenReader.Patches
{
    /// <summary>
    /// Manual patches for ability menu state transitions.
    /// For Ability menu, both the command level (magic type selection) and ability list are handled by SelectContent patches.
    /// State is cleared when the AbilityWindowController is deactivated (menu closes).
    /// </summary>
    public static class AbilityMenuStatePatches
    {
        private static bool isPatched = false;

        /// <summary>
        /// Apply manual Harmony patches for ability menu state management.
        /// Unlike Items/Equipment, Ability menu's command level is also handled by patches.
        /// We only need to clear state when the menu closes entirely.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched)
                return;

            try
            {
                // Patch SetActive(false) to clear state when ability menu closes
                Type controllerType = typeof(AbilityWindowController);
                var setActiveMethod = controllerType.GetMethod("SetActive", BindingFlags.Instance | BindingFlags.Public);
                if (setActiveMethod != null)
                {
                    var postfix = typeof(AbilityMenuStatePatches).GetMethod(nameof(AbilityWindow_SetActive_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(setActiveMethod, postfix: new HarmonyMethod(postfix));
                }

                isPatched = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[AbilityMenu] Error applying state patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for AbilityWindowController.SetActive - clears state when menu closes.
        /// </summary>
        public static void AbilityWindow_SetActive_Postfix(AbilityWindowController __instance, bool isActive)
        {
            if (!isActive && MenuStates.Ability.IsActive)
            {
                MenuStates.Ability.Reset();
            }
        }
    }

    /// <summary>
    /// Controller-based patches for the Ability Menu accessed from the main menu.
    /// Provides screen reader accessibility for:
    /// - Command selection (Magic, Item, etc.)
    /// - Ability/Magic browsing
    /// - Ability equipping
    ///
    /// NOTE: This is separate from BattleCommandPatches.cs which handles in-battle menus.
    /// NOTE: Esper/Magic Stone patches removed - FF6-specific feature.
    /// </summary>

    /// <summary>
    /// Patch for ability command selection in the main ability menu.
    /// Announces command names (Attack, Magic, Item, etc.) when cursor moves.
    /// </summary>
    [HarmonyPatch(typeof(AbilityCommandController), nameof(AbilityCommandController.SelectContent))]
    public static class AbilityCommandController_SelectContent_Patch
    {
        private const string DEDUP_CONTEXT = AnnouncementContexts.ABILITY_COMMAND;

        [HarmonyPostfix]
        public static void Postfix(AbilityCommandController __instance, int index)
        {
            try
            {
                Announce(__instance, index);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in AbilityCommandController.SelectContent patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Announces the ability command at <paramref name="index"/>. Shared by cursor movement and
        /// the command-bar entry reader; deduplicated on the command name.
        /// </summary>
        internal static void Announce(AbilityCommandController controller, int index)
        {
            var contentView = SelectContentHelper.TryGetItem(controller?.contentList, index);
            if (contentView == null || contentView.text == null)
                return;

            // Get the command name from the text component, without icon markup
            string commandName = StripIconMarkup(contentView.text.text);
            if (string.IsNullOrWhiteSpace(commandName))
            {
                return;
            }

            // Skip duplicate announcements
            if (!AnnouncementDeduplicator.ShouldAnnounce(DEDUP_CONTEXT, commandName))
            {
                return;
            }

            // Set ability menu state active; the command bar has no spell focused for the details key
            MenuStates.Ability.SetActive();
            AbilityContentListController_SelectContent_Patch.LastDescription = null;

            commandName = FFIV_ScreenReader.Utils.MenuPosition.Format(commandName, index, controller.contentList.Count);
            FFIV_ScreenReaderMod.SpeakText(commandName);
        }
    }

    /// <summary>
    /// Patch for ability/magic list browsing in the ability menu.
    /// Announces spell/ability names, descriptions, and MP costs.
    /// </summary>
    [HarmonyPatch(typeof(AbilityContentListController), nameof(AbilityContentListController.SelectContent),
        new Type[] { typeof(Cursor), typeof(CustomScrollView.WithinRangeType), typeof(bool) })]
    public static class AbilityContentListController_SelectContent_Patch
    {
        private const string DEDUP_CONTEXT = AnnouncementContexts.ABILITY_CONTENT;

        /// <summary>
        /// Description of the focused spell, read by the details (I) key.
        /// Null while no spell is focused (command bar).
        /// </summary>
        internal static string LastDescription;

        [HarmonyPostfix]
        public static void Postfix(AbilityContentListController __instance, Cursor targetCursor)
        {
            try
            {
                int index = SelectContentHelper.GetCursorIndex(__instance, targetCursor);
                if (index < 0)
                    return;

                var selectedContent = SelectContentHelper.TryGetItem(__instance.contentList, index);
                if (selectedContent == null)
                    return;

                // Get the ability data
                var abilityData = selectedContent.Data;
                if (abilityData == null)
                {
                    return;
                }

                // Get localized name, without icon markup
                string abilityName = StripIconMarkup(MessageHelper.GetLocalizedMessage(abilityData.MesIdName));
                if (string.IsNullOrWhiteSpace(abilityName))
                {
                    return;
                }

                // Build announcement
                string announcement = abilityName;

                // MP cost from master data (the controller-level MP text can lag one entry behind)
                int mpCost = abilityData.Ability?.UseValue ?? 0;
                if (mpCost > 0)
                {
                    announcement += $", {T("MP")} {mpCost}";
                }

                string description = StripIconMarkup(MessageHelper.GetLocalizedMessage(abilityData.MesIdDescription));
                LastDescription = description;

                // Auto Detail: include the description the details key would read
                if (PreferencesManager.AutoDetailEnabled && !string.IsNullOrWhiteSpace(description))
                {
                    announcement += $". {description}";
                }

                // Skip duplicate announcements
                if (!AnnouncementDeduplicator.ShouldAnnounce(DEDUP_CONTEXT, announcement))
                {
                    return;
                }

                // Set ability menu state active
                MenuStates.Ability.SetActive();

                announcement = FFIV_ScreenReader.Utils.MenuPosition.Format(announcement, index, __instance.contentList.Count);
                FFIV_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in AbilityContentListController.SelectContent patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Clears the ability-menu guards as each ability screen (re)gains focus, so its focused entry
    /// re-announces on entry and on back-out instead of being swallowed as a duplicate. On command
    /// (re)entry the postfix also reads the focused command one frame later (placing the cursor
    /// doesn't fire SelectContent); that read is deduplicated, so a SelectContent inside the Init
    /// body and the deferred read never double up.
    /// </summary>
    [HarmonyPatch]
    public static class AbilityWindowController_StateInit_Patch
    {
        static System.Collections.Generic.IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var name in new[] { "CommandInit", "UseListInit", "UseTargetInit" })
            {
                var method = AccessTools.Method(typeof(AbilityWindowController), name);
                if (method != null)
                    yield return method;
                else
                    MelonLogger.Warning($"[AbilityMenu] AbilityWindowController.{name} not found");
            }
        }

        [HarmonyPrefix]
        public static void Prefix(MethodBase __originalMethod)
        {
            switch (__originalMethod.Name)
            {
                case "CommandInit": AnnouncementDeduplicator.Reset(AnnouncementContexts.ABILITY_COMMAND); break;
                case "UseListInit": AnnouncementDeduplicator.Reset(AnnouncementContexts.ABILITY_CONTENT); break;
                case "UseTargetInit": AnnouncementDeduplicator.Reset(AnnouncementContexts.ABILITY_USE_TARGET); break;
            }
        }

        [HarmonyPostfix]
        public static void Postfix(AbilityWindowController __instance, MethodBase __originalMethod)
        {
            if (__originalMethod.Name == "CommandInit")
                CoroutineManager.StartManaged(AnnounceCommandFocusAfterFrame(__instance));
        }

        private static IEnumerator AnnounceCommandFocusAfterFrame(AbilityWindowController window)
        {
            yield return null; // let the command bar cursor settle

            try
            {
                if (window == null || window.gameObject == null || !window.gameObject.activeInHierarchy)
                    yield break;

                var controller = window.commandController;
                var cursor = controller?.selectCursor;
                if (cursor != null)
                    AbilityCommandController_SelectContent_Patch.Announce(controller, cursor.Index);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[AbilityMenu] Error reading ability command focus: {ex.Message}");
            }
        }
    }

    // NOTE: AbilityChangeController does not exist in FF4 - patches removed

    /// <summary>
    /// Patch for target selection when using abilities from the ability menu.
    /// Announces character names when selecting a target for abilities like Cure, Raise, etc.
    /// Note: SelectContent is PRIVATE, so we must use string literal instead of nameof()
    /// </summary>
    [HarmonyPatch(typeof(AbilityUseContentListController), "SelectContent", new Type[] { typeof(Il2CppSystem.Collections.Generic.IEnumerable<ItemTargetSelectContentController>), typeof(Il2CppLast.UI.Cursor) })]
    public static class AbilityUseContentListController_SelectContent_Patch
    {
        private const string DEDUP_CONTEXT = AnnouncementContexts.ABILITY_USE_TARGET;

        [HarmonyPostfix]
        public static void Postfix(AbilityUseContentListController __instance, Il2CppSystem.Collections.Generic.IEnumerable<ItemTargetSelectContentController> targetContents, Il2CppLast.UI.Cursor targetCursor)
        {
            try
            {
                int index = SelectContentHelper.GetCursorIndex(__instance, targetCursor);
                if (index < 0)
                    return;

                // Use targetContents (display/layout order) instead of contentList (data order)
                // to get the correct character matching the cursor position
                ItemTargetSelectContentController selectedController = null;
                var targetList = targetContents.TryCast<Il2CppSystem.Collections.Generic.List<ItemTargetSelectContentController>>();
                if (targetList != null && index >= 0 && index < targetList.Count)
                {
                    selectedController = targetList[index];
                }
                else
                {
                    // Fallback to contentList if targetContents is not a List
                    selectedController = SelectContentHelper.TryGetItem(__instance.contentList, index);
                }

                if (selectedController == null || selectedController.CurrentData == null)
                    return;

                var data = selectedController.CurrentData;
                string characterName = data.Name;
                if (string.IsNullOrEmpty(characterName))
                {
                    return;
                }

                // Build announcement with HP, MP, and status conditions using helper
                string announcement = characterName;
                announcement += CharacterStatusHelper.GetFullStatus(data.parameter);

                // Skip duplicates
                if (!AnnouncementDeduplicator.ShouldAnnounce(DEDUP_CONTEXT, announcement))
                {
                    return;
                }

                // Set ability menu state active
                MenuStates.Ability.SetActive();

                announcement = FFIV_ScreenReader.Utils.MenuPosition.Format(announcement, index, targetList != null ? targetList.Count : __instance.contentList.Count);
                FFIV_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in AbilityUseContentListController.SelectContent patch: {ex.Message}");
            }
        }
    }
}
