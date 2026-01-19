using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using Il2CppLast.UI.KeyInput;
using Il2CppSerial.FF4.UI.KeyInput;
using Il2CppLast.UI;
using Il2CppLast.Data.Master;
using Il2CppLast.Data.User;
using Il2CppLast.Management;
using FFIV_ScreenReader.Core;
using FFIV_ScreenReader.Utils;
using static FFIV_ScreenReader.Utils.TextUtils;

// Import MenuState classes
using AbilityMenuState = FFIV_ScreenReader.Core.AbilityMenuState;

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
            if (!isActive && AbilityMenuState.IsActive)
            {
                AbilityMenuState.Reset();
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
        private const string DEDUP_CONTEXT = "AbilityMenu.Command";

        [HarmonyPostfix]
        public static void Postfix(AbilityCommandController __instance, int index)
        {
            try
            {
                if (__instance == null)
                    return;

                var contentView = SelectContentHelper.TryGetItem(__instance.contentList, index);
                if (contentView == null || contentView.text == null)
                    return;

                // Get the command name from the text component
                string commandName = contentView.text.text;
                if (string.IsNullOrWhiteSpace(commandName))
                {
                    return;
                }

                // Remove icon markup
                commandName = StripIconMarkup(commandName);

                if (string.IsNullOrWhiteSpace(commandName))
                {
                    return;
                }

                // Skip duplicate announcements
                if (!AnnouncementDeduplicator.ShouldAnnounce(DEDUP_CONTEXT, commandName))
                {
                    return;
                }

                // Set ability menu state active
                AbilityMenuState.SetActive();

                FFIV_ScreenReaderMod.SpeakText(commandName);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in AbilityCommandController.SelectContent patch: {ex.Message}");
            }
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
        private const string DEDUP_CONTEXT = "AbilityMenu.Content";

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

                // Get message IDs
                string mesIdName = abilityData.MesIdName;
                string mesIdDescription = abilityData.MesIdDescription;

                if (string.IsNullOrWhiteSpace(mesIdName))
                {
                    return;
                }

                var messageManager = MessageManager.Instance;
                if (messageManager == null)
                {
                    return;
                }

                // Get localized name
                string abilityName = messageManager.GetMessage(mesIdName);
                if (string.IsNullOrWhiteSpace(abilityName))
                {
                    return;
                }

                // Remove icon markup
                abilityName = StripIconMarkup(abilityName);

                if (string.IsNullOrWhiteSpace(abilityName))
                {
                    return;
                }

                // Build announcement
                string announcement = abilityName;

                // Try to get MP cost if available from the controller's view
                try
                {
                    var controllerView = __instance.view;
                    if (controllerView != null && controllerView.mpValueText != null)
                    {
                        string mpText = controllerView.mpValueText.text;
                        if (!string.IsNullOrWhiteSpace(mpText))
                        {
                            mpText = mpText.Trim();
                            if (mpText != "0" && mpText != "-")
                            {
                                announcement += $", MP {mpText}";
                            }
                        }
                    }
                }
                catch
                {
                    // MP cost not available, continue without it
                }

                // Add description if available
                if (!string.IsNullOrWhiteSpace(mesIdDescription))
                {
                    string description = StripIconMarkup(messageManager.GetMessage(mesIdDescription));

                    if (!string.IsNullOrWhiteSpace(description))
                    {
                        announcement += $". {description}";
                    }
                }

                // Skip duplicate announcements
                if (!AnnouncementDeduplicator.ShouldAnnounce(DEDUP_CONTEXT, announcement))
                {
                    return;
                }

                // Set ability menu state active
                AbilityMenuState.SetActive();

                FFIV_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in AbilityContentListController.SelectContent patch: {ex.Message}");
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
        private const string DEDUP_CONTEXT = "AbilityMenu.UseTarget";

        [HarmonyPostfix]
        public static void Postfix(AbilityUseContentListController __instance, Il2CppSystem.Collections.Generic.IEnumerable<ItemTargetSelectContentController> targetContents, Il2CppLast.UI.Cursor targetCursor)
        {
            try
            {
                int index = SelectContentHelper.GetCursorIndex(__instance, targetCursor);
                if (index < 0)
                    return;

                var selectedController = SelectContentHelper.TryGetItem(__instance.contentList, index);
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
                AbilityMenuState.SetActive();

                FFIV_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in AbilityUseContentListController.SelectContent patch: {ex.Message}");
            }
        }
    }
}
