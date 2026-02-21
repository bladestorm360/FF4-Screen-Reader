using System;
using MelonLoader;
using FFIV_ScreenReader.Patches;
using FFIV_ScreenReader.Utils;
using FFIV_ScreenReader.Field;

namespace FFIV_ScreenReader.Core
{
    /// <summary>
    /// Static helper for game state announcements (character status, gil, map name).
    /// These methods are stateless - they read game data and speak it.
    /// </summary>
    public static class GameAnnouncementHelper
    {
        /// <summary>
        /// Announces the current battle character's HP, MP, and status conditions.
        /// </summary>
        public static void AnnounceCurrentCharacterStatus()
        {
            try
            {
                var activeCharacter = BattleMenuController_SetCommandSelectTarget_Patch.CurrentActiveCharacter;
                if (activeCharacter?.ownedCharacterData == null)
                {
                    FFIV_ScreenReaderMod.SpeakText("Not in battle or no active character");
                    return;
                }

                var charData = activeCharacter.ownedCharacterData;
                string vitals = CharacterStatusHelper.GetVitalsString(charData.parameter);
                string conditions = CharacterStatusHelper.GetStatusConditions(charData.parameter);
                string status = string.IsNullOrEmpty(conditions)
                    ? $"{charData.Name}, {vitals}"
                    : $"{charData.Name}, {vitals}, {conditions}";
                FFIV_ScreenReaderMod.SpeakText(status);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error announcing character status: {ex.Message}");
                FFIV_ScreenReaderMod.SpeakText("Error reading character status");
            }
        }

        /// <summary>
        /// Announces the player's current gil amount.
        /// </summary>
        public static void AnnounceGilAmount()
        {
            try
            {
                var userDataManager = Il2CppLast.Management.UserDataManager.Instance();

                if (userDataManager == null)
                {
                    FFIV_ScreenReaderMod.SpeakText("User data not available");
                    return;
                }

                int gil = userDataManager.OwendGil;
                string gilMessage = $"{gil:N0} gil";

                FFIV_ScreenReaderMod.SpeakText(gilMessage);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error announcing gil amount: {ex.Message}");
                FFIV_ScreenReaderMod.SpeakText("Error reading gil amount");
            }
        }

        /// <summary>
        /// Announces the current map name.
        /// </summary>
        public static void AnnounceCurrentMap()
        {
            try
            {
                string mapName = MapNameResolver.GetCurrentMapName();
                FFIV_ScreenReaderMod.SpeakText(mapName);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error announcing current map: {ex.Message}");
                FFIV_ScreenReaderMod.SpeakText("Error reading map name");
            }
        }
    }
}
