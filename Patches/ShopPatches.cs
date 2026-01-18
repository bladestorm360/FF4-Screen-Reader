using HarmonyLib;
using Il2CppLast.UI.KeyInput;
using FFIV_ScreenReader.Core;
using FFIV_ScreenReader.Utils;
using System.Collections;
using MelonLoader;
using System;

namespace FFIV_ScreenReader.Patches
{
    /// <summary>
    /// Tracks shop menu state to prevent 'I' key from working outside shop menus
    /// </summary>
    public static class ShopMenuTracker
    {
        public static bool IsShopMenuActive { get; set; }
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
            if (IsShopMenuActive && ActiveInfoController != null)
            {
                if (ActiveInfoController.gameObject == null ||
                    !ActiveInfoController.gameObject.activeInHierarchy)
                {
                    // Controller is no longer active, clear state
                    Reset();
                    return false;
                }
            }
            return IsShopMenuActive;
        }

        public static void Reset()
        {
            IsShopMenuActive = false;
            ActiveInfoController = null;
            LastItemDescription = null;
            LastItemMpCost = null;
            LastItemName = null;
            LastItemPrice = null;
        }
    }

    /// <summary>
    /// Announces shop item details when 'I' key is pressed
    /// </summary>
    public static class ShopDetailsAnnouncer
    {
        public static void AnnounceCurrentItemDetails()
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
                    MelonLogger.Msg($"[Shop Details - I Key] {announcement}");
                    FFIV_ScreenReaderMod.SpeakText(announcement);
                }
                else
                {
                    MelonLogger.Msg("[Shop Details - I Key] No description available");
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
        private static string lastAnnouncedItem = "";

        /// <summary>
        /// Announces shop command menu options (Buy, Sell, Equipment, Back).
        /// </summary>
        [HarmonyPatch(typeof(ShopCommandMenuController), nameof(ShopCommandMenuController.SetCursor))]
        [HarmonyPostfix]
        private static void AfterShopCommandSetCursor(ShopCommandMenuController __instance, int index)
        {
            try
            {
                if (__instance?.contentList == null || index < 0 || index >= __instance.contentList.Count)
                    return;

                var content = __instance.contentList[index];
                if (content?.view?.nameText == null)
                    return;

                string commandText = content.view.nameText.text;
                if (string.IsNullOrEmpty(commandText))
                    return;

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
                // Store the controller and description for 'I' key access
                ShopMenuTracker.ActiveInfoController = __instance;
                ShopMenuTracker.IsShopMenuActive = true;
                ShopMenuTracker.LastItemDescription = value;

                // Also get MP cost if available
                string mpCost = __instance.itemInfoController?.shopItemInfoView?.mpText?.text;
                ShopMenuTracker.LastItemMpCost = mpCost;

                // Try to find the currently selected item by searching for active ShopListMainContentController
                string itemName = null;
                string price = null;

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

                // Store for later
                ShopMenuTracker.LastItemName = itemName;
                ShopMenuTracker.LastItemPrice = price;

                // Build announcement - item name + price first, description available via I key
                if (!string.IsNullOrEmpty(itemName))
                {
                    string announcement = string.IsNullOrEmpty(price) ? itemName : $"{itemName}, {price}";

                    // Deduplicate by content
                    if (itemName != lastAnnouncedItem)
                    {
                        lastAnnouncedItem = itemName;
                        MelonLogger.Msg($"[Shop Item] {announcement}");
                        CoroutineManager.StartManaged(DelayedAnnounceShopItem(announcement));
                    }
                }
                else
                {
                    // Fallback: just log description was stored
                    MelonLogger.Msg($"[Shop Description Stored] {value}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in AfterSetDescription: {ex.Message}");
            }
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
                if (controller?.view == null)
                    return;

                // Get quantity and total price
                string quantity = controller.view.selectCountText?.text;
                string totalPrice = controller.view.totarlPriceText?.text;

                if (!string.IsNullOrEmpty(quantity))
                {
                    string announcement = string.IsNullOrEmpty(totalPrice)
                        ? quantity
                        : $"{quantity}, {totalPrice}";

                    CoroutineManager.StartManaged(DelayedAnnounceQuantity(announcement));
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in AnnounceTradeWindowQuantity: {ex.Message}");
            }
        }

        private static IEnumerator DelayedAnnounceShopCommand(string commandText)
        {
            yield return null; // Wait one frame for UI to update
            MelonLogger.Msg($"[Shop Command] {commandText}");
            FFIV_ScreenReaderMod.SpeakText($"{commandText}");
        }

        private static IEnumerator DelayedAnnounceShopItem(string itemText)
        {
            yield return null; // Wait one frame for UI to update
            MelonLogger.Msg($"[Shop Item] {itemText}");
            FFIV_ScreenReaderMod.SpeakText($"{itemText}");
        }

        private static IEnumerator DelayedAnnounceQuantity(string quantityText)
        {
            yield return null; // Wait one frame for UI to update
            MelonLogger.Msg($"[Shop Quantity] {quantityText}");
            FFIV_ScreenReaderMod.SpeakText($"{quantityText}");
        }
    }

    // NOTE: OnHide method does not exist in FF4's ShopCommandMenuController
    // Shop state is managed through ValidateState() checks instead
}
