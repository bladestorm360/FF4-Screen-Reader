using System;
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

namespace FFIV_ScreenReader.Patches
{
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
        private static string lastAnnouncement = "";

        [HarmonyPostfix]
        public static void Postfix(AbilityCommandController __instance, int index)
        {
            try
            {
                if (__instance == null)
                {
                    return;
                }

                // Get the content list
                var contentList = __instance.contentList;
                if (contentList == null || contentList.Count == 0)
                {
                    return;
                }

                // Validate index
                if (index < 0 || index >= contentList.Count)
                {
                    return;
                }

                // Get the content view at the cursor position
                var contentView = contentList[index];
                if (contentView == null || contentView.text == null)
                {
                    return;
                }

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
                if (commandName == lastAnnouncement)
                {
                    return;
                }
                lastAnnouncement = commandName;

                MelonLogger.Msg($"[Ability Command] {commandName}");
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
        private static string lastAnnouncement = "";

        [HarmonyPostfix]
        public static void Postfix(AbilityContentListController __instance, Cursor targetCursor)
        {
            try
            {
                if (__instance == null || targetCursor == null)
                {
                    return;
                }

                // Get the content list
                var contentList = __instance.contentList;
                if (contentList == null || contentList.Count == 0)
                {
                    return;
                }

                int index = targetCursor.Index;
                if (index < 0 || index >= contentList.Count)
                {
                    return;
                }

                // Get the selected content controller
                var selectedContent = contentList[index];
                if (selectedContent == null)
                {
                    return;
                }

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
                if (announcement == lastAnnouncement)
                {
                    return;
                }
                lastAnnouncement = announcement;

                MelonLogger.Msg($"[Ability List] {announcement}");
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
        private static string lastAnnouncement = "";

        [HarmonyPostfix]
        public static void Postfix(AbilityUseContentListController __instance, Il2CppSystem.Collections.Generic.IEnumerable<ItemTargetSelectContentController> targetContents, Il2CppLast.UI.Cursor targetCursor)
        {
            try
            {
                if (__instance == null || targetCursor == null)
                {
                    return;
                }

                // Get the content list from the controller
                var contentList = __instance.contentList;
                if (contentList == null || contentList.Count == 0)
                {
                    return;
                }

                int index = targetCursor.Index;
                if (index < 0 || index >= contentList.Count)
                {
                    return;
                }

                var selectedController = contentList[index];
                if (selectedController == null || selectedController.CurrentData == null)
                {
                    return;
                }

                var data = selectedController.CurrentData;
                string characterName = data.Name;
                if (string.IsNullOrEmpty(characterName))
                {
                    return;
                }

                // Build announcement with HP and MP information
                string announcement = characterName;

                try
                {
                    // Get the character's parameter data
                    var parameter = data.parameter;
                    if (parameter != null)
                    {
                        int currentHP = parameter.CurrentHP;
                        int maxHP = parameter.ConfirmedMaxHp();
                        int currentMP = parameter.CurrentMP;
                        int maxMP = parameter.ConfirmedMaxMp();

                        announcement += $", HP {currentHP}/{maxHP}, MP {currentMP}/{maxMP}";

                        // Get status conditions
                        var conditionList = parameter.ConfirmedConditionList();
                        if (conditionList != null && conditionList.Count > 0)
                        {
                            var messageManager = MessageManager.Instance;
                            if (messageManager != null)
                            {
                                var statusNames = new System.Collections.Generic.List<string>();

                                foreach (var condition in conditionList)
                                {
                                    if (condition != null)
                                    {
                                        string conditionMesId = condition.MesIdName;

                                        // Skip conditions with no message ID (internal/hidden statuses)
                                        if (!string.IsNullOrEmpty(conditionMesId) && conditionMesId != "None")
                                        {
                                            string localizedConditionName = messageManager.GetMessage(conditionMesId);
                                            if (!string.IsNullOrEmpty(localizedConditionName))
                                            {
                                                statusNames.Add(localizedConditionName);
                                            }
                                        }
                                    }
                                }

                                if (statusNames.Count > 0)
                                {
                                    announcement += $", {string.Join(", ", statusNames)}";
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"Error reading HP/MP/Status for {characterName}: {ex.Message}");
                    // Continue with just the name if stats can't be read
                }

                // Skip duplicates
                if (announcement == lastAnnouncement)
                {
                    return;
                }
                lastAnnouncement = announcement;

                MelonLogger.Msg($"[Ability Target] {announcement}");
                FFIV_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in AbilityUseContentListController.SelectContent patch: {ex.Message}");
            }
        }
    }
}
