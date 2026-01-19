using System;
using System.Collections.Generic;
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
using Il2CppLast.Systems;
using FFIV_ScreenReader.Core;
using FFIV_ScreenReader.Utils;
using static FFIV_ScreenReader.Utils.TextUtils;
using UnityEngine;

namespace FFIV_ScreenReader.Patches
{
    /// <summary>
    /// Global tracker for battle action deduplication.
    /// Tracks actor+action to distinguish initial actions from results.
    /// Uses simple string equality (no time-based logic per Rule 3).
    /// </summary>
    public static class GlobalBattleMessageTracker
    {
        private static string lastMessage = "";

        // Actor+action tracking for two-part abilities (Pray, Steal, Flee, etc.)
        private static string lastAnnouncedActor = "";
        private static string lastAnnouncedAction = "";

        // Flee-in-progress flag to suppress command menu announcements
        public static bool IsFleeInProgress { get; private set; } = false;

        public static bool ShouldAnnounce(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            // Simple string equality - no time window
            if (message != lastMessage)
            {
                lastMessage = message;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Records that an action was announced for an actor.
        /// Used to prevent CreateActFunction from announcing results as new actions.
        /// </summary>
        public static void RecordAction(string actor, string action)
        {
            lastAnnouncedActor = actor ?? "";
            lastAnnouncedAction = action ?? "";
        }

        /// <summary>
        /// Checks if we already announced an action for this actor.
        /// If so, subsequent CreateActFunction calls are likely result messages.
        /// </summary>
        public static bool HasRecentActionForActor(string actor)
        {
            if (string.IsNullOrWhiteSpace(actor) || string.IsNullOrWhiteSpace(lastAnnouncedActor))
                return false;

            return actor.Equals(lastAnnouncedActor, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Checks if a message matches the last announced action name.
        /// Used by SetCommadnMessage to avoid duplicating CreateActFunction announcements.
        /// </summary>
        public static bool IsRedundantActionMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(lastAnnouncedAction))
                return false;

            // Check if message matches the action name (case-insensitive)
            return message.Trim().Equals(lastAnnouncedAction.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Sets the flee-in-progress flag to suppress command menu announcements.
        /// </summary>
        public static void SetFleeInProgress(bool inProgress)
        {
            IsFleeInProgress = inProgress;
        }

        /// <summary>
        /// Clears the flee flag. Called when battle ends or escape result is announced.
        /// </summary>
        public static void ClearFleeInProgress()
        {
            if (IsFleeInProgress)
            {
                IsFleeInProgress = false;
            }
        }

        /// <summary>
        /// Clears the last actor tracking. Call when a new actor takes an action.
        /// </summary>
        public static void ClearLastActor()
        {
            lastAnnouncedActor = "";
            lastAnnouncedAction = "";
        }

        public static void Reset()
        {
            lastMessage = "";
            lastAnnouncedActor = "";
            lastAnnouncedAction = "";
            IsFleeInProgress = false;
        }
    }

    /// <summary>
    /// Patches for battle-specific message display methods.
    /// Note: MessageWindowView.SetSpeker and SetMessage are in MessagePatches.cs
    /// </summary>

    /// <summary>
    /// Patch ParameterActFunctionManagment.CreateActFunction to announce battle actions.
    /// This is called when any unit (player or enemy) performs an action.
    /// Announces: "Cecil attacks", "Rosa uses Pray", "Cecil flees", etc.
    /// Skips result messages (handled by SetCommadnMessage).
    /// </summary>
    [HarmonyPatch(typeof(ParameterActFunctionManagment), nameof(ParameterActFunctionManagment.CreateActFunction))]
    public static class ParameterActFunctionManagment_CreateActFunction_Patch
    {
        // Known flee/escape command IDs - checked against Command.Id
        private static readonly HashSet<int> FleeCommandIds = new HashSet<int>();
        private static bool fleeCommandIdsInitialized = false;

        [HarmonyPostfix]
        public static void Postfix(BattleActData battleActData)
        {
            try
            {
                if (battleActData?.AttackUnitData == null)
                    return;

                string attackerName = BattleUnitHelper.GetUnitName(battleActData.AttackUnitData);
                if (string.IsNullOrWhiteSpace(attackerName))
                    return;

                // Check if we already announced an action for this actor recently
                // If so, this is likely a result message (e.g., "Prayer unanswered") - skip it
                if (GlobalBattleMessageTracker.HasRecentActionForActor(attackerName))
                {
                    return;
                }

                // Check for Flee command specifically
                bool isFlee = IsFleeCommand(battleActData);

                string actionName = GetActionName(battleActData, isFlee);
                if (string.IsNullOrWhiteSpace(actionName))
                    return; // Skip actions with no name

                string message;
                if (isFlee)
                {
                    // Format flee as "Cecil flees." to match "Cecil attacks."
                    message = $"{attackerName} flees";
                    GlobalBattleMessageTracker.SetFleeInProgress(true);
                }
                else if (actionName.Equals("Attack", StringComparison.OrdinalIgnoreCase))
                {
                    message = $"{attackerName} attacks";
                }
                else
                {
                    message = $"{attackerName} uses {actionName}";
                }

                // Record this action to prevent duplicates from SetCommadnMessage
                // and to detect result messages in subsequent CreateActFunction calls
                GlobalBattleMessageTracker.RecordAction(attackerName, actionName);

                FFIV_ScreenReaderMod.SpeakText(message, interrupt: false);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in CreateActFunction patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if this action is a Flee/Escape command.
        /// </summary>
        private static bool IsFleeCommand(BattleActData actData)
        {
            if (actData?.Command == null)
                return false;

            try
            {
                // Check command MesIdName for escape-related IDs
                string mesIdName = actData.Command.MesIdName;
                if (!string.IsNullOrEmpty(mesIdName))
                {
                    // Common escape command message IDs
                    if (mesIdName.Contains("ESCAPE", StringComparison.OrdinalIgnoreCase) ||
                        mesIdName.Contains("FLEE", StringComparison.OrdinalIgnoreCase) ||
                        mesIdName.Contains("RUN", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                // Also check command name directly
                var messageManager = MessageManager.Instance;
                if (messageManager != null && !string.IsNullOrEmpty(mesIdName))
                {
                    string commandName = messageManager.GetMessage(mesIdName);
                    if (!string.IsNullOrEmpty(commandName))
                    {
                        if (commandName.Equals("Flee", StringComparison.OrdinalIgnoreCase) ||
                            commandName.Equals("Escape", StringComparison.OrdinalIgnoreCase) ||
                            commandName.Equals("Run", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception) { }

            return false;
        }

        private static string GetActionName(BattleActData actData, bool isFlee)
        {
            if (actData == null)
                return null;

            // For flee commands, return "Flee" as the action name
            if (isFlee)
                return "Flee";

            var messageManager = MessageManager.Instance;
            if (messageManager == null)
                return null;

            // Check for item first
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
                    catch (Exception) { }
                }
            }

            // Check for abilities using ContentUtitlity
            if (actData.abilityList != null && actData.abilityList.Count > 0)
            {
                var ability = actData.abilityList[0];
                if (ability != null)
                {
                    try
                    {
                        string abilityName = ContentUtitlity.GetAbilityName(ability);
                        // Strip icon markup tags like <IC_WMGC>, <IC_SMGC> etc.
                        abilityName = StripIconMarkup(abilityName);
                        if (!string.IsNullOrEmpty(abilityName))
                            return abilityName;
                        // Ability with empty name - return null to skip
                        return null;
                    }
                    catch (Exception) { }
                }
            }

            // Fall back to command name
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
                catch (Exception) { }
            }

            return null;
        }
    }

    [HarmonyPatch(typeof(ScrollMessageManager), nameof(ScrollMessageManager.Play))]
    public static class ScrollMessageManager_Play_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Il2CppLast.Management.ScrollMessageClient.ScrollType type, string message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(message))
                {
                    return;
                }

                string cleanMessage = message.Trim();
                FFIV_ScreenReaderMod.SpeakText(cleanMessage, interrupt: false);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ScrollMessageManager.Play patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch ScrollMessageClient.PlayMessageId to catch battle messages by ID.
    /// This catches messages like "Back Attack!", "Preemptive Strike!", "The party escaped!" etc.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.Management.ScrollMessageClient), nameof(Il2CppLast.Management.ScrollMessageClient.PlayMessageId))]
    public static class ScrollMessageClient_PlayMessageId_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Il2CppLast.Management.ScrollMessageClient.ScrollType type, string messageId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(messageId))
                {
                    return;
                }

                // Look up the localized message
                var messageManager = MessageManager.Instance;
                if (messageManager != null)
                {
                    string message = messageManager.GetMessage(messageId);
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        string cleanMessage = message.Trim();
                        // Use global deduplication to avoid double announcements with ScrollMessageManager.Play
                        if (GlobalBattleMessageTracker.ShouldAnnounce(cleanMessage))
                        {
                            FFIV_ScreenReaderMod.SpeakText(cleanMessage, interrupt: false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ScrollMessageClient.PlayMessageId patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch ScrollMessageClient.PlayMessageValue for direct message display.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.Management.ScrollMessageClient), nameof(Il2CppLast.Management.ScrollMessageClient.PlayMessageValue))]
    public static class ScrollMessageClient_PlayMessageValue_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Il2CppLast.Management.ScrollMessageClient.ScrollType type, string messageValue)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(messageValue))
                {
                    return;
                }

                string cleanMessage = messageValue.Trim();
                // Use global deduplication to avoid double announcements with ScrollMessageManager.Play
                if (GlobalBattleMessageTracker.ShouldAnnounce(cleanMessage))
                {
                    FFIV_ScreenReaderMod.SpeakText(cleanMessage, interrupt: false);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ScrollMessageClient.PlayMessageValue patch: {ex.Message}");
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
                string targetName = BattleUnitHelper.GetUnitName(data) ?? "Unknown";

                string message;
                if (hitType == Il2CppLast.Systems.HitType.Miss)
                {
                    message = $"{targetName}: Miss";
                }
                else if (value == 0)
                {
                    // Non-damaging ability (buff/debuff) - don't announce, status effects handle it
                    return;
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
        private const string DEDUP_CONTEXT = "Battle.ConditionAdd";

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
                string targetName = BattleUnitHelper.GetUnitName(battleUnitData) ?? "Unknown";

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
                if (!AnnouncementDeduplicator.ShouldAnnounce(DEDUP_CONTEXT, announcement))
                {
                    return;
                }

                FFIV_ScreenReaderMod.SpeakText(announcement, interrupt: false);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in BattleConditionController.Add patch: {ex.Message}");
            }
        }
    }

    // Patch BattleMenuController from KeyInput namespace - announces result messages
    // Skips action names that were already announced by CreateActFunction
    // Uses simple string equality for deduplication (no time-based logic per Rule 3)
    [HarmonyPatch(typeof(Il2CppLast.UI.KeyInput.BattleMenuController), nameof(Il2CppLast.UI.KeyInput.BattleMenuController.SetCommadnMessage))]
    public static class BattleMenuController_KeyInput_SetCommadnMessage_Patch
    {
        private static string lastMessage = "";

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
                    }
                    return;
                }

                // Create managed string from Il2Cpp string to prevent GC issues
                string cleanMessage = message.Trim();

                // Skip if this message matches the action name just announced by CreateActFunction
                // This prevents duplicate announcements like "Rosa uses Pray" followed by "Pray"
                if (GlobalBattleMessageTracker.IsRedundantActionMessage(cleanMessage))
                {
                    return;
                }

                // Skip duplicate messages (simple string equality)
                if (cleanMessage == lastMessage)
                {
                    return;
                }

                lastMessage = cleanMessage;

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
        private const string DEDUP_CONTEXT = "Battle.Turn";
        public static Il2Cpp.BattlePlayerData CurrentActiveCharacter = null;

        [HarmonyPostfix]
        public static void Postfix(Il2Cpp.BattlePlayerData targetData)
        {
            try
            {
                // Set battle state active (this is called on every turn, so it keeps state fresh)
                BattleState.SetActive();

                // Clear flee-in-progress flag when a player's turn begins
                // If flee succeeded, battle would have ended. If we're here, flee failed.
                GlobalBattleMessageTracker.ClearFleeInProgress();

                // Store the currently active character for health/status readouts
                CurrentActiveCharacter = targetData;

                // CRITICAL: Reset enemy/player targeting state when a new turn begins
                AnnouncementDeduplicator.Reset(
                    BattleTargetSelectController_SelectContent_Enemy_Patch.DEDUP_CONTEXT,
                    BattleTargetSelectController_SelectContent_Player_Patch.DEDUP_CONTEXT);

                if (targetData != null && targetData.ownedCharacterData != null)
                {
                    string characterName = targetData.ownedCharacterData.Name;

                    if (!string.IsNullOrWhiteSpace(characterName))
                    {
                        // Skip duplicate announcements
                        if (!AnnouncementDeduplicator.ShouldAnnounce(DEDUP_CONTEXT, characterName))
                        {
                            return;
                        }

                        string message = $"{characterName}'s turn";
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
        public const string DEDUP_CONTEXT = "Battle.Target.Player";
        public const string DEDUP_CONTEXT_ENEMY = "Battle.Target.Enemy";

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
                            // Build announcement with HP, MP, and status conditions using helper
                            string announcement = characterName;

                            try
                            {
                                var unitDataInfo = selectedPlayer.BattleUnitDataInfo;
                                if (unitDataInfo != null && unitDataInfo.Parameter != null)
                                {
                                    announcement += CharacterStatusHelper.GetFullStatus(unitDataInfo.Parameter);
                                }
                            }
                            catch (Exception ex)
                            {
                                MelonLogger.Warning($"Error reading HP/MP for {characterName}: {ex.Message}");
                            }

                            // Skip duplicate announcements (same index AND same announcement)
                            if (!AnnouncementDeduplicator.ShouldAnnounce(DEDUP_CONTEXT, index, announcement))
                            {
                                return;
                            }

                            // Reset enemy targeting tracking when player is selected
                            AnnouncementDeduplicator.Reset(DEDUP_CONTEXT_ENEMY);

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
                AnnouncementDeduplicator.Reset(
                    BattleTargetSelectController_SelectContent_Player_Patch.DEDUP_CONTEXT,
                    BattleTargetSelectController_SelectContent_Enemy_Patch.DEDUP_CONTEXT);
            }
        }
    }

    // Patch BattleTargetSelectController.SelectContent to announce enemy names during targeting
    [HarmonyPatch(typeof(Il2CppLast.UI.KeyInput.BattleTargetSelectController), nameof(Il2CppLast.UI.KeyInput.BattleTargetSelectController.SelectContent), new Type[] { typeof(Il2CppSystem.Collections.Generic.IEnumerable<Il2CppLast.Battle.BattleEnemyData>), typeof(int) })]
    public static class BattleTargetSelectController_SelectContent_Enemy_Patch
    {
        public const string DEDUP_CONTEXT = "Battle.Target.Enemy";

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
                if (!AnnouncementDeduplicator.ShouldAnnounce(DEDUP_CONTEXT, index))
                {
                    return;
                }

                // Reset player targeting tracking when enemy is selected
                AnnouncementDeduplicator.Reset(BattleTargetSelectController_SelectContent_Player_Patch.DEDUP_CONTEXT);

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
                                    catch
                                    {
                                        // Continue with just the name if HP can't be read
                                    }

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
