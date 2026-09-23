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
using static FFIV_ScreenReader.Utils.ModTextTranslator;

namespace FFIV_ScreenReader.Patches
{
    /// <summary>
    /// Controller-based patches for battle menus (commands, abilities, items).
    /// Uses direct controller access instead of hierarchy walking.
    /// </summary>

    /// <summary>
    /// Description of the item or spell focused in a battle list, read by the details (I) key.
    /// Null while focus is on the command menu (no list entry focused).
    /// </summary>
    internal static class BattleListDetails
    {
        internal static string LastDescription;

        internal static void Clear() => LastDescription = null;
    }

    /// <summary>
    /// Patch for battle command selection (Attack, Magic, Item, Defend, etc.)
    /// Announces command names when cursor moves through the menu.
    /// </summary>
    [HarmonyPatch(typeof(BattleCommandSelectController), nameof(BattleCommandSelectController.SetCursor))]
    public static class BattleCommandSelectController_SetCursor_Patch
    {
        private const string DEDUP_CONTEXT = AnnouncementContexts.BATTLE_COMMAND_SELECT;

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

                // SAFETY: Skip if flee is in progress to prevent command menu announcements
                // from interrupting the flee sequence
                if (GlobalBattleMessageTracker.IsFleeInProgress)
                {
                    return;
                }

                // Focus is on the command menu: no list entry is focused for the details key
                BattleListDetails.Clear();

                // The first command after the per-turn reset queues behind "X's turn";
                // cursor movement after that interrupts as usual.
                bool firstOfTurn = AnnouncementDeduplicator.GetLastIndex(DEDUP_CONTEXT) < 0;

                // Skip duplicate announcements
                if (!AnnouncementDeduplicator.ShouldAnnounce(DEDUP_CONTEXT, index))
                    return;

                var contentController = SelectContentHelper.TryGetItem(__instance.contentList, index);
                if (contentController == null || contentController.TargetCommand == null)
                    return;

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

                commandName = FFIV_ScreenReader.Utils.MenuPosition.Format(commandName, index, __instance.contentList.Count);
                FFIV_ScreenReaderMod.SpeakText(commandName, interrupt: !firstOfTurn);
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
        private const string DEDUP_CONTEXT = AnnouncementContexts.BATTLE_ITEM_SELECT;

        [HarmonyPostfix]
        public static void Postfix(BattleItemInfomationController __instance, Cursor targetCursor)
        {
            try
            {
                int index = SelectContentHelper.GetCursorIndex(__instance, targetCursor);
                if (index < 0)
                    return;

                var selectedContent = SelectContentHelper.TryGetItem(__instance.contentList, index);
                if (selectedContent == null)
                    return;

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

                // Add quantity
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
                }

                // Description for the details (I) key; spoken on focus only with Auto Detail
                string description = StripIconMarkup(contentData?.Description);
                BattleListDetails.LastDescription = description;
                if (PreferencesManager.AutoDetailEnabled && !string.IsNullOrWhiteSpace(description))
                {
                    announcement += $", {description}";
                }

                // Skip duplicate announcements
                if (!AnnouncementDeduplicator.ShouldAnnounce(DEDUP_CONTEXT, announcement))
                {
                    return;
                }

                announcement = FFIV_ScreenReader.Utils.MenuPosition.Format(announcement, index, __instance.contentList.Count);
                FFIV_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in BattleItemInfomationController.SelectContent patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Clears the battle item guard each time the item list opens, so re-entering the list
    /// re-announces the focused item instead of treating it as a duplicate.
    /// </summary>
    [HarmonyPatch(typeof(BattleItemInfomationController), nameof(BattleItemInfomationController.ShowUseSelect))]
    public static class BattleItemInfomationController_ShowUseSelect_Patch
    {
        [HarmonyPrefix]
        public static void Prefix() => AnnouncementDeduplicator.Reset(AnnouncementContexts.BATTLE_ITEM_SELECT);
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
        private const string DEDUP_CONTEXT = AnnouncementContexts.BATTLE_ABILITY_SELECT;

        [HarmonyPostfix]
        public static void Postfix(BattleQuantityAbilityInfomationController __instance, Cursor targetCursor)
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

                // Remove icon markup from name
                abilityName = StripIconMarkup(abilityName);

                if (string.IsNullOrWhiteSpace(abilityName))
                {
                    return;
                }

                // Build announcement
                string announcement = abilityName;

                // MP cost from master data
                int mpCost = abilityData.Ability?.UseValue ?? 0;
                if (mpCost > 0)
                {
                    announcement += $", {T("MP")} {mpCost}";
                }

                // Description for the details (I) key; spoken on focus only with Auto Detail
                string description = string.IsNullOrWhiteSpace(mesIdDescription)
                    ? null
                    : StripIconMarkup(messageManager.GetMessage(mesIdDescription));
                BattleListDetails.LastDescription = description;
                if (PreferencesManager.AutoDetailEnabled && !string.IsNullOrWhiteSpace(description))
                {
                    announcement += $", {description}";
                }

                // Skip duplicate announcements
                if (!AnnouncementDeduplicator.ShouldAnnounce(DEDUP_CONTEXT, announcement))
                {
                    return;
                }

                announcement = FFIV_ScreenReader.Utils.MenuPosition.Format(announcement, index, __instance.contentList.Count);
                FFIV_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in BattleQuantityAbilityInfomationController.SelectContent patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Clears the battle ability guard each time a magic/ability list opens, so re-entering
    /// the list re-announces the focused spell instead of treating it as a duplicate.
    /// </summary>
    [HarmonyPatch(typeof(BattleQuantityAbilityInfomationController), nameof(BattleQuantityAbilityInfomationController.ShowUseSelect))]
    public static class BattleQuantityAbilityInfomationController_ShowUseSelect_Patch
    {
        [HarmonyPrefix]
        public static void Prefix() => AnnouncementDeduplicator.Reset(AnnouncementContexts.BATTLE_ABILITY_SELECT);
    }
}
