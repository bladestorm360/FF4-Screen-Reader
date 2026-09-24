using System;
using System.Collections.Generic;
using System.Reflection;
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
using static FFIV_ScreenReader.Utils.ModTextTranslator;
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

        // A plain attack is classified by the command's identity, never its localized name
        // (matching the English word "Attack" only ever worked in English). FF4 master data
        // (command.csv / ability.csv, 2026-09-23): command 1 = Fight (MSG_SYSTEM_085, ability_id 1);
        // ability 1 = the Fight command's own ability (type 4). Abilities 209 and 442 are two more
        // type-4 attacks whose display name is that same Fight message, so they read as attacks too.
        private const int FIGHT_COMMAND_ID = 1;
        private const int PLAIN_ATTACK_ABILITY_ID = 1;

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

                // The announced turn-holder's input turn is over once their action executes, so
                // their next "X's turn" must announce even if nobody else acted in between.
                if (battleActData.AttackUnitData.TryCast<Il2Cpp.BattlePlayerData>() != null &&
                    attackerName == AnnouncementDeduplicator.GetLastString(AnnouncementContexts.BATTLE_TURN))
                {
                    AnnouncementDeduplicator.Reset(AnnouncementContexts.BATTLE_TURN);
                }

                // Use object-based deduplication so different enemies with the same name
                // attacking in succession are both announced (each BattleActData is unique)
                if (!AnnouncementDeduplicator.ShouldAnnounce(AnnouncementContexts.BATTLE_ACTION, battleActData))
                {
                    return;
                }

                // Check for Flee command specifically
                bool isFlee = IsFleeCommand(battleActData);
                bool isPlainAttack = !isFlee && IsPlainAttack(battleActData);

                string actionName = GetActionName(battleActData, isFlee);
                if (!isPlainAttack && string.IsNullOrWhiteSpace(actionName))
                    return; // Skip actions with no name

                string message;
                if (isFlee)
                {
                    // Format flee as "Cecil flees." to match "Cecil attacks."
                    message = string.Format(T("{0} flees"), attackerName);
                    GlobalBattleMessageTracker.SetFleeInProgress(true);
                }
                else if (isPlainAttack)
                {
                    message = string.Format(T("{0} attacks"), attackerName);
                }
                else
                {
                    message = string.Format(T("{0} uses {1}"), attackerName, actionName);
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
        /// True for a plain weapon attack: the Fight command's own ability (id 1), an ability the game
        /// names with the Fight command's own (localized) name, or the Fight command with no ability.
        /// Items and named abilities keep their names ("uses X").
        /// </summary>
        private static bool IsPlainAttack(BattleActData actData)
        {
            try
            {
                if (actData.itemList != null && actData.itemList.Count > 0)
                    return false;

                var abilityList = actData.abilityList;
                if (abilityList != null && abilityList.Count > 0 && abilityList[0] != null)
                {
                    var ability = abilityList[0];
                    if (ability.Id == PLAIN_ATTACK_ABILITY_ID)
                        return true;

                    string fightName = GetFightCommandName();
                    if (string.IsNullOrEmpty(fightName))
                        return false;
                    string abilityName = StripIconMarkup(ContentUtitlity.GetAbilityName(ability));
                    return !string.IsNullOrEmpty(abilityName)
                        && string.Equals(abilityName.Trim(), fightName, StringComparison.Ordinal);
                }

                var command = actData.Command;
                return command != null && command.Id == FIGHT_COMMAND_ID;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>The Fight command's name in the current game language (master command 1).</summary>
        private static string GetFightCommandName()
        {
            try
            {
                var commands = Il2CppLast.Data.Master.MasterManager.Instance?.GetList<Il2CppLast.Data.Master.Command>();
                if (commands == null || !commands.ContainsKey(FIGHT_COMMAND_ID))
                    return null;
                string mesId = commands[FIGHT_COMMAND_ID]?.MesIdName;
                if (string.IsNullOrEmpty(mesId))
                    return null;
                string name = MessageManager.Instance?.GetMessage(mesId);
                return string.IsNullOrWhiteSpace(name) ? null : StripIconMarkup(name).Trim();
            }
            catch
            {
                return null;
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
                string message = MessageHelper.GetLocalizedMessage(messageId);
                if (message != null)
                {
                    string cleanMessage = message.Trim();
                    // Use global deduplication to avoid double announcements with ScrollMessageManager.Play
                    if (GlobalBattleMessageTracker.ShouldAnnounce(cleanMessage))
                    {
                        FFIV_ScreenReaderMod.SpeakText(cleanMessage, interrupt: false);
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

    /// <summary>
    /// Damage / recovery / miss for each target. Damage is always the total: FF4's calc results carry
    /// hit count 0 (CalcControllerProvider.GetFightStatus, RVA 0x429080, drops PhysicalExecution's
    /// landed count) and the game's own ×N display never runs in ATB battles, so there is no
    /// per-hit breakdown to read (the Multi-hit Damage setting was removed 2026-09-24).
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.Battle.Function.BattleBasicFunction), nameof(Il2CppLast.Battle.Function.BattleBasicFunction.CreateDamageView))]
    public static class BattleBasicFunction_CreateDamageView_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Il2CppLast.Battle.Function.BattleBasicFunction __instance, Il2CppLast.Battle.BattleUnitData data, int value, Il2CppLast.Systems.HitType hitType, bool isRecovery)
        {
            try
            {
                string targetName = BattleUnitHelper.GetUnitName(data) ?? T("Unknown");

                string message;
                if (hitType == Il2CppLast.Systems.HitType.Miss)
                {
                    message = string.Format(T("{0}: Miss"), targetName);
                }
                else if (value == 0)
                {
                    // Value-0 views (offline analysis, debug.md "Open-issues pass (2026-09-23, session 2)"):
                    // buffs/debuffs carry Hit and status cures carry Non; both stay silent here because
                    // the condition hooks announce them (BattleConditionController.Add for the status,
                    // RemoveFunction for "X: Poison removed"). Zero is written only for a genuine 0
                    // result (a recovery reversed on an undead target that comes to 0,
                    // MagicAbsorptionFunction).
                    if (hitType != Il2CppLast.Systems.HitType.Zero)
                        return;
                    message = string.Format(T("{0}: {1} damage"), targetName, 0);
                }
                else if (hitType == Il2CppLast.Systems.HitType.Recovery)
                {
                    message = string.Format(T("{0}: Recovered {1} HP"), targetName, value);
                }
                else if (hitType == Il2CppLast.Systems.HitType.MPRecovery)
                {
                    message = string.Format(T("{0}: Recovered {1} MP"), targetName, value);
                }
                else
                {
                    message = string.Format(T("{0}: {1} damage"), targetName, value);
                }

                FFIV_ScreenReaderMod.SpeakText(message, interrupt: false);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in BattleBasicFunction.CreateDamageView patch: {ex.Message}");
            }
        }
    }

    // Patch BattleConditionController.Add to announce status effects with target names
    [HarmonyPatch(typeof(Il2CppLast.Battle.BattleConditionController), nameof(Il2CppLast.Battle.BattleConditionController.Add))]
    public static class BattleConditionController_Add_Patch
    {
        private const string DEDUP_CONTEXT = AnnouncementContexts.BATTLE_CONDITION_ADD;

        /// <summary>
        /// The spoken name of a condition, shared by the add and removal announcements so both use the
        /// same words. Returns false for a hidden/internal condition (no name message: Defend, Escape,
        /// Dying and the unnamed system conditions), which is never announced. Otherwise the localized
        /// name, or "Status {id}" when the condition or its message can't be resolved.
        /// </summary>
        internal static bool TryGetConditionName(Il2CppLast.Data.Master.Condition condition, int id, out string name)
        {
            name = null;
            try
            {
                if (condition != null)
                {
                    string conditionMesId = condition.MesIdName;
                    if (string.IsNullOrEmpty(conditionMesId) || conditionMesId == "None")
                        return false;

                    var messageManager = MessageManager.Instance;
                    if (messageManager != null)
                    {
                        string localizedConditionName = messageManager.GetMessage(conditionMesId);
                        if (!string.IsNullOrEmpty(localizedConditionName))
                            name = localizedConditionName;
                    }
                }
            }
            catch (Exception condEx)
            {
                MelonLogger.Warning($"Error resolving condition ID {id}: {condEx.Message}");
            }

            if (name == null)
            {
                // Final fallback: the raw ID if the name couldn't be resolved
                name = string.Format(T("Status {0}"), id);
                MelonLogger.Warning($"[Status] Could not resolve condition ID {id}, announcing as raw ID");
            }
            return true;
        }

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
                string targetName = BattleUnitHelper.GetUnitName(battleUnitData) ?? T("Unknown");

                // Get the condition from ConfirmedConditionList (includes equipment statuses)
                Il2CppLast.Data.Master.Condition added = null;
                try
                {
                    var confirmedList = battleUnitData.BattleUnitDataInfo?.Parameter?.ConfirmedConditionList();
                    if (confirmedList != null)
                    {
                        foreach (var condition in confirmedList)
                        {
                            if (condition != null && condition.Id == id)
                            {
                                added = condition;
                                break;
                            }
                        }
                    }
                }
                catch (Exception condEx)
                {
                    MelonLogger.Warning($"Error resolving condition ID {id}: {condEx.Message}");
                }

                // Skip conditions with no message ID (internal/hidden statuses)
                if (!TryGetConditionName(added, id, out string conditionName))
                {
                    return;
                }

                string announcement = string.Format(T("{0}: {1}"), targetName, conditionName);

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

    /// <summary>
    /// "X: Poison removed" when a status leaves a unit in battle: cures (items, spells, abilities),
    /// natural wear-off, conflicts (Haste cancelling Slow) and revive (KO removed).
    ///
    /// Hook: BattleConditionController.RemoveFunction(BattleUnitData, int id) (private, RVA 0x3602A0,
    /// unique), the mirror of the Add hook above. FF4 removes conditions from the unit's
    /// CurrentConditionList in many places (RecoveryConditionFunction/UniqueFunction/DispelFunction
    /// .UpdateParameter for cures, BattleConditionFunction.NaturalRemove and Recovery(untilType) for
    /// wear-off, ConditionUtility for conflicts, Remove(unit, id, isNegate) for the controller's own
    /// removals); none of those but Remove go through one method. What they share is the controller's
    /// sync: CheckConditionFunction → RemoveConditionFunction → RemoveFunction destroys the effect
    /// function of every condition that left the list (Add creates it on the way in), and Remove
    /// tail-calls RemoveFunction. So RemoveFunction runs once per status that really goes, and only
    /// for statuses whose function (and so whose add announcement) exists.
    ///
    /// Prefix, so the function being removed is still in BattleUnitDataInfo.BattleConditionFunction
    /// to name it; the condition list is already in its after-removal state at that point on both
    /// paths. Silent for: the battle-end clean-up (BattleEndRecoveryCondition); statuses cleared by
    /// KO (the unit's list holds a ConditionType.UnableFight condition); a condition still present
    /// (one of two stacked instances going); unnamed conditions; the same unit and condition twice
    /// in one frame. Remove's isNegate is false at every call site in FF4, and negated/immune
    /// conditions leave the list before a function (and an add announcement) is ever created.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.Battle.BattleConditionController), nameof(Il2CppLast.Battle.BattleConditionController.RemoveFunction))]
    public static class BattleConditionController_RemoveFunction_Patch
    {
        // ConditionType.UnableFight (KO); Condition.ConditionType is an int.
        private const int CONDITION_TYPE_KO = 5;

        /// <summary>
        /// Set when the battle-end clean-up starts (victory or escape); cleared when the next battle
        /// starts or a player's turn begins.
        /// </summary>
        internal static bool BattleEnding;

        private static int lastFrame = -1;
        private static readonly HashSet<(IntPtr unit, int id)> spokenThisFrame = new HashSet<(IntPtr unit, int id)>();

        [HarmonyPrefix]
        public static void Prefix(BattleUnitData battleUnitData, int id)
        {
            try
            {
                if (BattleEnding || battleUnitData == null)
                    return;

                var info = battleUnitData.BattleUnitDataInfo;
                var functions = info?.BattleConditionFunction;
                if (functions == null)
                    return;

                // RemoveFunction removes the LAST function with this condition id, and does nothing
                // when there is none.
                Il2CppLast.Data.Master.Condition removed = null;
                for (int i = functions.Count - 1; i >= 0; i--)
                {
                    var condition = functions[i]?.condition;
                    if (condition != null && condition.Id == id)
                    {
                        removed = condition;
                        break;
                    }
                }
                if (removed == null)
                    return;

                var current = info.Parameter?.CurrentConditionList;
                if (current != null)
                {
                    bool knockedOut = false;
                    foreach (var condition in current)
                    {
                        if (condition == null) continue;
                        if (condition.Id == id) return; // still has it
                        if (condition.ConditionType == CONDITION_TYPE_KO) knockedOut = true;
                    }
                    if (knockedOut && removed.ConditionType != CONDITION_TYPE_KO)
                        return;
                }

                if (!BattleConditionController_Add_Patch.TryGetConditionName(removed, id, out string conditionName))
                    return;

                int frame = UnityEngine.Time.frameCount;
                if (frame != lastFrame)
                {
                    lastFrame = frame;
                    spokenThisFrame.Clear();
                }
                if (!spokenThisFrame.Add((battleUnitData.Pointer, id)))
                    return;

                string targetName = BattleUnitHelper.GetUnitName(battleUnitData) ?? T("Unknown");
                FFIV_ScreenReaderMod.SpeakText(string.Format(T("{0}: {1} removed"), targetName, conditionName), interrupt: false);

                // The status is gone, so adding it again is news: don't let the add announcement's
                // duplicate guard swallow a second "X: Poison".
                AnnouncementDeduplicator.Reset(AnnouncementContexts.BATTLE_CONDITION_ADD);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in BattleConditionController.RemoveFunction patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// BattleConditionController.BattleEndRecoveryCondition (RVA 0x35C990, unique; called from
    /// BattleController.StartWinResult, EndWinFadeOutCallback and EndEscapeFadeOut) clears the party's
    /// battle-only statuses through Remove → RemoveFunction. Those are clean-up, not news.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.Battle.BattleConditionController), nameof(Il2CppLast.Battle.BattleConditionController.BattleEndRecoveryCondition))]
    public static class BattleConditionController_BattleEndRecoveryCondition_Patch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            BattleConditionController_RemoveFunction_Patch.BattleEnding = true;
        }
    }

    /// <summary>
    /// Patch BattleController.StartBattle to suppress navigation at battle start.
    /// Fires immediately when battle begins, before any combat actions.
    /// Uses explicit parameter types to avoid "Ambiguous match" error (two overloads exist).
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.Battle.BattleController), "StartBattle",
        new Type[] { typeof(Il2CppLast.Battle.InstantiateManager), typeof(bool) })]
    public static class BattleController_StartBattle_Patch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            try
            {
                BattleState.SetActive();

                // New battle: clear the previous battle's announcement guards so its last turn,
                // command, list entry, status or system message ("Back attack!") is not
                // swallowed as a duplicate when it repeats here.
                AnnouncementDeduplicator.Reset(
                    AnnouncementContexts.BATTLE_TURN,
                    AnnouncementContexts.BATTLE_COMMAND_SELECT,
                    AnnouncementContexts.BATTLE_ITEM_SELECT,
                    AnnouncementContexts.BATTLE_ABILITY_SELECT,
                    AnnouncementContexts.BATTLE_CONDITION_ADD,
                    AnnouncementContexts.BATTLE_SET_COMMAND_MESSAGE);
                BattleCommandMessageManualPatches.ResetState();
                BattleResultState.ResetState();
                BattleConditionController_RemoveFunction_Patch.BattleEnding = false;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in BattleController.StartBattle patch: {ex.Message}");
            }
        }
    }

    // Patch BattleMenuController from KeyInput namespace - announces result messages
    // Skips action names that were already announced by CreateActFunction
    // Uses simple string equality for deduplication (no time-based logic per Rule 3)
    [HarmonyPatch(typeof(Il2CppLast.UI.KeyInput.BattleMenuController), nameof(Il2CppLast.UI.KeyInput.BattleMenuController.SetCommadnMessage))]
    public static class BattleMenuController_KeyInput_SetCommadnMessage_Patch
    {
        private const string DEDUP_CONTEXT = AnnouncementContexts.BATTLE_SET_COMMAND_MESSAGE;

        [HarmonyPostfix]
        public static void Postfix(string message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(message))
                {
                    AnnouncementDeduplicator.Reset(DEDUP_CONTEXT);
                    return;
                }

                // Create managed string from Il2Cpp string to prevent GC issues
                string cleanMessage = message.Trim();

                // Skip if this message matches the action name just announced by CreateActFunction
                if (GlobalBattleMessageTracker.IsRedundantActionMessage(cleanMessage))
                {
                    return;
                }

                // Skip duplicate messages
                if (!AnnouncementDeduplicator.ShouldAnnounce(DEDUP_CONTEXT, cleanMessage))
                {
                    return;
                }

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
        private const string DEDUP_CONTEXT = AnnouncementContexts.BATTLE_TURN;
        public static Il2Cpp.BattlePlayerData CurrentActiveCharacter = null;

        /// <summary>
        /// Clears the command-menu guard before the new turn's command list is built, so the
        /// first focused command is announced even when it sits at the previous turn's index.
        /// </summary>
        [HarmonyPrefix]
        public static void Prefix()
        {
            AnnouncementDeduplicator.Reset(AnnouncementContexts.BATTLE_COMMAND_SELECT);
        }

        [HarmonyPostfix]
        public static void Postfix(Il2Cpp.BattlePlayerData targetData)
        {
            try
            {
                // Keep battle state active as fallback (primary hook is StartBattle)
                BattleState.SetActive();

                // A player's turn means the battle is running, even if the StartBattle hook was skipped.
                BattleConditionController_RemoveFunction_Patch.BattleEnding = false;

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

                        string message = string.Format(T("{0}'s turn"), characterName);
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
        public const string DEDUP_CONTEXT = AnnouncementContexts.BATTLE_TARGET_PLAYER;
        public const string DEDUP_CONTEXT_ENEMY = AnnouncementContexts.BATTLE_TARGET_ENEMY;

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

                            announcement = FFIV_ScreenReader.Utils.MenuPosition.Format(announcement, index, playerList.Count);
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
        public const string DEDUP_CONTEXT = AnnouncementContexts.BATTLE_TARGET_ENEMY;

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
                            string localizedName = MessageHelper.GetLocalizedMessage(mesIdName);
                            if (localizedName != null)
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
                                        string enemyName = MessageHelper.GetLocalizedMessage(enemyMesId);
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

                                // Add positional indicator if there are multiple enemies with the same name
                                if (sameNameCount > 1)
                                {
                                    // Use letter suffixes: A, B, C, etc.
                                    char letter = (char)('A' + positionInGroup);
                                    announcement += $" {letter}";
                                }

                                // Append enemy HP according to user preference (Numbers/Percentage/Hidden)
                                try
                                {
                                    var unitDataInfo = selectedEnemy.BattleUnitDataInfo;
                                    if (unitDataInfo != null && unitDataInfo.Parameter != null)
                                    {
                                        int currentHP = unitDataInfo.Parameter.CurrentHP;
                                        int maxHP = unitDataInfo.Parameter.ConfirmedMaxHp();

                                        switch (PreferencesManager.EnemyHPDisplay)
                                        {
                                            case 0: // Numbers (default)
                                                announcement += $", HP {currentHP}/{maxHP}";
                                                break;
                                            case 1: // Percentage
                                                int pct = maxHP > 0 ? (currentHP * 100 / maxHP) : 0;
                                                announcement += $", {pct}%";
                                                break;
                                            case 2: // Hidden
                                                break;
                                        }
                                    }
                                }
                                catch
                                {
                                    // Continue with just the name if HP can't be read
                                }

                                announcement = FFIV_ScreenReader.Utils.MenuPosition.Format(announcement, index, enemyList.Count);
                                FFIV_ScreenReaderMod.SpeakText(announcement);
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

    /// <summary>
    /// Manual patches for BattleCommandMessageController to announce defeat messages.
    /// </summary>
    public static class BattleCommandMessageManualPatches
    {
        private const string DEDUP_CONTEXT = AnnouncementContexts.BATTLE_COMMAND_MESSAGE;

        /// <summary>
        /// Apply manual patches for battle command messages (defeat message, etc.)
        /// </summary>
        public static void ApplyManualPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                PatchBattleCommandMessage(harmony);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Message] Error applying patches: {ex.Message}");
            }
        }


        /// <summary>
        /// Patch BattleCommandMessageController.SetMessage for system messages like "The party was defeated".
        /// </summary>
        private static void PatchBattleCommandMessage(HarmonyLib.Harmony harmony)
        {
            try
            {
                // KeyInput version - uses SetMessage
                var keyInputType = PatchHelper.FindType("Il2CppLast.UI.KeyInput.BattleCommandMessageController");
                if (keyInputType != null)
                {
                    var setMessageMethod = AccessTools.Method(keyInputType, "SetMessage");
                    if (setMessageMethod != null)
                    {
                        var postfix = typeof(BattleCommandMessageManualPatches).GetMethod(
                            nameof(SetMessage_Postfix), BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(setMessageMethod, postfix: new HarmonyMethod(postfix));
                    }
                    else
                    {
                        MelonLogger.Warning("[Battle Message] KeyInput.BattleCommandMessageController.SetMessage method not found");
                    }
                }
                else
                {
                    MelonLogger.Warning("[Battle Message] KeyInput.BattleCommandMessageController type not found");
                }

                // Touch version - uses SetCommandMessage and SetSystemMessage
                var touchType = PatchHelper.FindType("Il2CppLast.UI.Touch.BattleCommandMessageController");
                if (touchType != null)
                {
                    // Patch SetCommandMessage
                    var setCommandMsgMethod = AccessTools.Method(touchType, "SetCommandMessage");
                    if (setCommandMsgMethod != null)
                    {
                        var postfix = typeof(BattleCommandMessageManualPatches).GetMethod(
                            nameof(SetMessage_Postfix), BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(setCommandMsgMethod, postfix: new HarmonyMethod(postfix));
                    }

                    // Patch SetSystemMessage
                    var setSystemMsgMethod = AccessTools.Method(touchType, "SetSystemMessage");
                    if (setSystemMsgMethod != null)
                    {
                        var postfix = typeof(BattleCommandMessageManualPatches).GetMethod(
                            nameof(SetMessage_Postfix), BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(setSystemMsgMethod, postfix: new HarmonyMethod(postfix));
                    }
                }
                else
                {
                    MelonLogger.Warning("[Battle Message] Touch.BattleCommandMessageController type not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Message] Error patching BattleCommandMessageController: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for BattleCommandMessageController.SetMessage/SetCommandMessage/SetSystemMessage.
        /// Announces battle messages including "The party was defeated".
        /// </summary>
        public static void SetMessage_Postfix(object __0)
        {
            try
            {
                // __0 is the message string (using __0 to avoid IL2CPP string param issues)
                string message = __0?.ToString();
                if (string.IsNullOrEmpty(message)) return;

                // Deduplicate
                if (!AnnouncementDeduplicator.ShouldAnnounce(DEDUP_CONTEXT, message)) return;

                // Clean up the message
                string cleanMessage = TextUtils.NormalizeWhitespace(TextUtils.StripIconMarkup(message));

                if (string.IsNullOrEmpty(cleanMessage)) return;

                // Skip action names that were already announced by CreateActFunction
                if (GlobalBattleMessageTracker.IsRedundantActionMessage(cleanMessage))
                {
                    return;
                }

                FFIV_ScreenReaderMod.SpeakText(cleanMessage, interrupt: IsDefeatMessage(cleanMessage));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Message] Error in SetMessage_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// The defeat message interrupts so it's heard immediately. Compared against the game's own
        /// localized text for BATTLE_RESULT_ANNIHILATION so it works in every language; the English
        /// keyword is only a fallback for when that text can't be resolved.
        /// </summary>
        private static bool IsDefeatMessage(string message)
        {
            string defeat = TextUtils.NormalizeWhitespace(TextUtils.StripIconMarkup(
                MessageHelper.GetLocalizedMessage(Il2Cpp.UiMessageConstants.BATTLE_RESULT_ANNIHILATION)));
            return string.IsNullOrEmpty(defeat)
                ? message.Contains("defeated", StringComparison.OrdinalIgnoreCase)
                : message == defeat;
        }

        /// <summary>
        /// Reset state tracking (call at battle start).
        /// </summary>
        public static void ResetState()
        {
            AnnouncementDeduplicator.Reset(DEDUP_CONTEXT);
        }
    }
}
