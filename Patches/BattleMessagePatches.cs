using System;
using HarmonyLib;
using MelonLoader;
using Il2CppLast.Message;
using Il2CppLast.Management;
using Il2CppLast.UI;
using Il2CppLast.UI.Touch;
using Il2CppLast.UI.KeyInput;
using Il2CppLast.UI.Message;
using Il2CppLast.Battle;
using Il2CppLast.Battle.Function;
using Il2CppLast.Data.Master;
using FFIV_ScreenReader.Core;
using UnityEngine;

namespace FFIV_ScreenReader.Patches
{
    /// <summary>
    /// Global tracker for battle message deduplication.
    /// Prevents the same action from being announced multiple times in quick succession.
    /// </summary>
    public static class GlobalBattleMessageTracker
    {
        private static string lastMessage = "";
        private static float lastMessageTime = 0f;
        private const float DEDUP_WINDOW_SECONDS = 1.5f;

        public static bool ShouldAnnounce(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            float currentTime = UnityEngine.Time.time;

            // Allow if message is different or enough time has passed
            if (message != lastMessage || (currentTime - lastMessageTime) >= DEDUP_WINDOW_SECONDS)
            {
                lastMessage = message;
                lastMessageTime = currentTime;
                return true;
            }

            return false;
        }

        public static void Reset()
        {
            lastMessage = "";
            lastMessageTime = 0f;
        }
    }

    /// <summary>
    /// Patches for battle-specific message display methods.
    /// Note: MessageWindowView.SetSpeker and SetMessage are in MessagePatches.cs
    /// </summary>

    /// <summary>
    /// Patch ParameterActFunctionManagment.CreateActFunction to announce battle actions.
    /// This is called when any unit (player or enemy) performs an action.
    /// Announces: "Cecil attacks", "Goblin uses Goblin Punch", etc.
    /// </summary>
    [HarmonyPatch(typeof(ParameterActFunctionManagment), nameof(ParameterActFunctionManagment.CreateActFunction))]
    public static class ParameterActFunctionManagment_CreateActFunction_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BattleActData battleActData)
        {
            try
            {
                if (battleActData == null)
                    return;

                // Get attacker name
                string attackerName = GetActorName(battleActData.AttackUnitData);
                if (string.IsNullOrWhiteSpace(attackerName))
                    return;

                // Get action name
                string actionName = GetActionName(battleActData);
                if (string.IsNullOrWhiteSpace(actionName))
                    return;

                // Format message naturally
                string message;
                if (actionName.Equals("Attack", StringComparison.OrdinalIgnoreCase) ||
                    actionName.Equals("attack", StringComparison.OrdinalIgnoreCase))
                {
                    message = $"{attackerName} attacks";
                }
                else
                {
                    message = $"{attackerName} uses {actionName}";
                }

                // Use global deduplication
                if (GlobalBattleMessageTracker.ShouldAnnounce(message))
                {
                    MelonLogger.Msg($"[Battle Action] {message}");
                    FFIV_ScreenReaderMod.SpeakText(message, interrupt: false);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in CreateActFunction patch: {ex.Message}");
            }
        }

        private static string GetActorName(BattleUnitData unitData)
        {
            if (unitData == null)
                return null;

            // Try player character
            var playerData = unitData.TryCast<Il2Cpp.BattlePlayerData>();
            if (playerData?.ownedCharacterData != null)
            {
                return playerData.ownedCharacterData.Name;
            }

            // Try enemy
            var enemyData = unitData.TryCast<BattleEnemyData>();
            if (enemyData != null)
            {
                try
                {
                    string mesIdName = enemyData.GetMesIdName();
                    var messageManager = MessageManager.Instance;
                    if (messageManager != null && !string.IsNullOrEmpty(mesIdName))
                    {
                        string localizedName = messageManager.GetMessage(mesIdName);
                        if (!string.IsNullOrEmpty(localizedName))
                        {
                            return localizedName;
                        }
                    }
                }
                catch { }
            }

            return null;
        }

        private static string GetActionName(BattleActData actData)
        {
            if (actData == null)
                return null;

            var messageManager = MessageManager.Instance;
            if (messageManager == null)
                return null;

            // Check for item first (items have a direct Name property)
            if (actData.itemList != null && actData.itemList.Count > 0)
            {
                var item = actData.itemList[0];
                if (item != null)
                {
                    try
                    {
                        string name = item.Name;
                        if (!string.IsNullOrEmpty(name))
                            return name;
                    }
                    catch { }
                }
            }

            // Get command name (Attack, Magic, Item, etc.)
            // Note: Ability class doesn't have MesIdName directly,
            // so we rely on the Command to describe the action type
            var command = actData.Command;
            if (command != null)
            {
                try
                {
                    string mesIdName = command.MesIdName;
                    if (!string.IsNullOrEmpty(mesIdName))
                    {
                        string name = messageManager.GetMessage(mesIdName);
                        if (!string.IsNullOrEmpty(name))
                            return name;
                    }
                }
                catch { }
            }

            return null;
        }
    }

    [HarmonyPatch(typeof(ScrollMessageManager), nameof(ScrollMessageManager.Play))]
    public static class ScrollMessageManager_Play_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ScrollMessageClient.ScrollType type, string message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(message))
                {
                    return;
                }

                string cleanMessage = message.Trim();
                MelonLogger.Msg($"[ScrollMessage] {cleanMessage}");
                FFIV_ScreenReaderMod.SpeakText(cleanMessage, interrupt: false);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ScrollMessageManager.Play patch: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(Il2CppLast.Battle.Function.BattleBasicFunction), nameof(Il2CppLast.Battle.Function.BattleBasicFunction.CreateDamageView))]
    public static class BattleBasicFunction_CreateDamageView_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Il2CppLast.Battle.BattleUnitData data, int value, Il2CppLast.Systems.HitType hitType, bool isRecovery)
        {
            try
            {
                string targetName = "Unknown";

                // Check if this is a BattlePlayerData (player character)
                var playerData = data.TryCast<Il2Cpp.BattlePlayerData>();
                if (playerData != null)
                {
                    try
                    {
                        var ownedCharData = playerData.ownedCharacterData;
                        if (ownedCharData != null)
                        {
                            targetName = ownedCharData.Name;
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"Error getting player name: {ex.Message}");
                    }
                }

                // Check if this is a BattleEnemyData (enemy)
                var enemyData = data.TryCast<Il2CppLast.Battle.BattleEnemyData>();
                if (enemyData != null)
                {
                    try
                    {
                        string mesIdName = enemyData.GetMesIdName();
                        var messageManager = Il2CppLast.Management.MessageManager.Instance;
                        if (messageManager != null && !string.IsNullOrEmpty(mesIdName))
                        {
                            string localizedName = messageManager.GetMessage(mesIdName);
                            if (!string.IsNullOrEmpty(localizedName))
                            {
                                targetName = localizedName;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"Error getting enemy name: {ex.Message}");
                    }
                }

                string message;
                if (hitType == Il2CppLast.Systems.HitType.Miss || value == 0)
                {
                    message = $"{targetName}: Miss";
                }
                else if (hitType == Il2CppLast.Systems.HitType.Recovery)
                {
                    message = $"{targetName}: Recovered {value} HP";
                }
                else if (hitType == Il2CppLast.Systems.HitType.MPRecovery)
                {
                    message = $"{targetName}: Recovered {value} MP";
                }
                else
                {
                    message = $"{targetName}: {value} damage";
                }

                MelonLogger.Msg($"[Damage] {message}");
                FFIV_ScreenReaderMod.SpeakText(message, interrupt: false);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in BattleBasicFunction.CreateDamageView patch: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(DamageViewUIManager), nameof(DamageViewUIManager.CreateHitCount))]
    public static class DamageViewUIManager_CreateHitCount_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(int hitCountValue, Il2CppLast.Battle.BattleSpriteEntity attack, Il2CppLast.Battle.BattleSpriteEntity target)
        {
            try
            {
                string message = $"{hitCountValue} hits";
                MelonLogger.Msg($"[Hit Count] {message}");
                FFIV_ScreenReaderMod.SpeakText(message, interrupt: false);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in CreateHitCount patch: {ex.Message}");
            }
        }
    }

    // Patch BattleConditionController.Add to announce status effects with target names
    [HarmonyPatch(typeof(Il2CppLast.Battle.BattleConditionController), nameof(Il2CppLast.Battle.BattleConditionController.Add))]
    public static class BattleConditionController_Add_Patch
    {
        private static string lastAnnouncement = "";

        [HarmonyPostfix]
        public static void Postfix(BattleUnitData battleUnitData, int id)
        {
            try
            {
                if (battleUnitData == null)
                {
                    return;
                }

                // Get target name
                string targetName = "Unknown";
                var playerData = battleUnitData.TryCast<Il2Cpp.BattlePlayerData>();
                if (playerData?.ownedCharacterData != null)
                {
                    targetName = playerData.ownedCharacterData.Name;
                }
                else
                {
                    var enemyData = battleUnitData.TryCast<BattleEnemyData>();
                    if (enemyData != null)
                    {
                        string mesIdName = enemyData.GetMesIdName();
                        var messageManager = MessageManager.Instance;
                        if (messageManager != null && !string.IsNullOrEmpty(mesIdName))
                        {
                            string localizedName = messageManager.GetMessage(mesIdName);
                            if (!string.IsNullOrEmpty(localizedName))
                            {
                                targetName = localizedName;
                            }
                        }
                    }
                }

                // Get condition name from ID - look up from ConfirmedConditionList (includes equipment statuses)
                string conditionName = null;
                try
                {
                    var unitDataInfo = battleUnitData.BattleUnitDataInfo;
                    if (unitDataInfo != null && unitDataInfo.Parameter != null)
                    {
                        var param = unitDataInfo.Parameter;
                        var confirmedList = param.ConfirmedConditionList();
                        if (confirmedList != null && confirmedList.Count > 0)
                        {
                            // Look for a condition matching our ID
                            foreach (var condition in confirmedList)
                            {
                                if (condition != null && condition.Id == id)
                                {
                                    string conditionMesId = condition.MesIdName;

                                    // Skip conditions with no message ID (internal/hidden statuses)
                                    if (string.IsNullOrEmpty(conditionMesId) || conditionMesId == "None")
                                    {
                                        return; // Skip this status announcement entirely
                                    }

                                    var messageManager = MessageManager.Instance;
                                    if (messageManager != null)
                                    {
                                        string localizedConditionName = messageManager.GetMessage(conditionMesId);
                                        if (!string.IsNullOrEmpty(localizedConditionName))
                                        {
                                            conditionName = localizedConditionName;
                                        }
                                    }
                                    break;
                                }
                            }
                        }
                    }

                    // Final fallback: Announce the raw ID if we couldn't resolve the name
                    if (conditionName == null)
                    {
                        conditionName = $"Status {id}";
                        MelonLogger.Warning($"[Status] Could not resolve condition ID {id}, announcing as raw ID");
                    }
                }
                catch (Exception condEx)
                {
                    MelonLogger.Warning($"Error resolving condition ID {id}: {condEx.Message}");
                    conditionName = $"Status {id}";
                }

                string announcement = $"{targetName}: {conditionName}";

                // Skip duplicates
                if (announcement == lastAnnouncement)
                {
                    return;
                }
                lastAnnouncement = announcement;

                MelonLogger.Msg($"[Status] {announcement}");
                FFIV_ScreenReaderMod.SpeakText(announcement, interrupt: false);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in BattleConditionController.Add patch: {ex.Message}");
            }
        }
    }

    // Patch BattleMenuController from KeyInput namespace - command messages like "Cecil uses Fire"
    // Also handles Libra/Scan spell results which call this method repeatedly with the same text
    [HarmonyPatch(typeof(Il2CppLast.UI.KeyInput.BattleMenuController), nameof(Il2CppLast.UI.KeyInput.BattleMenuController.SetCommadnMessage))]
    public static class BattleMenuController_KeyInput_SetCommadnMessage_Patch
    {
        private static string lastMessage = "";
        private static float lastMessageTime = 0f;
        private const float MESSAGE_THROTTLE_SECONDS = 2.5f; // Only announce if message changes or 2.5 seconds has passed

        [HarmonyPostfix]
        public static void Postfix(string message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(message))
                {
                    // If message is cleared, reset tracking
                    if (!string.IsNullOrWhiteSpace(lastMessage))
                    {
                        lastMessage = "";
                        lastMessageTime = 0f;
                    }
                    return;
                }

                // Create managed string from Il2Cpp string to prevent GC issues
                string cleanMessage = message.Trim();

                // Get current time
                float currentTime = UnityEngine.Time.time;

                // Skip if this is the same message within the throttle window
                // This prevents Libra/Scan results from being announced 40+ times
                if (cleanMessage == lastMessage && (currentTime - lastMessageTime) < MESSAGE_THROTTLE_SECONDS)
                {
                    return;
                }

                // This is either a new message or enough time has passed
                lastMessage = cleanMessage;
                lastMessageTime = currentTime;

                MelonLogger.Msg($"[Battle Command] {cleanMessage}");
                FFIV_ScreenReaderMod.SpeakText(cleanMessage, interrupt: false);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in BattleMenuController KeyInput.SetCommadnMessage patch: {ex.Message}");
            }
        }
    }

    // Patch SetCommandSelectTarget to announce whose turn it is
    [HarmonyPatch(typeof(Il2CppLast.UI.KeyInput.BattleMenuController), nameof(Il2CppLast.UI.KeyInput.BattleMenuController.SetCommandSelectTarget))]
    public static class BattleMenuController_SetCommandSelectTarget_Patch
    {
        private static string lastCharacter = "";
        public static Il2Cpp.BattlePlayerData CurrentActiveCharacter = null;

        [HarmonyPostfix]
        public static void Postfix(Il2Cpp.BattlePlayerData targetData)
        {
            try
            {
                // Store the currently active character for health/status readouts
                CurrentActiveCharacter = targetData;

                // CRITICAL: Reset enemy targeting state when a new turn begins
                // This ensures enemy names are announced every time, even if the same enemy
                // was targeted on previous turns
                BattleTargetSelectController_SelectContent_Enemy_Patch.lastAnnouncedIndex = -1;
                BattleTargetSelectController_SelectContent_Player_Patch.lastAnnouncedIndex = -1;
                BattleTargetSelectController_SelectContent_Player_Patch.lastAnnouncement = "";

                if (targetData != null && targetData.ownedCharacterData != null)
                {
                    string characterName = targetData.ownedCharacterData.Name;

                    if (!string.IsNullOrWhiteSpace(characterName))
                    {
                        // Skip duplicate announcements
                        if (characterName == lastCharacter)
                        {
                            return;
                        }
                        lastCharacter = characterName;

                        string message = $"{characterName}'s turn";
                        MelonLogger.Msg($"[Battle Turn] {message}");
                        FFIV_ScreenReaderMod.SpeakText(message, interrupt: false);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in SetCommandSelectTarget patch: {ex.Message}");
            }
        }
    }
    // NOTE: BattleUIManager.SetCommandText does not exist in FF4 - removed

    // Patch BattleTargetSelectController.SelectContent to announce player names during friendly targeting
    [HarmonyPatch(typeof(Il2CppLast.UI.KeyInput.BattleTargetSelectController), nameof(Il2CppLast.UI.KeyInput.BattleTargetSelectController.SelectContent), new Type[] { typeof(Il2CppSystem.Collections.Generic.IEnumerable<Il2Cpp.BattlePlayerData>), typeof(int) })]
    public static class BattleTargetSelectController_SelectContent_Player_Patch
    {
        public static int lastAnnouncedIndex = -1;
        public static string lastAnnouncement = "";

        [HarmonyPostfix]
        public static void Postfix(Il2CppSystem.Collections.Generic.IEnumerable<Il2Cpp.BattlePlayerData> list, int index)
        {
            try
            {
                if (list == null)
                {
                    return;
                }

                // Convert IEnumerable to List to access by index
                var playerList = list.TryCast<Il2CppSystem.Collections.Generic.List<Il2Cpp.BattlePlayerData>>();
                if (playerList == null || playerList.Count == 0)
                {
                    return;
                }

                // Get the player at the specified index
                if (index >= 0 && index < playerList.Count)
                {
                    var selectedPlayer = playerList[index];
                    if (selectedPlayer != null && selectedPlayer.ownedCharacterData != null)
                    {
                        string characterName = selectedPlayer.ownedCharacterData.Name;
                        if (!string.IsNullOrEmpty(characterName))
                        {
                            // Build announcement with HP and MP information
                            string announcement = characterName;

                            // Try to get HP and MP from BattleUnitDataInfo
                            try
                            {
                                var unitDataInfo = selectedPlayer.BattleUnitDataInfo;
                                if (unitDataInfo != null && unitDataInfo.Parameter != null)
                                {
                                    int currentHP = unitDataInfo.Parameter.CurrentHP;
                                    int maxHP = unitDataInfo.Parameter.ConfirmedMaxHp();
                                    int currentMP = unitDataInfo.Parameter.CurrentMP;
                                    int maxMP = unitDataInfo.Parameter.ConfirmedMaxMp();

                                    announcement += $", HP {currentHP}/{maxHP}, MP {currentMP}/{maxMP}";

                                    // Get status conditions
                                    var conditionList = unitDataInfo.Parameter.ConfirmedConditionList();
                                    if (conditionList != null && conditionList.Count > 0)
                                    {
                                        var messageManager = MessageManager.Instance;
                                        if (messageManager != null)
                                        {
                                            var statusNames = new System.Collections.Generic.List<string>();

                                            foreach (var condition in conditionList)
                                            {
                                                if (condition != null)
                                                {
                                                    string conditionMesId = condition.MesIdName;

                                                    // Skip conditions with no message ID (internal/hidden statuses)
                                                    if (!string.IsNullOrEmpty(conditionMesId) && conditionMesId != "None")
                                                    {
                                                        string localizedConditionName = messageManager.GetMessage(conditionMesId);
                                                        if (!string.IsNullOrEmpty(localizedConditionName))
                                                        {
                                                            statusNames.Add(localizedConditionName);
                                                        }
                                                    }
                                                }
                                            }

                                            if (statusNames.Count > 0)
                                            {
                                                announcement += $", {string.Join(", ", statusNames)}";
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                MelonLogger.Warning($"Error reading HP/MP for {characterName}: {ex.Message}");
                                // Continue with just the name if stats can't be read
                            }

                            // Skip duplicate announcements (same index AND same announcement)
                            if (index == lastAnnouncedIndex && announcement == lastAnnouncement)
                            {
                                return;
                            }
                            lastAnnouncedIndex = index;
                            lastAnnouncement = announcement;

                            // Reset enemy targeting tracking when player is selected
                            // This ensures switching between enemy/player targets announces correctly
                            BattleTargetSelectController_SelectContent_Enemy_Patch.lastAnnouncedIndex = -1;

                            MelonLogger.Msg($"[Player Target] {announcement}");
                            FFIV_ScreenReaderMod.SpeakText(announcement);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in BattleTargetSelectController.SelectContent (player) patch: {ex.Message}");
            }
        }
    }

    // Reset tracking state when targeting cursor becomes active
    [HarmonyPatch(typeof(Il2CppLast.UI.KeyInput.BattleTargetSelectController), nameof(Il2CppLast.UI.KeyInput.BattleTargetSelectController.SetActiveCursor))]
    public static class BattleTargetSelectController_SetActiveCursor_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(bool isActive)
        {
            if (isActive)
            {
                // Reset tracking when cursor becomes active so first selection is always announced
                // This provides defense-in-depth along with the reset in SetCommandSelectTarget
                BattleTargetSelectController_SelectContent_Enemy_Patch.lastAnnouncedIndex = -1;
                BattleTargetSelectController_SelectContent_Player_Patch.lastAnnouncedIndex = -1;
                BattleTargetSelectController_SelectContent_Player_Patch.lastAnnouncement = "";
            }
        }
    }

    // Patch BattleTargetSelectController.SelectContent to announce enemy names during targeting
    [HarmonyPatch(typeof(Il2CppLast.UI.KeyInput.BattleTargetSelectController), nameof(Il2CppLast.UI.KeyInput.BattleTargetSelectController.SelectContent), new Type[] { typeof(Il2CppSystem.Collections.Generic.IEnumerable<Il2CppLast.Battle.BattleEnemyData>), typeof(int) })]
    public static class BattleTargetSelectController_SelectContent_Enemy_Patch
    {
        public static int lastAnnouncedIndex = -1;

        [HarmonyPostfix]
        public static void Postfix(Il2CppSystem.Collections.Generic.IEnumerable<Il2CppLast.Battle.BattleEnemyData> list, int index)
        {
            try
            {
                if (list == null)
                {
                    return;
                }

                // Convert IEnumerable to array to access by index
                // Try to cast to List first
                var enemyList = list.TryCast<Il2CppSystem.Collections.Generic.List<Il2CppLast.Battle.BattleEnemyData>>();
                if (enemyList == null || enemyList.Count == 0)
                {
                    return;
                }

                // Skip duplicate announcements based on index only
                // This prevents re-announcing when SelectContent is called multiple times for the same selection
                // but allows re-announcement when navigating back to the same enemy after selecting a different one
                if (index == lastAnnouncedIndex)
                {
                    return;
                }
                lastAnnouncedIndex = index;

                // Reset player targeting tracking when enemy is selected
                // This ensures switching between enemy/player targets announces correctly
                BattleTargetSelectController_SelectContent_Player_Patch.lastAnnouncedIndex = -1;
                BattleTargetSelectController_SelectContent_Player_Patch.lastAnnouncement = "";

                // Get the enemy at the specified index
                if (index >= 0 && index < enemyList.Count)
                {
                    var selectedEnemy = enemyList[index];
                    if (selectedEnemy != null)
                    {
                        try
                        {
                            string mesIdName = selectedEnemy.GetMesIdName();
                            var messageManager = Il2CppLast.Management.MessageManager.Instance;
                            if (messageManager != null && !string.IsNullOrEmpty(mesIdName))
                            {
                                string localizedName = messageManager.GetMessage(mesIdName);
                                if (!string.IsNullOrEmpty(localizedName))
                                {
                                    // Build announcement with HP information
                                    string announcement = localizedName;

                                    // Check if there are multiple enemies with the same name
                                    int sameNameCount = 0;
                                    int positionInGroup = 0;
                                    for (int i = 0; i < enemyList.Count; i++)
                                    {
                                        var enemy = enemyList[i];
                                        if (enemy != null)
                                        {
                                            string enemyMesId = enemy.GetMesIdName();
                                            if (!string.IsNullOrEmpty(enemyMesId))
                                            {
                                                string enemyName = messageManager.GetMessage(enemyMesId);
                                                if (enemyName == localizedName)
                                                {
                                                    sameNameCount++;
                                                    if (i < index)
                                                    {
                                                        positionInGroup++;
                                                    }
                                                }
                                            }
                                        }
                                    }

                                    // Add positional indicator if there are multiple enemies with the same name
                                    if (sameNameCount > 1)
                                    {
                                        // Use letter suffixes: A, B, C, etc.
                                        char letter = (char)('A' + positionInGroup);
                                        announcement += $" {letter}";
                                    }

                                    // Try to get HP from BattleUnitDataInfo
                                    try
                                    {
                                        var unitDataInfo = selectedEnemy.BattleUnitDataInfo;
                                        if (unitDataInfo != null && unitDataInfo.Parameter != null)
                                        {
                                            int currentHP = unitDataInfo.Parameter.CurrentHP;
                                            int maxHP = unitDataInfo.Parameter.ConfirmedMaxHp();

                                            announcement += $", HP {currentHP}/{maxHP}";
                                        }
                                    }
                                    catch (Exception hpEx)
                                    {
                                        MelonLogger.Warning($"Error reading HP for {localizedName}: {hpEx.Message}");
                                        // Continue with just the name if HP can't be read
                                    }

                                    MelonLogger.Msg($"[Enemy Target] {announcement}");
                                    FFIV_ScreenReaderMod.SpeakText(announcement);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            MelonLogger.Warning($"Error getting enemy name: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in BattleTargetSelectController.SelectContent patch: {ex.Message}");
            }
        }
    }
}
