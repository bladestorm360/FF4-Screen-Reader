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
using static FFIV_ScreenReader.Utils.ModTextTranslator;

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
        private const string DEDUP_CONTEXT = AnnouncementContexts.EQUIPMENT_ANNOUNCEMENT;

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
    /// Description of the equipment focused in the slot or candidate list, read by the
    /// details (I) key. Cleared with EquipmentMenuState.
    /// </summary>
    public static class EquipmentDetails
    {
        public static string LastDescription { get; set; }
    }

    /// <summary>
    /// Clears the slot guard when the slot list (re)gains focus, so the focused slot
    /// re-announces on entry and on back-out from the candidate list.
    /// </summary>
    [HarmonyPatch(typeof(EquipmentWindowController), "InfoInit")]
    public static class EquipmentWindowController_InfoInit_Patch
    {
        [HarmonyPrefix]
        public static void Prefix() => EquipmentAnnouncementDeduplicator.Reset();
    }

    /// <summary>
    /// Clears the candidate guard when the equipment candidate list opens, so its focused
    /// item announces even if it matches the last candidate spoken.
    /// </summary>
    [HarmonyPatch(typeof(EquipmentWindowController), "SelectInit")]
    public static class EquipmentWindowController_SelectInit_Patch
    {
        [HarmonyPrefix]
        public static void Prefix() => AnnouncementDeduplicator.Reset(AnnouncementContexts.EQUIPMENT_SELECT);
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
        private const string DEDUP_CONTEXT = AnnouncementContexts.ITEM_LIST;

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

                Announce(itemData, index, targetList.Count);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ItemListController.SelectContent patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Announces an item-list row: name, quantity, the description when Auto Detail is on
        /// (otherwise on the details key), and position. Shared by cursor movement and the
        /// list-entry reader; deduplicated on the announcement text.
        /// </summary>
        internal static void Announce(ItemListContentData itemData, int index, int listCount)
        {
            // Store for the details (I) and usable-by (U) keys
            ItemMenuState.LastSelectedItem = itemData;

            // Remove icon markup from name (e.g., <ic_Drag>, <IC_DRAG>)
            string itemName = StripIconMarkup(itemData.Name);
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

            // Auto Detail: include the description the details key would read
            if (PreferencesManager.AutoDetailEnabled)
            {
                string description = StripIconMarkup(itemData.Description);
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

            announcement = FFIV_ScreenReader.Utils.MenuPosition.Format(announcement, index, listCount);
            FFIV_ScreenReaderMod.SpeakText(announcement);
        }
    }

    /// <summary>
    /// Re-announces the focused row when the item list (Use / Key Items / Sort) (re)gains focus —
    /// on entry and on back-out from target selection. ItemListController.SelectContent only fires
    /// on cursor movement, so (re)entry was silent. The prefix clears the row guard so a
    /// SelectContent fired inside the Init body still speaks; the postfix reads the focused row one
    /// frame later through the same deduplicated announcer, so the two paths never double up.
    /// </summary>
    [HarmonyPatch]
    public static class ItemListController_ListInit_Patch
    {
        static System.Collections.Generic.IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var name in new[] { "UseSelectInit", "ImportantSelectInit", "OrganizeSelectInit" })
            {
                var method = AccessTools.Method(typeof(Il2CppLast.UI.KeyInput.ItemListController), name);
                if (method != null)
                    yield return method;
                else
                    MelonLogger.Warning($"[ItemMenu] ItemListController.{name} not found");
            }
        }

        [HarmonyPrefix]
        public static void Prefix() => AnnouncementDeduplicator.Reset(AnnouncementContexts.ITEM_LIST);

        [HarmonyPostfix]
        public static void Postfix(Il2CppLast.UI.KeyInput.ItemListController __instance)
        {
            CoroutineManager.StartManaged(AnnounceFocusAfterFrame(__instance));
        }

        private static System.Collections.IEnumerator AnnounceFocusAfterFrame(Il2CppLast.UI.KeyInput.ItemListController controller)
        {
            yield return null; // let the list and cursor settle

            try
            {
                if (controller == null || controller.gameObject == null || !controller.gameObject.activeInHierarchy)
                    yield break;

                var dataList = controller.dataList;
                var cursor = controller.selectCursor;
                if (dataList == null || cursor == null)
                    yield break;

                var list = new Il2CppSystem.Collections.Generic.List<ItemListContentData>(dataList);
                var itemData = SelectContentHelper.TryGetItem(list, cursor.Index);
                if (itemData != null)
                    ItemListController_SelectContent_Patch.Announce(itemData, cursor.Index, list.Count);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[ItemMenu] Error reading item list focus: {ex.Message}");
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
        private const string DEDUP_CONTEXT = AnnouncementContexts.EQUIPMENT_SELECT;

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

                // Description for the details (I) key; spoken on focus only with Auto Detail
                string description = StripIconMarkup(equipmentData.Description);
                if (PreferencesManager.AutoDetailEnabled && !string.IsNullOrEmpty(description))
                {
                    announcement += $", {description}";
                }

                // Skip duplicates
                if (!AnnouncementDeduplicator.ShouldAnnounce(DEDUP_CONTEXT, announcement))
                {
                    return;
                }

                // Set equipment menu state active
                EquipmentMenuState.SetActive();
                EquipmentDetails.LastDescription = description;

                announcement = FFIV_ScreenReader.Utils.MenuPosition.Format(announcement, index, __instance.ContentDataList.Count);
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
                    if (itemData != null && !string.IsNullOrEmpty(itemData.Name))
                    {
                        equippedItem = itemData.Name;

                        // Get parameter message (ATK +15, DEF +8, etc.)
                        string paramMessage = itemData.ParameterMessage;
                        if (!string.IsNullOrEmpty(paramMessage))
                        {
                            equippedItem += ", " + paramMessage;
                        }
                    }
                    else if (!string.IsNullOrEmpty(slotName))
                    {
                        // Nothing equipped in this slot
                        equippedItem = T("Empty");
                    }
                    // Own try: the getter may throw on an empty-slot stub, which must not cost
                    // the slot announcement below.
                    try { EquipmentDetails.LastDescription = itemData?.Deiscription; } // game typo
                    catch { EquipmentDetails.LastDescription = null; }
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

                announcement = FFIV_ScreenReader.Utils.MenuPosition.Format(announcement, index, __instance.contentList.Count);
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
        private const string DEDUP_CONTEXT = AnnouncementContexts.ITEM_USE_TARGET;

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
                Announce(targetList, index);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ItemUseController.SelectContent patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Announces the item-use target at <paramref name="index"/> (name, HP/MP, status, position).
        /// Shared by cursor movement and the target-entry reader; deduplicated on the text.
        /// </summary>
        internal static void Announce(Il2CppSystem.Collections.Generic.List<Il2CppLast.UI.KeyInput.ItemTargetSelectContentController> targetList, int index)
        {
            var data = SelectContentHelper.TryGetItem(targetList, index)?.CurrentData;
            string characterName = data?.Name;
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

            announcement = FFIV_ScreenReader.Utils.MenuPosition.Format(announcement, index, targetList.Count);
            FFIV_ScreenReaderMod.SpeakText(announcement);
        }
    }

    /// <summary>
    /// Re-announces the focused character when item-use target selection (single or all) begins.
    /// Same prefix-reset / deferred-read pattern as ItemListController_ListInit_Patch. Reads the
    /// targets through GetTargets(), the display-order list SelectContent itself receives.
    /// </summary>
    [HarmonyPatch]
    public static class ItemUseController_TargetInit_Patch
    {
        static System.Collections.Generic.IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var name in new[] { "SingleInit", "AllInit" })
            {
                var method = AccessTools.Method(typeof(Il2CppLast.UI.KeyInput.ItemUseController), name);
                if (method != null)
                    yield return method;
                else
                    MelonLogger.Warning($"[ItemMenu] ItemUseController.{name} not found");
            }
        }

        [HarmonyPrefix]
        public static void Prefix() => AnnouncementDeduplicator.Reset(AnnouncementContexts.ITEM_USE_TARGET);

        [HarmonyPostfix]
        public static void Postfix(Il2CppLast.UI.KeyInput.ItemUseController __instance)
        {
            CoroutineManager.StartManaged(AnnounceFocusAfterFrame(__instance));
        }

        private static System.Collections.IEnumerator AnnounceFocusAfterFrame(Il2CppLast.UI.KeyInput.ItemUseController controller)
        {
            yield return null; // let the target list and cursor settle

            try
            {
                if (controller == null || controller.gameObject == null || !controller.gameObject.activeInHierarchy)
                    yield break;

                var targets = controller.GetTargets();
                var cursor = controller.selectCursor;
                if (targets == null || cursor == null)
                    yield break;

                ItemUseController_SelectContent_Patch.Announce(
                    new Il2CppSystem.Collections.Generic.List<Il2CppLast.UI.KeyInput.ItemTargetSelectContentController>(targets),
                    cursor.Index);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[ItemMenu] Error reading item target focus: {ex.Message}");
            }
        }
    }
}
