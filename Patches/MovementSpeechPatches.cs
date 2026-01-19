using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using Il2Cpp;
using Il2CppLast.Map;
using Il2CppLast.Entity.Field;
using FFIV_ScreenReader.Utils;

namespace FFIV_ScreenReader.Patches
{
    /// <summary>
    /// Patches FieldPlayer.GetOn/GetOff to announce when entering/exiting vehicles.
    /// Uses manual Harmony patching for direct hook into vehicle boarding/disembarking.
    ///
    /// FF4 Vehicles:
    /// - Hovercraft (TRANSPORT_SHIP)
    /// - Enterprise (TRANSPORT_PLANE)
    /// - Falcon/Lunar Whale (TRANSPORT_SPECIAL_PLANE)
    /// - Yellow/Black Chocobo (TRANSPORT_YELLOW_CHOCOBO, TRANSPORT_BLACK_CHOCOBO)
    /// </summary>
    public static class MovementSpeechPatches
    {
        private static bool isPatched = false;

        // TransportationType enum values (from MapConstants.TransportationType in dump.cs)
        private const int TRANSPORT_NONE = 0;
        private const int TRANSPORT_PLAYER = 1;
        private const int TRANSPORT_SHIP = 2;           // Hovercraft in FF4
        private const int TRANSPORT_PLANE = 3;          // Enterprise
        private const int TRANSPORT_SYMBOL = 4;
        private const int TRANSPORT_CONTENT = 5;
        private const int TRANSPORT_SUBMARINE = 6;
        private const int TRANSPORT_LOWFLYING = 7;
        private const int TRANSPORT_SPECIAL_PLANE = 8;  // Falcon/Lunar Whale
        private const int TRANSPORT_YELLOW_CHOCOBO = 9;
        private const int TRANSPORT_BLACK_CHOCOBO = 10;

        // Track previous transportation for change detection
        private static int lastTransportationId = TRANSPORT_PLAYER;
        private static int lastAnnouncedTransportId = -1;

        /// <summary>
        /// Callback to trigger entity scan when field is ready.
        /// Set by FFIV_ScreenReaderMod during initialization.
        /// </summary>
        public static Action OnFieldReady;

        /// <summary>
        /// Apply manual Harmony patches for vehicle boarding/disembarking.
        /// Called from FFIV_ScreenReaderMod initialization.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched)
                return;

            try
            {
                TryPatchGetOn(harmony);
                TryPatchGetOff(harmony);
                TryPatchChangeTransportation(harmony);
                TryPatchFieldReady(harmony);
                isPatched = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MoveState] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Patch MainGame.set_FieldReady - fires when the field is fully ready for gameplay.
        /// This is the game's internal signal that entities are instantiated and the player can interact.
        /// </summary>
        private static void TryPatchFieldReady(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type mainGameType = typeof(MainGame);
                MethodInfo targetMethod = null;

                // Look for the property setter set_FieldReady(bool value)
                foreach (var method in mainGameType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (method.Name == "set_FieldReady")
                    {
                        var parameters = method.GetParameters();
                        if (parameters.Length == 1 && parameters[0].ParameterType == typeof(bool))
                        {
                            targetMethod = method;
                            break;
                        }
                    }
                }

                if (targetMethod != null)
                {
                    var postfix = typeof(MovementSpeechPatches).GetMethod(nameof(FieldReady_Postfix),
                        BindingFlags.Public | BindingFlags.Static);

                    harmony.Patch(targetMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[FieldReady] Could not find MainGame.set_FieldReady method");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[FieldReady] Error patching set_FieldReady: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for MainGame.set_FieldReady - triggers entity scan when field becomes ready.
        /// </summary>
        public static void FieldReady_Postfix(bool value)
        {
            try
            {
                if (value)
                {
                    OnFieldReady?.Invoke();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[FieldReady] Error in set_FieldReady postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Patch FieldController.ChangeTransportation - the central method for all transportation changes.
        /// This is more reliable than GetOn/GetOff which don't fire in all scenarios.
        /// Signature: public void ChangeTransportation(int transportationId, bool changeCollisionEnable = True, bool isBackground = False)
        /// </summary>
        private static void TryPatchChangeTransportation(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type fieldControllerType = typeof(FieldController);
                MethodInfo targetMethod = null;

                foreach (var method in fieldControllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (method.Name == "ChangeTransportation")
                    {
                        var parameters = method.GetParameters();
                        // ChangeTransportation(int transportationId, bool changeCollisionEnable = True, bool isBackground = False)
                        if (parameters.Length >= 1 && parameters[0].ParameterType == typeof(int))
                        {
                            targetMethod = method;
                            break;
                        }
                    }
                }

                if (targetMethod != null)
                {
                    var postfix = typeof(MovementSpeechPatches).GetMethod(nameof(ChangeTransportation_Postfix),
                        BindingFlags.Public | BindingFlags.Static);

                    harmony.Patch(targetMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[MoveState] Could not find ChangeTransportation method");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MoveState] Error patching ChangeTransportation: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for FieldController.ChangeTransportation - announces transportation changes.
        /// This is the most reliable hook for detecting vehicle boarding/disembarking.
        /// </summary>
        public static void ChangeTransportation_Postfix(int transportationId)
        {
            try
            {
                // Skip if same as last announced (prevents duplicates)
                if (transportationId == lastAnnouncedTransportId)
                    return;

                int previousId = lastTransportationId;
                lastTransportationId = transportationId;

                // Skip initial state
                if (previousId == -1)
                    return;

                // Skip intermediate/special states (CONTENT, SYMBOL, NONE)
                // These are used during cinematics and transitions, not actual vehicle changes
                if (IsIntermediateTransportation(transportationId))
                {
                    return;
                }

                bool wasOnVehicle = IsVehicleTransportation(previousId);
                bool isOnVehicle = IsVehicleTransportation(transportationId);
                bool isNowOnFoot = (transportationId == TRANSPORT_PLAYER);

                string announcement = null;

                if (!wasOnVehicle && isOnVehicle)
                {
                    // Boarding a vehicle
                    string vehicleName = GetTransportationName(transportationId);
                    if (!string.IsNullOrEmpty(vehicleName))
                    {
                        announcement = $"On {vehicleName}";
                        MoveStateHelper.SetVehicleState(transportationId);
                    }
                }
                else if (wasOnVehicle && isNowOnFoot)
                {
                    // Disembarking - specifically to TRANSPORT_PLAYER (on foot)
                    announcement = "On foot";
                    MoveStateHelper.SetOnFoot();
                }

                if (announcement != null)
                {
                    lastAnnouncedTransportId = transportationId;
                    Core.FFIV_ScreenReaderMod.SpeakText(announcement, interrupt: false);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[MoveState] Error in ChangeTransportation patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if a transportationId is an intermediate/special state (not a real vehicle or on-foot).
        /// These are used during cinematics and transitions.
        /// </summary>
        private static bool IsIntermediateTransportation(int transportationId)
        {
            return transportationId == TRANSPORT_NONE ||
                   transportationId == TRANSPORT_SYMBOL ||
                   transportationId == TRANSPORT_CONTENT;
        }

        /// <summary>
        /// Check if a transportationId represents a vehicle (not on foot or intermediate).
        /// </summary>
        private static bool IsVehicleTransportation(int transportationId)
        {
            return transportationId != TRANSPORT_NONE &&
                   transportationId != TRANSPORT_PLAYER &&
                   transportationId != TRANSPORT_SYMBOL &&
                   transportationId != TRANSPORT_CONTENT;
        }

        /// <summary>
        /// Patch GetOn - called when player boards a vehicle.
        /// FF4 Signature: public void GetOn(int typeId, bool isBackground = False)
        /// </summary>
        private static void TryPatchGetOn(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type fieldPlayerType = typeof(FieldPlayer);
                MethodInfo targetMethod = null;

                foreach (var method in fieldPlayerType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (method.Name == "GetOn")
                    {
                        var parameters = method.GetParameters();
                        // FF4: GetOn(int typeId, bool isBackground = False)
                        if (parameters.Length >= 1 &&
                            parameters[0].ParameterType == typeof(int))
                        {
                            targetMethod = method;
                            break;
                        }
                    }
                }

                if (targetMethod != null)
                {
                    var postfix = typeof(MovementSpeechPatches).GetMethod(nameof(GetOn_Postfix),
                        BindingFlags.Public | BindingFlags.Static);

                    harmony.Patch(targetMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[MoveState] Could not find GetOn method");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MoveState] Error patching GetOn: {ex.Message}");
            }
        }

        /// <summary>
        /// Patch GetOff - called when player disembarks a vehicle.
        /// FF4 Signature: public void GetOff(int typeId, int layer = -1)
        /// </summary>
        private static void TryPatchGetOff(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type fieldPlayerType = typeof(FieldPlayer);
                MethodInfo targetMethod = null;

                foreach (var method in fieldPlayerType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (method.Name == "GetOff")
                    {
                        var parameters = method.GetParameters();
                        // FF4: GetOff(int typeId, int layer = -1)
                        if (parameters.Length >= 1 && parameters[0].ParameterType == typeof(int))
                        {
                            targetMethod = method;
                            break;
                        }
                    }
                }

                if (targetMethod != null)
                {
                    var postfix = typeof(MovementSpeechPatches).GetMethod(nameof(GetOff_Postfix),
                        BindingFlags.Public | BindingFlags.Static);

                    harmony.Patch(targetMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[MoveState] Could not find GetOff method");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MoveState] Error patching GetOff: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for GetOn - announces vehicle boarding.
        /// Note: ChangeTransportation is the primary hook; this is supplementary.
        /// </summary>
        public static void GetOn_Postfix(int typeId)
        {
            try
            {
                // Skip if already announced via ChangeTransportation
                if (typeId == lastAnnouncedTransportId)
                    return;

                string vehicleName = GetTransportationName(typeId);
                if (!string.IsNullOrEmpty(vehicleName))
                {
                    MoveStateHelper.SetVehicleState(typeId);
                    lastAnnouncedTransportId = typeId;
                    lastTransportationId = typeId;
                    string announcement = $"On {vehicleName}";
                    Core.FFIV_ScreenReaderMod.SpeakText(announcement, interrupt: false);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[MoveState] Error in GetOn patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for GetOff - announces vehicle disembarking.
        /// Note: ChangeTransportation is the primary hook; this is supplementary.
        /// </summary>
        public static void GetOff_Postfix(int typeId)
        {
            try
            {
                // Skip if already announced via ChangeTransportation
                if (lastAnnouncedTransportId == TRANSPORT_PLAYER)
                    return;

                string vehicleName = GetTransportationName(typeId);
                MoveStateHelper.SetOnFoot();
                lastAnnouncedTransportId = TRANSPORT_PLAYER;
                lastTransportationId = TRANSPORT_PLAYER;

                // Only announce "On foot" if we were on a known vehicle
                if (!string.IsNullOrEmpty(vehicleName))
                {
                    Core.FFIV_ScreenReaderMod.SpeakText("On foot", interrupt: false);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[MoveState] Error in GetOff patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Get human-readable name for FF4 TransportationType.
        /// Returns more specific names where possible (Enterprise vs generic airship).
        /// </summary>
        private static string GetTransportationName(int typeId)
        {
            switch (typeId)
            {
                case TRANSPORT_SHIP:
                    return "hovercraft";
                case TRANSPORT_PLANE:
                    return "Enterprise";
                case TRANSPORT_SPECIAL_PLANE:
                    // Falcon or Lunar Whale - try to get specific name from TransportationController
                    return GetSpecialPlaneName();
                case TRANSPORT_LOWFLYING:
                    return "black chocobo"; // Low flying in FF4 is typically black chocobo
                case TRANSPORT_YELLOW_CHOCOBO:
                    return "yellow chocobo";
                case TRANSPORT_BLACK_CHOCOBO:
                    return "black chocobo";
                default:
                    return null;
            }
        }

        /// <summary>
        /// Try to get specific name for TRANSPORT_SPECIAL_PLANE (Falcon or Lunar Whale).
        /// </summary>
        private static string GetSpecialPlaneName()
        {
            try
            {
                var fieldMap = GameObjectCache.Get<FieldMap>();
                if (fieldMap?.fieldController?.transportation?.CurrentTransportation != null)
                {
                    var transport = fieldMap.fieldController.transportation.CurrentTransportation;
                    if (!string.IsNullOrEmpty(transport.MessageId))
                    {
                        var msg = Il2CppLast.Management.MessageManager.Instance?.GetMessage(transport.MessageId);
                        if (!string.IsNullOrEmpty(msg))
                            return msg;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MoveState] Error getting special plane name: {ex.Message}");
            }
            return "airship";
        }

        /// <summary>
        /// Reset state tracking (call on map transitions).
        /// </summary>
        public static void ResetState()
        {
            lastTransportationId = TRANSPORT_PLAYER;
            lastAnnouncedTransportId = -1;
            MoveStateHelper.ResetState();
        }

        /// <summary>
        /// Sync tracking to on-foot state (called when entering interior maps).
        /// </summary>
        public static void SyncToOnFoot()
        {
            lastTransportationId = TRANSPORT_PLAYER;
            lastAnnouncedTransportId = TRANSPORT_PLAYER;
        }
    }

    /// <summary>
    /// Backup patch for ChangeMoveState - fires when moveState changes.
    /// Catches all state transitions including those that bypass GetOn/GetOff.
    /// </summary>
    [HarmonyPatch(typeof(FieldPlayer), nameof(FieldPlayer.ChangeMoveState))]
    public static class FieldPlayer_ChangeMoveState_Patch
    {
        private static int lastMoveState = -1;

        [HarmonyPostfix]
        public static void Postfix(FieldPlayer __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                int currentMoveState = (int)__instance.moveState;

                // Only process if state actually changed
                if (currentMoveState != lastMoveState)
                {
                    int previousState = lastMoveState;
                    lastMoveState = currentMoveState;

                    // Skip first call (initialization)
                    if (previousState == -1)
                        return;

                    // Announce state change (handles both boarding and disembarking)
                    MoveStateHelper.AnnounceStateChange(previousState, currentMoveState);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[MoveState] Error in ChangeMoveState patch: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
