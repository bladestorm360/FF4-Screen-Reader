using System;
using System.Collections.Generic;
using System.Text;
using Il2CppLast.Data.User;
using Il2CppLast.Defaine;
using Il2CppLast.UI.KeyInput;
using Il2CppSerial.FF4.UI.KeyInput;
using MelonLoader;
using UnityEngine;
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
    /// FF4-specific implementation with 17 stats matching visual display.
    /// </summary>
    public static class StatusNavigationReader
    {
        private static List<StatusStatDefinition> statList = null;
        // Group start indices: CharacterInfo=0, Vitals=4, Attributes=6, CombatStats=11
        private static readonly int[] GroupStartIndices = new int[] { 0, 4, 6, 11 };

        // ParameterType enum values for UI reading
        private const int PARAMETER_TYPE_ATTACK = 10;
        private const int PARAMETER_TYPE_DEFENSE = 11;
        private const int PARAMETER_TYPE_ABILITY_DEFENSE = 12;

        // Memory offsets for UI reading
        private const int OFFSET_PARAM_TYPE = 0x18;
        private const int OFFSET_PARAM_VIEW = 0x20;
        private const int OFFSET_MULTIPLIED_VALUE_TEXT = 0x28;
        private const int OFFSET_DOMINANT_ARM_TEXT = 0x98;

        /// <summary>
        /// Try to read a count value from the UI for a specific ParameterType.
        /// Returns -1 if unable to read from UI.
        /// </summary>
        private static int TryReadCountFromUI(int parameterType)
        {
            try
            {
                // Find all ParameterContentController objects in scene
                var controllers = UnityEngine.Object.FindObjectsOfType<Il2CppLast.UI.KeyInput.ParameterContentController>();
                if (controllers == null || controllers.Length == 0)
                    return -1;

                foreach (var controller in controllers)
                {
                    if (controller == null) continue;

                    // Check if this is the parameter we're looking for
                    IntPtr controllerPtr = controller.Pointer;
                    if (controllerPtr == IntPtr.Zero) continue;

                    int paramType;
                    unsafe
                    {
                        paramType = *(int*)((byte*)controllerPtr + OFFSET_PARAM_TYPE);
                    }

                    if (paramType != parameterType)
                        continue;

                    // Found the parameter - read the view at offset 0x20
                    IntPtr viewPtr;
                    unsafe
                    {
                        viewPtr = *(IntPtr*)((byte*)controllerPtr + OFFSET_PARAM_VIEW);
                    }
                    if (viewPtr == IntPtr.Zero)
                        return -1;

                    // Read multipliedValueText at offset 0x28
                    IntPtr textPtr;
                    unsafe
                    {
                        textPtr = *(IntPtr*)((byte*)viewPtr + OFFSET_MULTIPLIED_VALUE_TEXT);
                    }
                    if (textPtr == IntPtr.Zero)
                        return -1;

                    // Cast to Text component and read value
                    var textComponent = new UnityEngine.UI.Text(textPtr);
                    if (textComponent == null)
                        return -1;

                    string textValue = textComponent.text;
                    if (string.IsNullOrEmpty(textValue))
                        return -1;

                    // Parse the count from the text
                    if (int.TryParse(textValue.Trim(), out int count))
                    {
                        return count;
                    }

                    return -1;
                }

                return -1;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading count from UI for type {parameterType}: {ex.Message}");
                return -1;
            }
        }

        /// <summary>
        /// Try to read handedness text from the AbilityCharaStatusView UI.
        /// Returns null if unable to read.
        /// </summary>
        private static string TryReadHandednessFromUI()
        {
            try
            {
                // Find AbilityCharaStatusView in scene
                var views = UnityEngine.Object.FindObjectsOfType<AbilityCharaStatusView>();
                if (views == null || views.Length == 0)
                    return null;

                foreach (var view in views)
                {
                    if (view == null || view.gameObject == null || !view.gameObject.activeInHierarchy)
                        continue;

                    // Try direct property access first
                    try
                    {
                        var dominantText = view.DominantArmText;
                        if (dominantText != null && !string.IsNullOrWhiteSpace(dominantText.text))
                        {
                            return dominantText.text.Trim();
                        }
                    }
                    catch
                    {
                        // Fall back to pointer reading
                    }

                    // Pointer-based reading as fallback
                    IntPtr viewPtr = view.Pointer;
                    if (viewPtr == IntPtr.Zero)
                        continue;

                    IntPtr textPtr;
                    unsafe
                    {
                        textPtr = *(IntPtr*)((byte*)viewPtr + OFFSET_DOMINANT_ARM_TEXT);
                    }
                    if (textPtr == IntPtr.Zero)
                        continue;

                    var textComponent = new UnityEngine.UI.Text(textPtr);
                    if (textComponent != null && !string.IsNullOrWhiteSpace(textComponent.text))
                    {
                        return textComponent.text.Trim();
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading handedness from UI: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Initialize the stat list with all 17 visible stats in UI order.
        /// Matches the visual layout of FF4's status screen.
        /// </summary>
        public static void InitializeStatList()
        {
            if (statList != null) return;

            statList = new List<StatusStatDefinition>();

            // Character Info Group (indices 0-3)
            statList.Add(new StatusStatDefinition("Level", StatGroup.CharacterInfo, ReadLevel));
            statList.Add(new StatusStatDefinition("Handed", StatGroup.CharacterInfo, ReadHanded));
            statList.Add(new StatusStatDefinition("Experience", StatGroup.CharacterInfo, ReadExperience));
            statList.Add(new StatusStatDefinition("Next Level", StatGroup.CharacterInfo, ReadNextLevel));

            // Vitals Group (indices 4-5)
            statList.Add(new StatusStatDefinition("HP", StatGroup.Vitals, ReadHP));
            statList.Add(new StatusStatDefinition("MP", StatGroup.Vitals, ReadMP));

            // Attributes Group (indices 6-10)
            statList.Add(new StatusStatDefinition("Strength", StatGroup.Attributes, ReadStrength));
            statList.Add(new StatusStatDefinition("Agility", StatGroup.Attributes, ReadAgility));
            statList.Add(new StatusStatDefinition("Stamina", StatGroup.Attributes, ReadStamina));
            statList.Add(new StatusStatDefinition("Intellect", StatGroup.Attributes, ReadIntellect));
            statList.Add(new StatusStatDefinition("Spirit", StatGroup.Attributes, ReadSpirit));

            // Combat Stats Group (indices 11-16)
            statList.Add(new StatusStatDefinition("Attack", StatGroup.CombatStats, ReadAttack));
            statList.Add(new StatusStatDefinition("Accuracy", StatGroup.CombatStats, ReadAccuracy));
            statList.Add(new StatusStatDefinition("Defense", StatGroup.CombatStats, ReadDefense));
            statList.Add(new StatusStatDefinition("Evasion", StatGroup.CombatStats, ReadEvasion));
            statList.Add(new StatusStatDefinition("Magic Defense", StatGroup.CombatStats, ReadMagicDefense));
            statList.Add(new StatusStatDefinition("Magic Evasion", StatGroup.CombatStats, ReadMagicEvasion));
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

        private static string ReadHanded(OwnedCharacterData data)
        {
            try
            {
                // Try to read from UI first (most accurate - shows localized text)
                string uiText = TryReadHandednessFromUI();
                if (!string.IsNullOrWhiteSpace(uiText))
                {
                    return uiText;
                }

                // Fallback to data API
                if (data == null) return "N/A";
                var dominantType = data.GetEquipDominantArmType();
                return dominantType switch
                {
                    Il2CppLast.Defaine.EquipDominantType.RightHanded => "Right-handed",
                    Il2CppLast.Defaine.EquipDominantType.LeftHanded => "Left-handed",
                    Il2CppLast.Defaine.EquipDominantType.Ambidextrous => "Ambidextrous",
                    _ => "N/A"
                };
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading handedness: {ex.Message}");
                return "N/A";
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

        private static string ReadIntellect(OwnedCharacterData data)
        {
            try
            {
                if (data?.parameter == null) return "Intellect: N/A";
                return $"Intellect: {data.parameter.ConfirmedIntelligence()}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Intellect: {ex.Message}");
                return "Intellect: N/A";
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

                int attackPower = data.parameter.ConfirmedAttack();

                // Try to read attack count from UI
                int attackCount = TryReadCountFromUI(PARAMETER_TYPE_ATTACK);

                if (attackCount > 0)
                {
                    return $"Attack: {attackCount}x {attackPower}";
                }

                // Fallback to power only if count unavailable
                return $"Attack: {attackPower}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Attack: {ex.Message}");
                return "Attack: N/A";
            }
        }

        private static string ReadAccuracy(OwnedCharacterData data)
        {
            try
            {
                if (data?.parameter == null) return "Accuracy: N/A";
                int accuracy = data.parameter.ConfirmedAccuracyRate(false);
                return $"Accuracy: {accuracy}%";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Accuracy: {ex.Message}");
                return "Accuracy: N/A";
            }
        }

        private static string ReadDefense(OwnedCharacterData data)
        {
            try
            {
                if (data?.parameter == null) return "Defense: N/A";

                int defensePower = data.parameter.ConfirmedDefense();

                // Try to read defense count from UI
                int defenseCount = TryReadCountFromUI(PARAMETER_TYPE_DEFENSE);

                if (defenseCount > 0)
                {
                    return $"Defense: {defenseCount}x {defensePower}";
                }

                // Fallback to power only if count unavailable
                return $"Defense: {defensePower}";
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
                int evasion = data.parameter.ConfirmedEvasionRate(false);
                return $"Evasion: {evasion}%";
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

                int magicDefensePower = data.parameter.ConfirmedAbilityDefense();

                // Try to read magic defense count from UI
                int magicDefenseCount = TryReadCountFromUI(PARAMETER_TYPE_ABILITY_DEFENSE);

                if (magicDefenseCount > 0)
                {
                    return $"Magic Defense: {magicDefenseCount}x {magicDefensePower}";
                }

                // Fallback to power only if count unavailable
                return $"Magic Defense: {magicDefensePower}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Magic Defense: {ex.Message}");
                return "Magic Defense: N/A";
            }
        }

        private static string ReadMagicEvasion(OwnedCharacterData data)
        {
            try
            {
                if (data?.parameter == null) return "Magic Evasion: N/A";
                int magicEvasion = data.parameter.ConfirmedAbilityEvasionRate(false);
                return $"Magic Evasion: {magicEvasion}%";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Magic Evasion: {ex.Message}");
                return "Magic Evasion: N/A";
            }
        }
    }
}
