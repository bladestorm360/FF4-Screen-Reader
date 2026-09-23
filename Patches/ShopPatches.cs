using HarmonyLib;
using Il2CppLast.UI.KeyInput;
using FFIV_ScreenReader.Core;
using FFIV_ScreenReader.Utils;
using System.Collections;
using MelonLoader;
using System;
using static FFIV_ScreenReader.Utils.ModTextTranslator;

// Import MenuState classes
using ShopState = FFIV_ScreenReader.Core.ShopState;

namespace FFIV_ScreenReader.Patches
{
    /// <summary>
    /// Tracks shop menu data for 'I' key item description support.
    /// State is delegated to ShopState.IsActive - no separate boolean.
    /// </summary>
    public static class ShopMenuTracker
    {
        public static ShopInfoController ActiveInfoController { get; set; }
        public static string LastItemDescription { get; set; }
        public static string LastItemMpCost { get; set; }
        public static string LastItemName { get; set; }
        public static string LastItemPrice { get; set; }

        /// <summary>
        /// Validates that shop menu is actually active and visible.
        /// Clears stale state if controller is no longer active.
        /// </summary>
        public static bool ValidateState()
        {
            if (ShopState.IsActive && ActiveInfoController != null)
            {
                if (ActiveInfoController.gameObject == null ||
                    !ActiveInfoController.gameObject.activeInHierarchy)
                {
                    // Controller is no longer active, clear state
                    Reset();
                    return false;
                }
            }
            return ShopState.IsActive;
        }

        public static void Reset()
        {
            ActiveInfoController = null;
            LastItemDescription = null;
            LastItemMpCost = null;
            LastItemName = null;
            LastItemPrice = null;
        }

        // ShopController.State values
        public const int STATE_SELECT_COMMAND = 1;
        public const int STATE_SELECT_SELL_ITEM = 3;

        /// <summary>
        /// Current ShopController state (the game's own "which panel has focus"), or -1 if the
        /// shop controller isn't available.
        /// </summary>
        public static int GetShopState()
        {
            try
            {
                var shop = UnityEngine.Object.FindObjectOfType<ShopController>();
                var current = shop?.stateMachine?.current;
                return current != null ? (int)current.Tag : -1;
            }
            catch
            {
                return -1; // Controller torn down mid-transition
            }
        }
    }

    /// <summary>
    /// Announces shop item details when 'I' key is pressed
    /// </summary>
    public static class ShopDetailsAnnouncer
    {
        /// <param name="interrupt">
        /// When true (the details key), the announcement interrupts current speech.
        /// When false (Auto Detail on focus), it is queued after the item name/price so it
        /// never cuts off the name announcement.
        /// </param>
        public static void AnnounceCurrentItemDetails(bool interrupt = true)
        {
            try
            {
                // Verify shop menu is actually active
                if (!ShopMenuTracker.ValidateState())
                {
                    return; // Silently fail if not active
                }

                // Build announcement from stored data - description only for I key
                string announcement = ShopMenuTracker.LastItemDescription;

                if (!string.IsNullOrEmpty(ShopMenuTracker.LastItemMpCost))
                {
                    announcement += $". {ShopMenuTracker.LastItemMpCost}";
                }

                if (!string.IsNullOrEmpty(announcement))
                {
                    FFIV_ScreenReaderMod.SpeakText(announcement, interrupt: interrupt);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error announcing shop details: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patches for shop menu navigation.
    ///
    /// Working:
    /// - Shop command menu (Buy/Sell/Equipment/Back)
    /// - Item lists for buying/selling (item name + price)
    /// - 'I' key support for re-reading item descriptions
    /// - Quantity selection (quantity + total price)
    /// </summary>
    [HarmonyPatch]
    public static class ShopPatches
    {
        private const string DEDUP_CONTEXT = AnnouncementContexts.SHOP_ITEM;

        /// <summary>
        /// Announces shop command menu options (Buy, Sell, Equipment, Back).
        /// Also restores ShopState when returning from Equipment submenu.
        /// </summary>
        [HarmonyPatch(typeof(ShopCommandMenuController), nameof(ShopCommandMenuController.SetCursor))]
        [HarmonyPostfix]
        private static void AfterShopCommandSetCursor(ShopCommandMenuController __instance, int index)
        {
            try
            {
                // Restore ShopState if returning from Equipment submenu
                // Skip announcement because generic cursor already read it (ShopState was inactive)
                if (ShopState.EnteredEquipmentSubmenu)
                {
                    ShopState.EnteredEquipmentSubmenu = false;
                    ShopState.SetActive();
                    return; // Generic cursor already announced, don't duplicate
                }

                // Only announce while the command bar has focus. On shop entry the state goes
                // straight to the product list, so the setup-time cursor placement stays silent
                // (no double "Buy"); cancelling back to the bar is SelectCommand and announces.
                int state = ShopMenuTracker.GetShopState();
                if (state >= 0 && state != ShopMenuTracker.STATE_SELECT_COMMAND)
                    return;

                if (__instance?.contentList == null || index < 0 || index >= __instance.contentList.Count)
                    return;

                var content = __instance.contentList[index];
                if (content?.view?.nameText == null)
                    return;

                string commandText = content.view.nameText.text;
                if (string.IsNullOrEmpty(commandText))
                    return;

                commandText = FFIV_ScreenReader.Utils.MenuPosition.Format(commandText, index, __instance.contentList.Count);
                CoroutineManager.StartManaged(DelayedAnnounceShopCommand(commandText));
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in AfterShopCommandSetCursor: {ex.Message}");
            }
        }

        /// <summary>
        /// Announces item name + price AND stores description when SetDescription is called.
        /// SetDescription is called each time an item is selected in the shop.
        /// We find the currently selected item by searching the shop's product list.
        /// </summary>
        [HarmonyPatch(typeof(ShopInfoController), nameof(ShopInfoController.SetDescription))]
        [HarmonyPostfix]
        private static void AfterSetDescription(ShopInfoController __instance, string value)
        {
            try
            {
                // Empty description means returning to command menu - don't announce stale item data.
                // In the sell list it can also be an empty slot, which is announced below.
                if (string.IsNullOrEmpty(value) && ShopMenuTracker.GetShopState() != ShopMenuTracker.STATE_SELECT_SELL_ITEM)
                {
                    // Also reset dedup so next submenu entry announces first item
                    AnnouncementDeduplicator.Reset(DEDUP_CONTEXT);
                    return;
                }

                // Set shop state active first (handles ClearOtherMenuStates)
                ShopState.SetActive();
                ShopMenuTracker.ActiveInfoController = __instance;

                // Try to find the currently selected item by searching for active ShopListMainContentController
                string itemName = null;
                string price = null;
                int shopItemIndex = -1;
                int shopItemCount = 0;

                var shopListController = UnityEngine.Object.FindObjectOfType<ShopListMainContentController>();
                if (shopListController != null)
                {
                    // In IL2CPP, private fields are exposed directly through the interop
                    try
                    {
                        var cursor = shopListController.selectCursor;
                        var productList = shopListController.productContentList;

                        if (cursor != null && productList != null)
                        {
                            int index = cursor.Index;
                            shopItemIndex = index;

                            // productContentList is a fixed pool whose unused entries still hold
                            // other products' names, so count only the active (shown) entries.
                            for (int i = 0; i < productList.Count; i++)
                            {
                                var entry = productList[i];
                                if (entry != null && entry.gameObject != null && entry.gameObject.activeInHierarchy)
                                    shopItemCount++;
                            }

                            if (index >= 0 && index < productList.Count)
                            {
                                var item = productList[index];
                                if (item != null)
                                {
                                    itemName = item.iconTextView?.nameText?.text;
                                    price = item.shopListItemContentView?.priceText?.text;
                                }
                            }
                        }
                    }
                    catch (Exception fieldEx)
                    {
                        MelonLogger.Warning($"[Shop] Field access failed: {fieldEx.Message}");
                    }
                }

                // An empty sell-list slot must not touch any I/U target, so the description,
                // MP cost and item name all keep referring to the same last item.
                bool emptySlot = string.IsNullOrEmpty(itemName) && shopItemIndex >= 0;

                // Store the description and MP cost for the I key
                if (!emptySlot)
                {
                    ShopMenuTracker.LastItemDescription = value;
                    ShopMenuTracker.LastItemMpCost = __instance.itemInfoController?.shopItemInfoView?.mpText?.text;
                }

                if (string.IsNullOrEmpty(itemName))
                {
                    if (emptySlot && AnnouncementDeduplicator.ShouldAnnounce(DEDUP_CONTEXT, shopItemIndex, T("Empty")))
                        CoroutineManager.StartManaged(DelayedSpeak(
                            FFIV_ScreenReader.Utils.MenuPosition.Format(T("Empty"), shopItemIndex, shopItemCount)));
                    return;
                }

                // Store for later (the U key resolves the item by name)
                ShopMenuTracker.LastItemName = itemName;
                ShopMenuTracker.LastItemPrice = price;

                // Build announcement - item name + price first, description available via I key
                if (!string.IsNullOrEmpty(itemName))
                {
                    string announcement = string.IsNullOrEmpty(price) ? itemName : $"{itemName}, {price}";
                    announcement = FFIV_ScreenReader.Utils.MenuPosition.Format(announcement, shopItemIndex, shopItemCount);

                    // Deduplicate by content
                    if (AnnouncementDeduplicator.ShouldAnnounce(DEDUP_CONTEXT, itemName))
                    {
                        CoroutineManager.StartManaged(DelayedAnnounceShopItem(announcement));
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in AfterSetDescription: {ex.Message}");
            }
        }

        /// <summary>
        /// Announces the starting quantity and total when the buy/sell trade window opens.
        /// </summary>
        [HarmonyPatch(typeof(ShopTradeWindowController), nameof(ShopTradeWindowController.Show))]
        [HarmonyPostfix]
        private static void AfterTradeWindowShow(ShopTradeWindowController __instance)
        {
            AnnounceTradeWindowQuantity(__instance);
        }

        /// <summary>
        /// Announces quantity changes in the buy/sell trade window.
        /// </summary>
        [HarmonyPatch(typeof(ShopTradeWindowController), nameof(ShopTradeWindowController.AddCount))]
        [HarmonyPostfix]
        private static void AfterAddCount(ShopTradeWindowController __instance)
        {
            AnnounceTradeWindowQuantity(__instance);
        }

        [HarmonyPatch(typeof(ShopTradeWindowController), nameof(ShopTradeWindowController.TakeCount))]
        [HarmonyPostfix]
        private static void AfterTakeCount(ShopTradeWindowController __instance)
        {
            AnnounceTradeWindowQuantity(__instance);
        }

        private static void AnnounceTradeWindowQuantity(ShopTradeWindowController controller)
        {
            try
            {
                if (controller != null)
                    CoroutineManager.StartManaged(DelayedAnnounceQuantity(controller));
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in AnnounceTradeWindowQuantity: {ex.Message}");
            }
        }

        private static IEnumerator DelayedAnnounceShopCommand(string commandText)
        {
            yield return null; // Wait one frame for UI to update
            FFIV_ScreenReaderMod.SpeakText($"{commandText}");
        }

        private static IEnumerator DelayedAnnounceShopItem(string itemText)
        {
            yield return null; // Wait one frame for UI to update
            FFIV_ScreenReaderMod.SpeakText($"{itemText}");

            // Auto Detail: queue the description + MP cost normally reached with the details key,
            // after the name/price (interrupt: false) so it never cuts off the name. This coroutine
            // is only started after the shop-item content dedup passes, so it fires once per focused
            // item (same-index debounce).
            if (PreferencesManager.AutoDetailEnabled)
            {
                ShopDetailsAnnouncer.AnnounceCurrentItemDetails(interrupt: false);
            }
        }

        private static IEnumerator DelayedSpeak(string text)
        {
            yield return null; // Wait one frame for UI to update
            FFIV_ScreenReaderMod.SpeakText(text);
        }

        private static IEnumerator DelayedAnnounceQuantity(ShopTradeWindowController controller)
        {
            yield return null; // Wait one frame for the count and total text to update

            try
            {
                if (controller == null)
                    yield break;

                // Quantity from the controller's count; total from the view's rendered price text
                int quantity = controller.selectedCount;
                string totalPrice = controller.view?.totarlPriceText?.text;
                string announcement = string.IsNullOrEmpty(totalPrice)
                    ? string.Format(T("Quantity: {0}"), quantity)
                    : string.Format(T("Quantity: {0}, Total: {1}"), quantity, totalPrice);

                FFIV_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error announcing trade quantity: {ex.Message}");
            }
        }
    }

    // NOTE: OnHide method does not exist in FF4's ShopCommandMenuController
    // Shop state is managed through ValidateState() checks instead
}
