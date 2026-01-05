using System;
using System.Collections.Generic;
using System.Text;
using Il2CppLast.Data.User;
using MelonLoader;
using UnityEngine.UI;
using FFIV_ScreenReader.Core;
using FFIV_ScreenReader.Patches;

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

    /// <summary>
    /// Stat groups for organizing status screen statistics.
    /// FF4-specific: No Job system, so excludes Job-related groups.
    /// </summary>
    public enum StatGroup
    {
        CharacterInfo,  // Level, Experience, Next Level
        Vitals,         // HP, MP
        Attributes,     // Strength, Agility, Stamina, Magic, Spirit
        CombatStats     // Attack, Defense, Evasion, Magic Defense, Magic Evade
    }

    /// <summary>
    /// Definition of a single stat that can be navigated.
    /// </summary>
    public class StatusStatDefinition
    {
        public string Name { get; set; }
        public StatGroup Group { get; set; }
        public Func<OwnedCharacterData, string> Reader { get; set; }

        public StatusStatDefinition(string name, StatGroup group, Func<OwnedCharacterData, string> reader)
        {
            Name = name;
            Group = group;
            Reader = reader;
        }
    }

    /// <summary>
    /// Handles navigation through status screen stats using arrow keys.
    /// FF4-specific implementation with 13 stats (no Job system).
    /// </summary>
    public static class StatusNavigationReader
    {
        private static List<StatusStatDefinition> statList = null;
        // Group start indices: CharacterInfo=0, Vitals=3, Attributes=5, CombatStats=10
        private static readonly int[] GroupStartIndices = new int[] { 0, 3, 5, 10 };

        /// <summary>
        /// Initialize the stat list with all 13 visible stats in UI order.
        /// </summary>
        public static void InitializeStatList()
        {
            if (statList != null) return;

            statList = new List<StatusStatDefinition>();

            // Character Info Group (indices 0-2)
            statList.Add(new StatusStatDefinition("Level", StatGroup.CharacterInfo, ReadLevel));
            statList.Add(new StatusStatDefinition("Experience", StatGroup.CharacterInfo, ReadExperience));
            statList.Add(new StatusStatDefinition("Next Level", StatGroup.CharacterInfo, ReadNextLevel));

            // Vitals Group (indices 3-4)
            statList.Add(new StatusStatDefinition("HP", StatGroup.Vitals, ReadHP));
            statList.Add(new StatusStatDefinition("MP", StatGroup.Vitals, ReadMP));

            // Attributes Group (indices 5-9)
            statList.Add(new StatusStatDefinition("Strength", StatGroup.Attributes, ReadStrength));
            statList.Add(new StatusStatDefinition("Agility", StatGroup.Attributes, ReadAgility));
            statList.Add(new StatusStatDefinition("Stamina", StatGroup.Attributes, ReadStamina));
            statList.Add(new StatusStatDefinition("Magic", StatGroup.Attributes, ReadMagic));
            statList.Add(new StatusStatDefinition("Spirit", StatGroup.Attributes, ReadSpirit));

            // Combat Stats Group (indices 10-14)
            statList.Add(new StatusStatDefinition("Attack", StatGroup.CombatStats, ReadAttack));
            statList.Add(new StatusStatDefinition("Defense", StatGroup.CombatStats, ReadDefense));
            statList.Add(new StatusStatDefinition("Evasion", StatGroup.CombatStats, ReadEvasion));
            statList.Add(new StatusStatDefinition("Magic Defense", StatGroup.CombatStats, ReadMagicDefense));
            statList.Add(new StatusStatDefinition("Magic Evade", StatGroup.CombatStats, ReadMagicEvade));
        }

        /// <summary>
        /// Navigate to the next stat (wraps to top at end).
        /// </summary>
        public static void NavigateNext()
        {
            if (statList == null) InitializeStatList();

            var tracker = StatusNavigationTracker.Instance;
            if (!tracker.IsNavigationActive) return;

            tracker.CurrentStatIndex = (tracker.CurrentStatIndex + 1) % statList.Count;
            ReadCurrentStat();
        }

        /// <summary>
        /// Navigate to the previous stat (wraps to bottom at top).
        /// </summary>
        public static void NavigatePrevious()
        {
            if (statList == null) InitializeStatList();

            var tracker = StatusNavigationTracker.Instance;
            if (!tracker.IsNavigationActive) return;

            tracker.CurrentStatIndex--;
            if (tracker.CurrentStatIndex < 0)
            {
                tracker.CurrentStatIndex = statList.Count - 1;
            }
            ReadCurrentStat();
        }

        /// <summary>
        /// Jump to the first stat of the next group.
        /// </summary>
        public static void JumpToNextGroup()
        {
            if (statList == null) InitializeStatList();

            var tracker = StatusNavigationTracker.Instance;
            if (!tracker.IsNavigationActive) return;

            int currentIndex = tracker.CurrentStatIndex;
            int nextGroupIndex = -1;

            // Find next group start index
            for (int i = 0; i < GroupStartIndices.Length; i++)
            {
                if (GroupStartIndices[i] > currentIndex)
                {
                    nextGroupIndex = GroupStartIndices[i];
                    break;
                }
            }

            // Wrap to first group if at end
            if (nextGroupIndex == -1)
            {
                nextGroupIndex = GroupStartIndices[0];
            }

            tracker.CurrentStatIndex = nextGroupIndex;
            ReadCurrentStat();
        }

        /// <summary>
        /// Jump to the first stat of the previous group.
        /// </summary>
        public static void JumpToPreviousGroup()
        {
            if (statList == null) InitializeStatList();

            var tracker = StatusNavigationTracker.Instance;
            if (!tracker.IsNavigationActive) return;

            int currentIndex = tracker.CurrentStatIndex;
            int prevGroupIndex = -1;

            // Find previous group start index
            for (int i = GroupStartIndices.Length - 1; i >= 0; i--)
            {
                if (GroupStartIndices[i] < currentIndex)
                {
                    prevGroupIndex = GroupStartIndices[i];
                    break;
                }
            }

            // Wrap to last group if at beginning
            if (prevGroupIndex == -1)
            {
                prevGroupIndex = GroupStartIndices[GroupStartIndices.Length - 1];
            }

            tracker.CurrentStatIndex = prevGroupIndex;
            ReadCurrentStat();
        }

        /// <summary>
        /// Jump to the top (first stat).
        /// </summary>
        public static void JumpToTop()
        {
            var tracker = StatusNavigationTracker.Instance;
            if (!tracker.IsNavigationActive) return;

            tracker.CurrentStatIndex = 0;
            ReadCurrentStat();
        }

        /// <summary>
        /// Jump to the bottom (last stat).
        /// </summary>
        public static void JumpToBottom()
        {
            if (statList == null) InitializeStatList();

            var tracker = StatusNavigationTracker.Instance;
            if (!tracker.IsNavigationActive) return;

            tracker.CurrentStatIndex = statList.Count - 1;
            ReadCurrentStat();
        }

        /// <summary>
        /// Read the stat at the current index.
        /// </summary>
        public static void ReadCurrentStat()
        {
            var tracker = StatusNavigationTracker.Instance;
            if (!tracker.ValidateState())
            {
                FFIV_ScreenReaderMod.SpeakText("Navigation not available");
                return;
            }

            ReadStatAtIndex(tracker.CurrentStatIndex);
        }

        /// <summary>
        /// Read the stat at the specified index.
        /// </summary>
        private static void ReadStatAtIndex(int index)
        {
            if (statList == null) InitializeStatList();

            var tracker = StatusNavigationTracker.Instance;

            if (index < 0 || index >= statList.Count)
            {
                MelonLogger.Warning($"Invalid stat index: {index}");
                return;
            }

            if (tracker.CurrentCharacterData == null)
            {
                FFIV_ScreenReaderMod.SpeakText("No character data");
                return;
            }

            try
            {
                var stat = statList[index];
                string value = stat.Reader(tracker.CurrentCharacterData);
                FFIV_ScreenReaderMod.SpeakText(value, true);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error reading stat at index {index}: {ex.Message}");
                FFIV_ScreenReaderMod.SpeakText("Error reading stat");
            }
        }

        // Character Info readers
        private static string ReadLevel(OwnedCharacterData data)
        {
            try
            {
                if (data?.parameter == null) return "Level: N/A";
                return $"Level: {data.parameter.ConfirmedLevel()}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading level: {ex.Message}");
                return "Level: N/A";
            }
        }

        private static string ReadExperience(OwnedCharacterData data)
        {
            try
            {
                if (data == null) return "Experience: N/A";
                int currentExp = data.CurrentExp;
                return $"Experience: {currentExp}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading experience: {ex.Message}");
                return "Experience: N/A";
            }
        }

        private static string ReadNextLevel(OwnedCharacterData data)
        {
            try
            {
                if (data == null) return "Next Level: N/A";
                int nextExp = data.GetNextExp();
                return $"Next Level: {nextExp}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading next level: {ex.Message}");
                return "Next Level: N/A";
            }
        }

        // Vitals readers
        private static string ReadHP(OwnedCharacterData data)
        {
            try
            {
                if (data?.parameter == null) return "HP: N/A";
                int current = data.parameter.CurrentHP;
                int max = data.parameter.ConfirmedMaxHp();
                return $"HP: {current} / {max}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading HP: {ex.Message}");
                return "HP: N/A";
            }
        }

        private static string ReadMP(OwnedCharacterData data)
        {
            try
            {
                if (data?.parameter == null) return "MP: N/A";
                int current = data.parameter.CurrentMP;
                int max = data.parameter.ConfirmedMaxMp();
                return $"MP: {current} / {max}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading MP: {ex.Message}");
                return "MP: N/A";
            }
        }

        // Attributes readers
        private static string ReadStrength(OwnedCharacterData data)
        {
            try
            {
                if (data?.parameter == null) return "Strength: N/A";
                return $"Strength: {data.parameter.ConfirmedPower()}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Strength: {ex.Message}");
                return "Strength: N/A";
            }
        }

        private static string ReadAgility(OwnedCharacterData data)
        {
            try
            {
                if (data?.parameter == null) return "Agility: N/A";
                return $"Agility: {data.parameter.ConfirmedAgility()}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Agility: {ex.Message}");
                return "Agility: N/A";
            }
        }

        private static string ReadStamina(OwnedCharacterData data)
        {
            try
            {
                if (data?.parameter == null) return "Stamina: N/A";
                return $"Stamina: {data.parameter.ConfirmedVitality()}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Stamina: {ex.Message}");
                return "Stamina: N/A";
            }
        }

        private static string ReadMagic(OwnedCharacterData data)
        {
            try
            {
                if (data?.parameter == null) return "Magic: N/A";
                return $"Magic: {data.parameter.ConfirmedMagic()}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Magic: {ex.Message}");
                return "Magic: N/A";
            }
        }

        private static string ReadSpirit(OwnedCharacterData data)
        {
            try
            {
                if (data?.parameter == null) return "Spirit: N/A";
                return $"Spirit: {data.parameter.ConfirmedSpirit()}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Spirit: {ex.Message}");
                return "Spirit: N/A";
            }
        }

        // Combat Stats readers
        private static string ReadAttack(OwnedCharacterData data)
        {
            try
            {
                if (data?.parameter == null) return "Attack: N/A";
                return $"Attack: {data.parameter.ConfirmedAttack()}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Attack: {ex.Message}");
                return "Attack: N/A";
            }
        }

        private static string ReadDefense(OwnedCharacterData data)
        {
            try
            {
                if (data?.parameter == null) return "Defense: N/A";
                return $"Defense: {data.parameter.ConfirmedDefense()}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Defense: {ex.Message}");
                return "Defense: N/A";
            }
        }

        private static string ReadEvasion(OwnedCharacterData data)
        {
            try
            {
                if (data?.parameter == null) return "Evasion: N/A";
                return $"Evasion: {data.parameter.ConfirmedDefenseCount()}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Evasion: {ex.Message}");
                return "Evasion: N/A";
            }
        }

        private static string ReadMagicDefense(OwnedCharacterData data)
        {
            try
            {
                if (data?.parameter == null) return "Magic Defense: N/A";
                return $"Magic Defense: {data.parameter.ConfirmedAbilityDefense()}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Magic Defense: {ex.Message}");
                return "Magic Defense: N/A";
            }
        }

        private static string ReadMagicEvade(OwnedCharacterData data)
        {
            try
            {
                if (data?.parameter == null) return "Magic Evade: N/A";
                return $"Magic Evade: {data.parameter.ConfirmedAbilityEvasionRate()}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Magic Evade: {ex.Message}");
                return "Magic Evade: N/A";
            }
        }
    }
}
