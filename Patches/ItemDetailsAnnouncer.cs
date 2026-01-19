using System;
using System.Collections.Generic;
using MelonLoader;
using FFIV_ScreenReader.Core;

// Type aliases for IL2CPP types
using UserDataManager = Il2CppLast.Management.UserDataManager;
using EquipUtility = Il2CppLast.Systems.EquipUtility;
using OwnedCharacterData = Il2CppLast.Data.User.OwnedCharacterData;
using OwnedItemData = Il2CppLast.Data.User.OwnedItemData;

namespace FFIV_ScreenReader.Patches
{
    /// <summary>
    /// Announces equipment character requirements when 'I' key is pressed in Items menu.
    /// Only works for equipment (weapons/armor), silent for consumables/key items.
    /// </summary>
    public static class ItemDetailsAnnouncer
    {
        // ContentType values from dump.cs
        private const int CONTENT_TYPE_WEAPON = 2;
        private const int CONTENT_TYPE_ARMOR = 3;

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

                int itemType = itemData.ItemType;
                int contentId = itemData.contentId;

                // Only process equipment (weapons and armor)
                if (itemType != CONTENT_TYPE_WEAPON && itemType != CONTENT_TYPE_ARMOR)
                    return;

                // Get UserDataManager
                var userDataManager = UserDataManager.Instance();
                if (userDataManager == null)
                    return;

                // Get OwnedItemData from contentId
                var ownedItemData = userDataManager.SearchOwnedItem(contentId);
                if (ownedItemData == null)
                    return;

                // Get party members
                var partyMembers = GetPartyMembers(userDataManager);
                if (partyMembers == null || partyMembers.Count == 0)
                    return;

                // Check each party member using EquipUtility.CanEquipped(OwnedItemData, jobId)
                var canEquipNames = new List<string>();
                foreach (var character in partyMembers)
                {
                    if (character == null)
                        continue;

                    try
                    {
                        int jobId = character.JobId;
                        bool canEquip = EquipUtility.CanEquipped(ownedItemData, jobId);
                        string characterName = character.Name;

                        if (canEquip && !string.IsNullOrEmpty(characterName))
                        {
                            canEquipNames.Add(characterName);
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"[ItemDetails] Error checking character: {ex.Message}");
                    }
                }

                // Build and announce the result
                string announcement = BuildAnnouncement(canEquipNames);
                if (!string.IsNullOrEmpty(announcement))
                {
                    FFIV_ScreenReaderMod.SpeakText(announcement, interrupt: true);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[ItemDetails] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the list of current party members.
        /// </summary>
        private static List<OwnedCharacterData> GetPartyMembers(UserDataManager userDataManager)
        {
            try
            {
                // GetCorpsListClone returns the current party corps
                var corpsList = userDataManager.GetCorpsListClone();
                if (corpsList == null || corpsList.Count == 0)
                    return null;

                // Get owned characters and filter by those in party
                var allCharacters = userDataManager.GetOwnedCharactersClone(false);
                if (allCharacters == null)
                    return null;

                // Build a set of character IDs in the current party
                var partyCharacterIds = new HashSet<int>();
                foreach (var corps in corpsList)
                {
                    if (corps != null)
                    {
                        partyCharacterIds.Add(corps.CharacterId);
                    }
                }

                // Filter to only party members
                var partyMembers = new List<OwnedCharacterData>();
                foreach (var character in allCharacters)
                {
                    if (character != null && partyCharacterIds.Contains(character.Id))
                    {
                        partyMembers.Add(character);
                    }
                }

                return partyMembers;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[ItemDetails] Error getting party members: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Builds the announcement string from the list of equippable character names.
        /// </summary>
        private static string BuildAnnouncement(List<string> characterNames)
        {
            if (characterNames == null || characterNames.Count == 0)
            {
                return "No party members can equip";
            }

            return "Can equip: " + string.Join(", ", characterNames);
        }
    }
}
