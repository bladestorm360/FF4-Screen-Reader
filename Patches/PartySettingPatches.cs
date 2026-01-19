using System;
using HarmonyLib;
using MelonLoader;
using Il2CppLast.UI.Common;
using Il2CppLast.Data.User;
using Il2CppLast.Defaine.User;
using Il2CppLast.Systems;
using FFIV_ScreenReader.Core;
using FFIV_ScreenReader.Utils;
using FFIV_ScreenReader.Menus;
using GameCursor = Il2CppLast.UI.Cursor;

// Import MenuState classes
using PartyMenuState = FFIV_ScreenReader.Core.PartyMenuState;

namespace FFIV_ScreenReader.Patches
{
    /// <summary>
    /// Patches for party setting/formation screen (used when dividing party into multiple groups).
    /// Announces character names, stats, and party assignments in character list.
    /// Announces slot positions with occupancy in party slot grid.
    /// Also detects and announces transitions between sections.
    /// </summary>
    [HarmonyPatch(typeof(PartySettingMenuBaseController), nameof(PartySettingMenuBaseController.SelectContent))]
    public static class PartySettingMenuBaseController_SelectContent_Patch
    {
        private const string DEDUP_CONTEXT = "PartySetting.Select";
        private static PartySettingMenuBaseController.State lastState = PartySettingMenuBaseController.State.None;

        [HarmonyPostfix]
        public static void Postfix(PartySettingMenuBaseController __instance, int index)
        {
            try
            {
                // Safety checks
                if (__instance == null)
                {
                    return;
                }

                // Check for state transitions and announce them
                CheckAndAnnounceStateTransition(__instance);

                // Set party menu state active
                PartyMenuState.SetActive();

                // Check which section we're in
                bool isCharacterList = IsNavigatingCharacterList(__instance, index);
                bool isSlotGrid = IsNavigatingSlotGrid(__instance, index);

                if (isCharacterList)
                {
                    AnnounceCharacter(__instance, index);
                }
                else if (isSlotGrid)
                {
                    AnnounceSlot(__instance, index);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in PartySettingMenuBaseController.SelectContent patch: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Check if state has changed and announce major section transitions
        /// </summary>
        private static void CheckAndAnnounceStateTransition(PartySettingMenuBaseController __instance)
        {
            try
            {
                if (__instance?.stateMachine?.current == null)
                {
                    return;
                }

                var currentState = __instance.stateMachine.current.Tag;

                // Skip if same state
                if (currentState == lastState)
                {
                    return;
                }

                // Announce major section transitions
                string announcement = null;
                if ((lastState == PartySettingMenuBaseController.State.SlotSelect ||
                     lastState == PartySettingMenuBaseController.State.SlotSelect ||
                     lastState == PartySettingMenuBaseController.State.SlotSelecting) &&
                    (currentState == PartySettingMenuBaseController.State.MemberSelect ||
                     currentState == PartySettingMenuBaseController.State.MemberSelect))
                {
                    announcement = "Entering character list.";
                }
                else if ((lastState == PartySettingMenuBaseController.State.MemberSelect ||
                          lastState == PartySettingMenuBaseController.State.MemberSelect ||
                          lastState == PartySettingMenuBaseController.State.MemberSelecting) &&
                         (currentState == PartySettingMenuBaseController.State.SlotSelect ||
                          currentState == PartySettingMenuBaseController.State.SlotSelect))
                {
                    announcement = "Entering party slot grid.";
                }

                lastState = currentState;

                if (!string.IsNullOrEmpty(announcement))
                {
                    FFIV_ScreenReaderMod.SpeakText(announcement);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error checking state transition: {ex.Message}");
            }
        }

        /// <summary>
        /// Announce character info from the character list
        /// </summary>
        private static void AnnounceCharacter(PartySettingMenuBaseController __instance, int index)
        {
            try
            {
                var characterData = SelectContentHelper.TryGetItem(__instance.members, index);
                if (characterData == null)
                    return;

                // Build announcement string
                var announcement = BuildCharacterAnnouncement(__instance, characterData, index);

                if (string.IsNullOrWhiteSpace(announcement))
                {
                    MelonLogger.Warning("PartySettingMenuBaseController: announcement is empty");
                    return;
                }

                // Skip duplicate announcements
                if (!AnnouncementDeduplicator.ShouldAnnounce(DEDUP_CONTEXT, announcement))
                {
                    return;
                }

                FFIV_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error announcing character: {ex.Message}");
            }
        }

        /// <summary>
        /// Announce party slot with occupancy info
        /// </summary>
        private static void AnnounceSlot(PartySettingMenuBaseController __instance, int index)
        {
            try
            {
                int slotCount = __instance.slotCount;

                // The slot grid is arranged as a 2x2 grid PER PARTY
                const int PARTY_WIDTH = 2; // each party occupies 2 columns
                int totalWidth = slotCount * PARTY_WIDTH;
                int row = index / totalWidth;
                int col = index % totalWidth;
                int partyNumber = (col / PARTY_WIDTH) + 1;
                int position = (row * PARTY_WIDTH) + (col % PARTY_WIDTH) + 1;

                // Get the character ID in this slot
                int characterId = __instance.GetSlotPostionCharaterId(slotCount, index);

                string announcement;
                if (characterId == 0)
                {
                    announcement = $"Party {partyNumber}, Position {position}: Empty";
                }
                else
                {
                    // Find the character name
                    string characterName = GetCharacterName(__instance, characterId);
                    if (!string.IsNullOrEmpty(characterName))
                    {
                        announcement = $"Party {partyNumber}, Position {position}: {characterName}";
                    }
                    else
                    {
                        announcement = $"Party {partyNumber}, Position {position}: Character {characterId}";
                    }
                }

                // Skip duplicate announcements
                if (!AnnouncementDeduplicator.ShouldAnnounce(DEDUP_CONTEXT, announcement))
                {
                    return;
                }

                FFIV_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error announcing slot: {ex.Message}");
            }
        }

        /// <summary>
        /// Get character name by ID
        /// </summary>
        private static string GetCharacterName(PartySettingMenuBaseController __instance, int characterId)
        {
            try
            {
                if (__instance.members == null) return null;

                for (int i = 0; i < __instance.members.Count; i++)
                {
                    var member = __instance.members[i];
                    if (member != null && member.Id == characterId)
                    {
                        return member.Name;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error getting character name: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Check if we're navigating the slot grid
        /// </summary>
        private static bool IsNavigatingSlotGrid(PartySettingMenuBaseController controller, int index)
        {
            if (controller?.stateMachine?.current == null)
            {
                return false;
            }

            var currentState = controller.stateMachine.current.Tag;

            bool isSlotState = currentState == PartySettingMenuBaseController.State.SlotSelect ||
                               currentState == PartySettingMenuBaseController.State.SlotSelect ||
                               currentState == PartySettingMenuBaseController.State.SlotSelecting ||
                               currentState == PartySettingMenuBaseController.State.SlotIndexSelect ||
                               currentState == PartySettingMenuBaseController.State.SlotIndexSelecting ||
                               currentState == PartySettingMenuBaseController.State.SlotSelect;

            return isSlotState;
        }

        /// <summary>
        /// Check if we're currently navigating the character list section
        /// </summary>
        private static bool IsNavigatingCharacterList(PartySettingMenuBaseController controller, int index)
        {
            if (controller?.stateMachine?.current == null)
            {
                return false;
            }

            var currentState = controller.stateMachine.current.Tag;

            bool isCharacterListState = currentState == PartySettingMenuBaseController.State.MemberSelect ||
                                        currentState == PartySettingMenuBaseController.State.MemberSelect ||
                                        currentState == PartySettingMenuBaseController.State.MemberSelecting;

            return isCharacterListState;
        }

        /// <summary>
        /// Build announcement string with character name, level, stats, and party assignment.
        /// </summary>
        private static string BuildCharacterAnnouncement(PartySettingMenuBaseController controller, OwnedCharacterData characterData, int index)
        {
            var parts = new System.Collections.Generic.List<string>();

            // Character name
            string characterName = characterData.Name;
            if (!string.IsNullOrWhiteSpace(characterName))
            {
                parts.Add(characterName);
            }
            else
            {
                parts.Add($"Character {index + 1}");
            }

            // Level and stats
            if (characterData.parameter != null)
            {
                var param = characterData.parameter;

                // Level
                int level = param.ConfirmedLevel();
                parts.Add($"Level {level}");

                // HP
                int currentHP = param.CurrentHP;
                int maxHP = param.ConfirmedMaxHp();
                parts.Add($"HP {currentHP}/{maxHP}");

                // MP
                int currentMP = param.CurrentMP;
                int maxMP = param.ConfirmedMaxMp();
                parts.Add($"MP {currentMP}/{maxMP}");
            }

            // Check party assignment
            string partyAssignment = GetPartyAssignment(controller, characterData);
            if (!string.IsNullOrWhiteSpace(partyAssignment))
            {
                parts.Add(partyAssignment);
            }

            return string.Join(", ", parts);
        }

        /// <summary>
        /// Determine which party (if any) this character is assigned to.
        /// </summary>
        private static string GetPartyAssignment(PartySettingMenuBaseController controller, OwnedCharacterData characterData)
        {
            try
            {
                if (controller == null || characterData == null)
                {
                    return null;
                }

                int characterId = characterData.Id;

                // Check slot 1
                if (controller.slot1Members != null && controller.slot1Members.Count > 0)
                {
                    for (int i = 0; i < controller.slot1Members.Count; i++)
                    {
                        if (controller.slot1Members[i] == characterId)
                        {
                            return "Party 1";
                        }
                    }
                }

                // Check slot 2
                if (controller.slot2Members != null && controller.slot2Members.Count > 0)
                {
                    for (int i = 0; i < controller.slot2Members.Count; i++)
                    {
                        if (controller.slot2Members[i] == characterId)
                        {
                            return "Party 2";
                        }
                    }
                }

                // Check slot 3
                if (controller.slot3Members != null && controller.slot3Members.Count > 0)
                {
                    for (int i = 0; i < controller.slot3Members.Count; i++)
                    {
                        if (controller.slot3Members[i] == characterId)
                        {
                            return "Party 3";
                        }
                    }
                }

                // Not assigned to any party
                return "not assigned";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error determining party assignment: {ex.Message}");
                return null;
            }
        }
    }

}
