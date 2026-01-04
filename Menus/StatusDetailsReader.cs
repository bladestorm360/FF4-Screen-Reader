using System.Collections.Generic;
using System.Text;
using Il2CppLast.Data.User;
using UnityEngine.UI;

namespace FFIV_ScreenReader.Menus
{
    /// <summary>
    /// Handles reading character status details from the status menu.
    /// Reads character stats, battle commands, and other status information in a logical order.
    /// </summary>
    public static class StatusDetailsReader
    {
        // Store the current character data for hotkey access
        private static OwnedCharacterData currentCharacterData = null;

        /// <summary>
        /// Read status details from the status details controller.
        /// Returns character name, HP/MP, and level.
        /// </summary>
        public static string ReadStatusDetails(Il2CppSerial.FF4.UI.KeyInput.StatusDetailsController controller)
        {
            if (controller == null)
            {
                return null;
            }

            try
            {
                var parts = new System.Collections.Generic.List<string>();

                // Try to get current character data from the controller
                var data = currentCharacterData;
                if (data != null)
                {
                    // Character name
                    string name = data.Name;
                    if (!string.IsNullOrEmpty(name))
                    {
                        parts.Add(name);
                    }

                    // HP and MP from parameter
                    var param = data.parameter;
                    if (param != null)
                    {
                        int currentHP = param.CurrentHP;
                        int maxHP = param.ConfirmedMaxHp();
                        int currentMP = param.CurrentMP;
                        int maxMP = param.ConfirmedMaxMp();
                        int level = param.ConfirmedLevel();

                        parts.Add($"Level {level}");
                        parts.Add($"HP {currentHP}/{maxHP}");
                        parts.Add($"MP {currentMP}/{maxMP}");
                    }
                }

                if (parts.Count == 0)
                {
                    return "Status Details";
                }

                return string.Join(", ", parts);
            }
            catch (System.Exception ex)
            {
                MelonLoader.MelonLogger.Warning($"Error reading status details: {ex.Message}");
                return "Status Details";
            }
        }

        /// <summary>
        /// Store character data when status screen is updated.
        /// Called from SetParameter patch.
        /// </summary>
        public static void SetCurrentCharacterData(OwnedCharacterData data)
        {
            currentCharacterData = data;
        }

        /// <summary>
        /// Clear character data when leaving status screen.
        /// </summary>
        public static void ClearCurrentCharacterData()
        {
            currentCharacterData = null;
        }

        /// <summary>
        /// Safely get text from a Text component, returning null if invalid.
        /// </summary>
        private static string GetTextSafe(Text textComponent)
        {
            if (textComponent == null)
            {
                return null;
            }

            try
            {
                string text = textComponent.text;
                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }

                // Trim and return
                return text.Trim();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Read physical combat stats (Strength, Stamina, Defense, Evade).
        /// Called when user presses [ key on status screen.
        /// </summary>
        public static string ReadPhysicalStats()
        {
            if (currentCharacterData == null || currentCharacterData.parameter == null)
            {
                return "No character data available";
            }

            try
            {
                var param = currentCharacterData.parameter;
                var parts = new List<string>();

                // Strength (Power)
                int strength = param.ConfirmedPower();
                parts.Add($"Strength: {strength}");

                // Stamina (Vitality)
                int stamina = param.ConfirmedVitality();
                parts.Add($"Stamina: {stamina}");

                // Defense
                int defense = param.ConfirmedDefense();
                parts.Add($"Defense: {defense}");

                // Evade (Defense Count)
                int evade = param.ConfirmedDefenseCount();
                parts.Add($"Evade: {evade}");

                return string.Join(". ", parts);
            }
            catch (System.Exception ex)
            {
                return $"Error reading physical stats: {ex.Message}";
            }
        }

        /// <summary>
        /// Read magical combat stats (Magic, Spirit, Magic Defense, Magic Evade).
        /// Called when user presses ] key on status screen.
        /// </summary>
        public static string ReadMagicalStats()
        {
            if (currentCharacterData == null || currentCharacterData.parameter == null)
            {
                return "No character data available";
            }

            try
            {
                var param = currentCharacterData.parameter;
                var parts = new List<string>();

                // Magic Power
                int magic = param.ConfirmedMagic();
                parts.Add($"Magic: {magic}");

                // Spirit
                int spirit = param.ConfirmedSpirit();
                parts.Add($"Spirit: {spirit}");

                // Magic Defense
                int magicDefense = param.ConfirmedAbilityDefense();
                parts.Add($"Magic Defense: {magicDefense}");

                // Magic Evade
                int magicEvade = param.ConfirmedAbilityEvasionRate();
                parts.Add($"Magic Evade: {magicEvade}");

                return string.Join(". ", parts);
            }
            catch (System.Exception ex)
            {
                return $"Error reading magical stats: {ex.Message}";
            }
        }
    }
}
