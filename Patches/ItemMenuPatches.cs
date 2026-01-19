using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using Il2CppLast.UI.KeyInput;
using Il2CppLast.UI;
using Il2CppLast.Defaine;
using Il2CppLast.Management;
using FFIV_ScreenReader.Core;
using FFIV_ScreenReader.Utils;
using static FFIV_ScreenReader.Utils.TextUtils;

// Import MenuState classes
using ItemMenuState = FFIV_ScreenReader.Core.ItemMenuState;
using EquipmentMenuState = FFIV_ScreenReader.Core.EquipmentMenuState;

// Type aliases for window controllers (in base namespace)
using ItemWindowController = Il2CppLast.UI.KeyInput.ItemWindowController;
using EquipmentWindowController = Il2CppLast.UI.KeyInput.EquipmentWindowController;

namespace FFIV_ScreenReader.Patches
{
    /// <summary>
    /// Manual patches for item and equipment menu state transitions.
    /// Hooks window controller SetActive to clear state when menus close entirely.
    /// Also hooks command controller SetFocus for finer-grained state transitions.
    /// </summary>
    public static class ItemMenuStatePatches
    {
        private static bool isPatched = false;

        /// <summary>
        /// Apply manual Harmony patches for state transitions.
        /// Uses SetActive(false) on window controllers to clear state when menus close.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched)
                return;

            try
            {
                // Patch ItemWindowController.SetActive - clears state when item menu closes
                PatchItemWindowController(harmony);

                // Patch EquipmentWindowController.SetActive - clears state when equipment menu closes
                PatchEquipmentWindowController(harmony);

                // Patch command controllers for state clearing when returning to command bar
                PatchItemCommandController(harmony);
                PatchEquipmentCommandController(harmony);

                // Patch ItemListController.ResetController - clears state when leaving item list
                // This covers Use, Key Items, and Sort menus returning to command bar
                PatchItemListController(harmony);

                // Patch ItemWindowController.CommandSelectInit - clears state when returning to command bar
                // This is the definitive hook for when the item menu transitions to command bar state
                PatchItemWindowControllerCommandSelectInit(harmony);

                isPatched = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[ItemMenu] Error applying state patches: {ex.Message}");
            }
        }

        private static void PatchItemWindowController(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(ItemWindowController);
                var method = controllerType.GetMethod("SetActive", BindingFlags.Instance | BindingFlags.Public);
                if (method != null)
                {
                    var postfix = typeof(ItemMenuStatePatches).GetMethod(nameof(ItemWindowController_SetActive_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[ItemMenu] Failed to patch ItemWindowController.SetActive: {ex.Message}");
            }
        }

        private static void PatchEquipmentWindowController(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(EquipmentWindowController);
                var method = controllerType.GetMethod("SetActive", BindingFlags.Instance | BindingFlags.Public);
                if (method != null)
                {
                    var postfix = typeof(ItemMenuStatePatches).GetMethod(nameof(EquipmentWindowController_SetActive_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[ItemMenu] Failed to patch EquipmentWindowController.SetActive: {ex.Message}");
            }
        }

        private static void PatchItemCommandController(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(Il2CppLast.UI.KeyInput.ItemCommandController);
                var method = controllerType.GetMethod("SetFocus", BindingFlags.Instance | BindingFlags.Public);
                if (method != null)
                {
                    var postfix = typeof(ItemMenuStatePatches).GetMethod(nameof(ItemCommandController_SetFocus_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[ItemMenu] Failed to patch ItemCommandController.SetFocus: {ex.Message}");
            }
        }

        private static void PatchEquipmentCommandController(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(Il2CppLast.UI.KeyInput.EquipmentCommandController);
                var method = controllerType.GetMethod("SetFocus", BindingFlags.Instance | BindingFlags.Public);
                if (method != null)
                {
                    var postfix = typeof(ItemMenuStatePatches).GetMethod(nameof(EquipmentCommandController_SetFocus_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[ItemMenu] Failed to patch EquipmentCommandController.SetFocus: {ex.Message}");
            }
        }

        private static void PatchItemListController(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(Il2CppLast.UI.KeyInput.ItemListController);
                var method = controllerType.GetMethod("ResetController", BindingFlags.Instance | BindingFlags.Public);
                if (method != null)
                {
                    var postfix = typeof(ItemMenuStatePatches).GetMethod(nameof(ItemListController_ResetController_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[ItemMenu] Failed to patch ItemListController.ResetController: {ex.Message}");
            }
        }

        private static void PatchItemWindowControllerCommandSelectInit(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Use AccessTools.Method which handles Il2Cpp private methods better than GetMethod
                var method = AccessTools.Method(typeof(ItemWindowController), "CommandSelectInit");
                if (method != null)
                {
                    var postfix = typeof(ItemMenuStatePatches).GetMethod(nameof(ItemWindowController_CommandSelectInit_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[ItemMenu] Failed to patch ItemWindowController.CommandSelectInit: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for ItemWindowController.SetActive - clears item menu state when menu closes.
        /// This is the definitive hook for when the item menu is fully closed.
        /// </summary>
        public static void ItemWindowController_SetActive_Postfix(ItemWindowController __instance, bool isActive)
        {
            if (!isActive && ItemMenuState.IsActive)
            {
                ItemMenuState.Reset();
            }
        }

        /// <summary>
        /// Postfix for EquipmentWindowController.SetActive - clears equipment menu state when menu closes.
        /// This is the definitive hook for when the equipment menu is fully closed.
        /// </summary>
        public static void EquipmentWindowController_SetActive_Postfix(EquipmentWindowController __instance, bool isActive)
        {
            if (!isActive && EquipmentMenuState.IsActive)
            {
                EquipmentMenuState.Reset();
            }
        }

        /// <summary>
        /// Postfix for ItemCommandController.SetFocus - placeholder for potential future use.
        /// State clearing is handled by CommandSelectInit.
        /// </summary>
        public static void ItemCommandController_SetFocus_Postfix(Il2CppLast.UI.KeyInput.ItemCommandController __instance, bool isFocus)
        {
            // State clearing handled by CommandSelectInit
        }

        /// <summary>
        /// Postfix for EquipmentCommandController.SetFocus - handles state transitions.
        /// When gaining focus from shop: clears ShopState so generic cursor can read command bar.
        /// When gaining focus from equipment slots: clears EquipmentMenuState.
        /// </summary>
        public static void EquipmentCommandController_SetFocus_Postfix(Il2CppLast.UI.KeyInput.EquipmentCommandController __instance, bool isFocus)
        {
            if (isFocus)
            {
                // When gaining focus from shop, clear ShopState to allow generic cursor to read
                // This enables the equipment command bar (Equip/Optimal/Remove All) to be announced
                if (ShopState.IsActive)
                {
                    ShopState.ClearForEquipmentSubmenu();
                }

                // When returning from equipment slots to command bar, clear EquipmentMenuState
                if (EquipmentMenuState.IsActive)
                {
                    EquipmentMenuState.Reset();
                }
            }
        }

        /// <summary>
        /// Postfix for ItemListController.ResetController - clears state when leaving item list.
        /// This fires when returning from Use/Key Items/Sort menus to command bar.
        /// </summary>
        public static void ItemListController_ResetController_Postfix(Il2CppLast.UI.KeyInput.ItemListController __instance, bool isForced)
        {
            if (ItemMenuState.IsActive)
            {
                ItemMenuState.Reset();
            }
        }

        /// <summary>
        /// Postfix for ItemWindowController.CommandSelectInit - clears state when returning to command bar.
        /// This fires exactly when the item menu transitions to the CommandSelect state (Use/Key Items/Sort).
        /// </summary>
        public static void ItemWindowController_CommandSelectInit_Postfix(ItemWindowController __instance)
        {
            if (ItemMenuState.IsActive)
            {
                ItemMenuState.Reset();
            }
        }
    }


    /// <summary>
    /// Deduplicator for equipment menu announcements.
    /// Prevents duplicate announcements when multiple patches fire for the same slot.
    /// Uses centralized AnnouncementDeduplicator.
    /// NOTE: This is NOT a state tracker - menu state is managed by EquipmentMenuState.
    /// </summary>
    public static class EquipmentAnnouncementDeduplicator
    {
        private const string DEDUP_CONTEXT = "EquipmentMenu.Announcement";

        public static bool ShouldAnnounce(string message)
        {
            return AnnouncementDeduplicator.ShouldAnnounce(DEDUP_CONTEXT, message);
        }

        public static void Reset()
        {
            AnnouncementDeduplicator.Reset(DEDUP_CONTEXT);
        }
    }

    /// <summary>
    /// Patches for item and equipment menu navigation.
    /// Announces item/equipment name, quantity, and description when browsing.
    /// </summary>

    // Patch ItemListController.SelectContent to announce items when navigating
    // Note: SelectContent is PRIVATE, so we must use string literal instead of nameof()
    [HarmonyPatch(typeof(Il2CppLast.UI.KeyInput.ItemListController), "SelectContent", new Type[] {
        typeof(Il2CppSystem.Collections.Generic.IEnumerable<ItemListContentData>),
        typeof(int),
        typeof(Il2CppLast.UI.Cursor),
        typeof(Il2CppLast.UI.CustomScrollView.WithinRangeType)
    })]
    public static class ItemListController_SelectContent_Patch
    {
        private const string DEDUP_CONTEXT = "ItemMenu.List";

        [HarmonyPostfix]
        public static void Postfix(
            Il2CppLast.UI.KeyInput.ItemListController __instance,
            Il2CppSystem.Collections.Generic.IEnumerable<ItemListContentData> targets,
            int index,
            Il2CppLast.UI.Cursor targetCursor)
        {
            try
            {
                if (targets == null)
                {
                    return;
                }

                // Convert IEnumerable to List for indexed access
                var targetList = new Il2CppSystem.Collections.Generic.List<ItemListContentData>(targets);
                if (targetList == null || targetList.Count == 0)
                {
                    return;
                }

                if (index < 0 || index >= targetList.Count)
                {
                    return;
                }

                var itemData = targetList[index];
                if (itemData == null)
                {
                    return;
                }

                // Store for I key equipment compatibility lookup
                ItemMenuState.LastSelectedItem = itemData;

                string itemName = itemData.Name;
                if (string.IsNullOrEmpty(itemName))
                {
                    return;
                }

                // Remove icon markup from name (e.g., <ic_Drag>, <IC_DRAG>)
                itemName = StripIconMarkup(itemName);

                if (string.IsNullOrEmpty(itemName))
                {
                    return;
                }

                // Build announcement with item details
                string announcement = itemName;

                // Add quantity if available
                int count = itemData.Count;
                if (count > 0)
                {
                    announcement += $", {count}";
                }

                // Add description if available
                string description = itemData.Description;
                if (!string.IsNullOrEmpty(description))
                {
                    // Remove icon markup
                    description = StripIconMarkup(description);

                    if (!string.IsNullOrEmpty(description))
                    {
                        announcement += $", {description}";
                    }
                }

                // Skip duplicates
                if (!AnnouncementDeduplicator.ShouldAnnounce(DEDUP_CONTEXT, announcement))
                {
                    return;
                }

                // Set item menu state active
                ItemMenuState.SetActive();

                FFIV_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ItemListController.SelectContent patch: {ex.Message}");
            }
        }
    }

    // Patch EquipmentSelectWindowController.SetCursor to announce equipment when navigating
    // Note: SetCursor is PRIVATE, so we must use string literal instead of nameof()
    [HarmonyPatch(typeof(Il2CppLast.UI.KeyInput.EquipmentSelectWindowController), "SetCursor", new Type[] {
        typeof(Il2CppLast.UI.Cursor),
        typeof(bool),
        typeof(Il2CppLast.UI.CustomScrollView.WithinRangeType)
    })]
    public static class EquipmentSelectWindowController_SetCursor_Patch
    {
        private const string DEDUP_CONTEXT = "EquipmentMenu.Select";

        [HarmonyPostfix]
        public static void Postfix(
            Il2CppLast.UI.KeyInput.EquipmentSelectWindowController __instance,
            Il2CppLast.UI.Cursor targetCursor)
        {
            try
            {
                int index = SelectContentHelper.GetCursorIndex(__instance, targetCursor);
                if (index < 0)
                    return;

                var equipmentData = SelectContentHelper.TryGetItem(__instance.ContentDataList, index);
                if (equipmentData == null)
                    return;

                string itemName = equipmentData.Name;
                if (string.IsNullOrEmpty(itemName))
                {
                    return;
                }

                // Remove icon markup from name (e.g., <ic_Drag>, <IC_DRAG>)
                itemName = StripIconMarkup(itemName);

                if (string.IsNullOrEmpty(itemName))
                {
                    return;
                }

                // Build announcement with equipment details
                string announcement = itemName;

                // Add mechanical info (ATK +15, DEF +8, etc.)
                string paramMessage = equipmentData.ParameterMessage;
                if (!string.IsNullOrEmpty(paramMessage))
                {
                    // Remove icon markup
                    paramMessage = StripIconMarkup(paramMessage);

                    if (!string.IsNullOrEmpty(paramMessage))
                    {
                        announcement += $", {paramMessage}";
                    }
                }

                // Add description if available
                string description = equipmentData.Description;
                if (!string.IsNullOrEmpty(description))
                {
                    // Remove icon markup
                    description = StripIconMarkup(description);

                    if (!string.IsNullOrEmpty(description))
                    {
                        announcement += $", {description}";
                    }
                }

                // Skip duplicates
                if (!AnnouncementDeduplicator.ShouldAnnounce(DEDUP_CONTEXT, announcement))
                {
                    return;
                }

                // Set equipment menu state active
                EquipmentMenuState.SetActive();

                FFIV_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in EquipmentSelectWindowController.SetCursor patch: {ex.Message}");
            }
        }
    }

    // Patch EquipmentInfoWindowController.SelectContent to announce equipment slots when navigating
    // This is the screen where you see R. Hand, L. Hand, Head, Body, etc. after selecting a character
    // Note: SelectContent is PRIVATE, so we must use string literal instead of nameof()
    [HarmonyPatch(typeof(Il2CppLast.UI.KeyInput.EquipmentInfoWindowController), "SelectContent", new Type[] {
        typeof(Il2CppLast.UI.Cursor)
    })]
    public static class EquipmentInfoWindowController_SelectContent_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(
            Il2CppLast.UI.KeyInput.EquipmentInfoWindowController __instance,
            Il2CppLast.UI.Cursor targetCursor)
        {
            try
            {
                int index = SelectContentHelper.GetCursorIndex(__instance, targetCursor);
                if (index < 0)
                    return;

                // Get slot name and equipped item from contentList
                string slotName = null;
                string equippedItem = null;
                var contentView = SelectContentHelper.TryGetItem(__instance.contentList, index);
                if (contentView != null)
                {
                    // Get slot name from partText
                    if (contentView.partText != null)
                    {
                        slotName = contentView.partText.text;
                    }

                    // Get item data from Data property
                    var itemData = contentView.Data;
                    if (itemData != null)
                    {
                        equippedItem = itemData.Name;

                        // Get parameter message (ATK +15, DEF +8, etc.)
                        string paramMessage = itemData.ParameterMessage;
                        if (!string.IsNullOrEmpty(paramMessage))
                        {
                            equippedItem += ", " + paramMessage;
                        }
                    }
                }

                // Build announcement
                string announcement = "";
                if (!string.IsNullOrEmpty(slotName))
                {
                    announcement = slotName;
                }

                if (!string.IsNullOrEmpty(equippedItem))
                {
                    if (!string.IsNullOrEmpty(announcement))
                    {
                        announcement += ": " + equippedItem;
                    }
                    else
                    {
                        announcement = equippedItem;
                    }
                }

                if (string.IsNullOrEmpty(announcement))
                {
                    return;
                }

                // Filter icon markup
                announcement = StripIconMarkup(announcement);

                // Use deduplication to prevent interruption by other patches
                if (!EquipmentAnnouncementDeduplicator.ShouldAnnounce(announcement))
                {
                    return;
                }

                // Set equipment menu state active
                EquipmentMenuState.SetActive();

                FFIV_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in EquipmentInfoWindowController.SelectContent patch: {ex.Message}");
            }
        }
    }

    // Patch ItemUseController.SelectContent to announce character stats when selecting item targets
    // Note: SelectContent is PRIVATE, so we must use string literal instead of nameof()
    [HarmonyPatch(typeof(Il2CppLast.UI.KeyInput.ItemUseController), "SelectContent", new Type[] {
        typeof(Il2CppSystem.Collections.Generic.IEnumerable<Il2CppLast.UI.KeyInput.ItemTargetSelectContentController>),
        typeof(Il2CppLast.UI.Cursor)
    })]
    public static class ItemUseController_SelectContent_Patch
    {
        private const string DEDUP_CONTEXT = "ItemMenu.UseTarget";

        [HarmonyPostfix]
        public static void Postfix(Il2CppLast.UI.KeyInput.ItemUseController __instance, Il2CppSystem.Collections.Generic.IEnumerable<Il2CppLast.UI.KeyInput.ItemTargetSelectContentController> targetContents, Il2CppLast.UI.Cursor targetCursor)
        {
            try
            {
                int index = SelectContentHelper.GetCursorIndex(__instance, targetCursor);
                if (index < 0)
                    return;

                // Convert IEnumerable to List for indexed access
                var targetList = new Il2CppSystem.Collections.Generic.List<Il2CppLast.UI.KeyInput.ItemTargetSelectContentController>(targetContents);
                var selectedController = SelectContentHelper.TryGetItem(targetList, index);
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
                announcement += CharacterStatusHelper.GetFullStatus(data.Parameter);

                if (!AnnouncementDeduplicator.ShouldAnnounce(DEDUP_CONTEXT, announcement))
                {
                    return;
                }

                FFIV_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ItemUseController.SelectContent patch: {ex.Message}");
            }
        }
    }
}
