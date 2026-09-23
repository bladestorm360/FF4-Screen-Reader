using System;
using MelonLoader;
using FFIV_ScreenReader.Core;
using FFIV_ScreenReader.Patches;
using FFIV_ScreenReader.Utils;

using MasterManager = Il2CppLast.Data.Master.MasterManager;
using MessageManager = Il2CppLast.Management.MessageManager;
using Content = Il2CppLast.Data.Master.Content;
using Weapon = Il2CppLast.Data.Master.Weapon;
using Armor = Il2CppLast.Data.Master.Armor;
using JobGroup = Il2CppLast.Data.Master.JobGroup;
using EquipUtility = Il2CppLast.Systems.EquipUtility;

namespace FFIV_ScreenReader.Menus
{
    /// <summary>
    /// Announces which party members can equip the focused weapon/armor.
    /// Triggered by the U key (or right-stick-left on gamepad).
    /// Works in shops (looks up master data by item name) and the item menu (delegates to
    /// ItemDetailsAnnouncer). Silent for anything that isn't equipment.
    /// </summary>
    public static class UsableByAnnouncer
    {
        private const int CONTENT_TYPE_WEAPON = Constants.ItemContentTypes.WEAPON;
        private const int CONTENT_TYPE_ARMOR = Constants.ItemContentTypes.ARMOR;

        public static void AnnounceForCurrentContext()
        {
            try
            {
                if (ShopMenuTracker.ValidateState())
                {
                    string announcement = BuildShopItemAnnouncement();
                    if (!string.IsNullOrEmpty(announcement))
                        FFIV_ScreenReaderMod.SpeakText(announcement, interrupt: true);
                    return;
                }

                if (ItemMenuState.IsActive)
                    ItemDetailsAnnouncer.AnnounceEquipRequirements();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[UsableBy] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Builds the "Can equip: ..." string for the focused shop item.
        /// Returns null if nothing is focused or the item isn't equipment; returns the
        /// "Equipment info unavailable" fallback if the row can't be resolved to master data,
        /// so the key is never silent on a real row.
        /// </summary>
        private static string BuildShopItemAnnouncement()
        {
            string itemName = ShopMenuTracker.LastItemName;
            if (string.IsNullOrEmpty(itemName))
                return null;

            var masterManager = MasterManager.Instance;
            if (masterManager == null)
                return ModTextTranslator.T("Equipment info unavailable");

            if (!TryResolveContent(masterManager, itemName, out int itemType, out int itemId))
                return ModTextTranslator.T("Equipment info unavailable");

            // Not equipment: nothing to say (by design)
            if (itemType != CONTENT_TYPE_WEAPON && itemType != CONTENT_TYPE_ARMOR)
                return null;

            int equipJobGroupId = itemType == CONTENT_TYPE_WEAPON
                ? masterManager.GetData<Weapon>(itemId)?.EquipJobGroupId ?? 0
                : masterManager.GetData<Armor>(itemId)?.EquipJobGroupId ?? 0;
            if (equipJobGroupId <= 0)
                return ModTextTranslator.T("Equipment info unavailable");

            var jobGroup = masterManager.GetData<JobGroup>(equipJobGroupId);
            if (jobGroup == null)
                return ModTextTranslator.T("Equipment info unavailable");

            return ItemDetailsAnnouncer.BuildAnnouncement(character => EquipUtility.CanEquipped(jobGroup, character.JobId));
        }

        /// <summary>
        /// Iterates the Content master list to resolve a display name into (TypeId, TypeValue).
        /// Content is the unified item table: TypeId is the ContentType (2=weapon, 3=armor, ...),
        /// TypeValue is the ID within the corresponding Weapon/Armor/Item master list.
        /// Matching by the displayed name avoids depending on the shop row's content id.
        /// </summary>
        private static bool TryResolveContent(MasterManager masterManager, string itemName, out int itemType, out int itemId)
        {
            itemType = -1;
            itemId = 0;

            var messageManager = MessageManager.Instance;
            string target = TextUtils.StripIconMarkup(itemName);
            if (messageManager == null || string.IsNullOrEmpty(target))
                return false;

            try
            {
                var contents = masterManager.GetList<Content>();
                if (contents == null)
                    return false;

                foreach (var kvp in contents)
                {
                    var content = kvp.Value;
                    string mes = content?.MesIdName;
                    if (string.IsNullOrEmpty(mes)) continue;
                    string localized = messageManager.GetMessage(mes, false);
                    if (string.Equals(TextUtils.StripIconMarkup(localized), target, StringComparison.Ordinal))
                    {
                        itemType = content.TypeId;
                        itemId = content.TypeValue;
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[UsableBy] Content scan failed: {ex.Message}");
            }

            return false;
        }
    }
}
