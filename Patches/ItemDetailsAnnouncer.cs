using System;
using System.Collections.Generic;
using MelonLoader;
using FFIV_ScreenReader.Core;
using static FFIV_ScreenReader.Utils.ModTextTranslator;

// Type aliases for IL2CPP types
using UserDataManager = Il2CppLast.Management.UserDataManager;
using EquipUtility = Il2CppLast.Systems.EquipUtility;
using FieldController = Il2CppLast.Map.FieldController;
using OwnedCharacterData = Il2CppLast.Data.User.OwnedCharacterData;

namespace FFIV_ScreenReader.Patches
{
    /// <summary>
    /// Item-menu details: which party members can equip the focused item (U key) and the
    /// focused item's description (I key). Also holds the party/announcement helpers shared
    /// with the shop's usable-by lookup.
    /// </summary>
    public static class ItemDetailsAnnouncer
    {
        // ContentType values - centralized in Constants
        private const int CONTENT_TYPE_WEAPON = Constants.ItemContentTypes.WEAPON;
        private const int CONTENT_TYPE_ARMOR = Constants.ItemContentTypes.ARMOR;

        /// <summary>
        /// Announces which party members can equip the currently selected item.
        /// Only announces for weapons and armor, silent for other items.
        /// </summary>
        public static void AnnounceEquipRequirements()
        {
            try
            {
                var itemData = ItemMenuState.LastSelectedItem;
                if (itemData == null)
                    return;

                // Only process equipment (weapons and armor)
                int itemType = itemData.ItemType;
                if (itemType != CONTENT_TYPE_WEAPON && itemType != CONTENT_TYPE_ARMOR)
                    return;

                // From here on the row is equipment, so the key never goes silent: anything that
                // can't be read speaks the "Equipment info unavailable" fallback.
                var userDataManager = UserDataManager.Instance();
                var ownedItemData = userDataManager?.SearchOwnedItem(itemData.contentId);
                if (ownedItemData == null)
                {
                    FFIV_ScreenReaderMod.SpeakText(T("Equipment info unavailable"), interrupt: true);
                    return;
                }

                string announcement = BuildAnnouncement(character => EquipUtility.CanEquipped(ownedItemData, character.JobId));
                FFIV_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[ItemDetails] Error: {ex.Message}");
                FFIV_ScreenReaderMod.SpeakText(T("Equipment info unavailable"), interrupt: true);
            }
        }

        /// <summary>
        /// Announces the description of the currently selected item (details key).
        /// </summary>
        public static void AnnounceDescription()
        {
            SpeakDescription(ItemMenuState.LastSelectedItem?.Description);
        }

        /// <summary>
        /// Speaks a description for the details key, or "No description" when there is none.
        /// </summary>
        internal static void SpeakDescription(string description)
        {
            description = Utils.TextUtils.StripIconMarkup(description);
            FFIV_ScreenReaderMod.SpeakText(string.IsNullOrEmpty(description) ? T("No description") : description, interrupt: true);
        }

        /// <summary>
        /// Builds "Can equip: A, B" over the current party in on-screen order, or the
        /// "no party members" message. Never null: when the party can't be read it returns the
        /// "Equipment info unavailable" fallback, so the U key is never silent on equipment.
        /// </summary>
        internal static string BuildAnnouncement(Func<OwnedCharacterData, bool> canEquip)
        {
            var partyMembers = GetPartyMembers();
            if (partyMembers == null || partyMembers.Count == 0)
                return T("Equipment info unavailable");

            var canEquipNames = new List<string>();
            foreach (var character in partyMembers)
            {
                try
                {
                    if (canEquip(character) && !string.IsNullOrEmpty(character.Name))
                        canEquipNames.Add(character.Name);
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[ItemDetails] Error checking character: {ex.Message}");
                }
            }

            return canEquipNames.Count == 0
                ? T("No party members can equip")
                : string.Format(T("Can equip: {0}"), string.Join(", ", canEquipNames));
        }

        /// <summary>
        /// Gets the current party members in display (apparent) order.
        /// </summary>
        private static List<OwnedCharacterData> GetPartyMembers()
        {
            try
            {
                var userDataManager = UserDataManager.Instance();
                var corpsList = FieldController.GetCorpsListCloneWithApparentOrder();
                var allCharacters = userDataManager?.GetOwnedCharactersClone(false);
                if (corpsList == null || allCharacters == null)
                    return null;

                var charactersById = new Dictionary<int, OwnedCharacterData>();
                foreach (var character in allCharacters)
                {
                    if (character != null)
                        charactersById[character.Id] = character;
                }

                var partyMembers = new List<OwnedCharacterData>();
                foreach (var corps in corpsList)
                {
                    if (corps != null && charactersById.TryGetValue(corps.CharacterId, out var member))
                        partyMembers.Add(member);
                }

                return partyMembers;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[ItemDetails] Error getting party members: {ex.Message}");
                return null;
            }
        }
    }
}
