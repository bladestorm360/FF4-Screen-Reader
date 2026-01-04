using System;
using Il2CppSerial.FF0.UI.KeyInput;
using HarmonyLib;
using MelonLoader;
using Il2CppLast.UI.KeyInput;
using Il2CppLast.Battle;
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
    /// Controller-based patches for battle menus (commands, abilities, items).
    /// Uses direct controller access instead of hierarchy walking.
    /// </summary>

    /// <summary>
    /// Patch for battle command selection (Attack, Magic, Item, Defend, etc.)
    /// Announces command names when cursor moves through the menu.
    /// </summary>
    [HarmonyPatch(typeof(BattleCommandSelectController), nameof(BattleCommandSelectController.SetCursor))]
    public static class BattleCommandSelectController_SetCursor_Patch
    {
        private static int lastAnnouncedIndex = -1;

        [HarmonyPostfix]
        public static void Postfix(BattleCommandSelectController __instance, int index)
        {
            try
            {
                if (__instance == null)
                {
                    return;
                }

                // SAFETY: Skip if target selection is active to prevent "Attack" from
                // interrupting target announcements after selecting a command
                if (BattleTargetPatches.IsTargetSelectionActive)
                {
                    return;
                }

                // Skip duplicate announcements
                if (index == lastAnnouncedIndex)
                {
                    return;
                }
                lastAnnouncedIndex = index;

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

                // Get the command content at the cursor position
                var contentController = contentList[index];
                if (contentController == null || contentController.TargetCommand == null)
                {
                    return;
                }

                // Get the localized command name using MessageManager
                string mesIdName = contentController.TargetCommand.MesIdName;
                if (string.IsNullOrWhiteSpace(mesIdName))
                {
                    return;
                }

                var messageManager = MessageManager.Instance;
                if (messageManager == null)
                {
                    return;
                }

                string commandName = messageManager.GetMessage(mesIdName);
                if (string.IsNullOrWhiteSpace(commandName))
                {
                    return;
                }

                MelonLogger.Msg($"[Battle Command Menu] {commandName}");
                FFIV_ScreenReaderMod.SpeakText(commandName);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in BattleCommandSelectController.SetCursor patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch for item selection in battle.
    /// Announces item names and details when cursor moves.
    /// </summary>
    [HarmonyPatch(typeof(BattleItemInfomationController), nameof(BattleItemInfomationController.SelectContent),
        new Type[] { typeof(Cursor), typeof(CustomScrollView.WithinRangeType) })]
    public static class BattleItemInfomationController_SelectContent_Patch
    {
        private static string lastAnnouncement = "";

        [HarmonyPostfix]
        public static void Postfix(BattleItemInfomationController __instance, Cursor targetCursor)
        {
            try
            {
                if (__instance == null || targetCursor == null)
                {
                    return;
                }

                int index = targetCursor.Index;

                var contentList = __instance.contentList;
                if (contentList == null || contentList.Count == 0)
                {
                    return;
                }

                if (index < 0 || index >= contentList.Count)
                {
                    return;
                }

                // Get the selected content controller from the content list
                var selectedContent = contentList[index];
                if (selectedContent == null)
                {
                    return;
                }

                // Get the item name from Data
                string itemName = null;

                var contentData = selectedContent.Data;
                if (contentData != null)
                {
                    itemName = contentData.Name;
                }
                else
                {
                    // Try to read from view's IconTextView as fallback
                    var view = selectedContent.view;
                    if (view != null)
                    {
                        var iconTextView = view.IconTextView;
                        if (iconTextView != null && iconTextView.nameText != null)
                        {
                            itemName = iconTextView.nameText.text;
                        }
                        else
                        {
                            // Fall back to NonItemTextView
                            var nonItemTextView = view.NonItemTextView;
                            if (nonItemTextView != null && nonItemTextView.nameText != null)
                            {
                                itemName = nonItemTextView.nameText.text;
                            }
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(itemName))
                {
                    return;
                }

                // Remove icon markup from name (e.g., <ic_Drag>, <IC_DRAG>)
                itemName = StripIconMarkup(itemName);

                if (string.IsNullOrWhiteSpace(itemName))
                {
                    return;
                }

                // Build announcement
                string announcement = itemName;

                // Add quantity and description
                if (contentData != null)
                {
                    // Add quantity if available (for items)
                    try
                    {
                        int count = contentData.Count;
                        if (count > 0)
                        {
                            announcement += $", {count}";
                        }
                    }
                    catch
                    {
                        // Not an item with count, continue
                    }

                    // Add description if available
                    try
                    {
                        string description = contentData.Description;
                        if (!string.IsNullOrWhiteSpace(description))
                        {
                            // Remove icon markup
                            description = StripIconMarkup(description);

                            if (!string.IsNullOrWhiteSpace(description))
                            {
                                announcement += $", {description}";
                            }
                        }
                    }
                    catch
                    {
                        // No description available
                    }
                }

                // Skip duplicate announcements
                if (announcement == lastAnnouncement)
                {
                    return;
                }
                lastAnnouncement = announcement;

                MelonLogger.Msg($"[Battle Item] {announcement}");
                FFIV_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in BattleItemInfomationController.SelectContent patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch for ability/magic selection in battle.
    /// Announces spell/ability names and descriptions when cursor moves.
    /// This controller handles abilities/magic using OwnedAbility data.
    /// </summary>
    [HarmonyPatch(typeof(BattleQuantityAbilityInfomationController), nameof(BattleQuantityAbilityInfomationController.SelectContent),
        new Type[] { typeof(Cursor), typeof(CustomScrollView.WithinRangeType) })]
    public static class BattleQuantityAbilityInfomationController_SelectContent_Patch
    {
        private static string lastAnnouncement = "";

        [HarmonyPostfix]
        public static void Postfix(BattleQuantityAbilityInfomationController __instance, Cursor targetCursor)
        {
            try
            {
                if (__instance == null || targetCursor == null)
                {
                    return;
                }

                // Get the content list (contains BattleAbilityInfomationContentController items)
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

                // Remove icon markup from name
                abilityName = StripIconMarkup(abilityName);

                if (string.IsNullOrWhiteSpace(abilityName))
                {
                    return;
                }

                // Build announcement
                string announcement = abilityName;

                // Add description if available
                if (!string.IsNullOrWhiteSpace(mesIdDescription))
                {
                    string description = StripIconMarkup(messageManager.GetMessage(mesIdDescription));

                    if (!string.IsNullOrWhiteSpace(description))
                    {
                        announcement += $", {description}";
                    }
                }

                // Skip duplicate announcements
                if (announcement == lastAnnouncement)
                {
                    return;
                }
                lastAnnouncement = announcement;

                MelonLogger.Msg($"[Battle Ability] {announcement}");
                FFIV_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in BattleQuantityAbilityInfomationController.SelectContent patch: {ex.Message}");
            }
        }
    }
}
