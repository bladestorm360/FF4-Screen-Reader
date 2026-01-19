using Il2CppLast.Map;
using Il2CppLast.Entity.Field;
using Il2Cpp;
using MelonLoader;

namespace FFIV_ScreenReader.Utils
{
    /// <summary>
    /// Helper class for tracking player movement state.
    /// Handles vehicle boarding/disembarking state for FF4's vehicles:
    /// Hovercraft, Enterprise, Falcon, Lunar Whale, Chocobo
    ///
    /// State is updated directly by GetOn/GetOff patches - no polling or timeouts.
    /// </summary>
    public static class MoveStateHelper
    {
        // MoveState enum values (from FieldPlayerConstants.MoveState)
        public const int MOVE_STATE_WALK = 0;
        public const int MOVE_STATE_DUSH = 1;    // Dash
        public const int MOVE_STATE_AIRSHIP = 2;
        public const int MOVE_STATE_SHIP = 3;     // Hovercraft in FF4
        public const int MOVE_STATE_LOWFLYING = 4;
        public const int MOVE_STATE_CHOCOBO = 5;
        public const int MOVE_STATE_GIMMICK = 6;

        // TransportationType enum values (more specific vehicle types)
        public const int TRANSPORT_NONE = 0;
        public const int TRANSPORT_PLAYER = 1;
        public const int TRANSPORT_SHIP = 2;
        public const int TRANSPORT_PLANE = 3;       // Enterprise
        public const int TRANSPORT_SYMBOL = 4;
        public const int TRANSPORT_CONTENT = 5;
        public const int TRANSPORT_SUBMARINE = 6;
        public const int TRANSPORT_LOWFLYING = 7;
        public const int TRANSPORT_SPECIAL_PLANE = 8;  // Falcon/Lunar Whale
        public const int TRANSPORT_YELLOW_CHOCOBO = 9;
        public const int TRANSPORT_BLACK_CHOCOBO = 10;

        // Cached state tracking (set by GetOn/GetOff patches)
        private static int cachedMoveState = MOVE_STATE_WALK;
        private static int cachedTransportType = TRANSPORT_NONE;
        private static int lastAnnouncedState = -1;

        /// <summary>
        /// Set vehicle state when boarding (called from GetOn patch).
        /// </summary>
        /// <param name="transportationType">The TransportationType ID</param>
        public static void SetVehicleState(int transportationType)
        {
            cachedTransportType = transportationType;
            cachedMoveState = TransportTypeToMoveState(transportationType);
            lastAnnouncedState = cachedMoveState; // Prevent duplicate from ChangeMoveState patch
        }

        /// <summary>
        /// Set on foot state when disembarking (called from GetOff patch).
        /// </summary>
        public static void SetOnFoot()
        {
            cachedTransportType = TRANSPORT_NONE;
            cachedMoveState = MOVE_STATE_WALK;
            lastAnnouncedState = MOVE_STATE_WALK; // Prevent duplicate from ChangeMoveState patch
        }

        /// <summary>
        /// Reset state tracking (call on map transitions).
        /// </summary>
        public static void ResetState()
        {
            cachedMoveState = MOVE_STATE_WALK;
            cachedTransportType = TRANSPORT_NONE;
            lastAnnouncedState = -1;
        }

        /// <summary>
        /// Called when transitioning to a new map.
        /// Interior maps (non-world maps) should always be on-foot state.
        /// </summary>
        /// <param name="isWorldMap">True if the new map is a world map (overworld, underworld, moon)</param>
        /// <returns>True if state changed and announcement was made</returns>
        public static bool OnMapTransition(bool isWorldMap)
        {
            // If entering a non-world map (interior/dungeon) and currently in vehicle state,
            // transition to on-foot (you're walking inside the ship/building)
            if (!isWorldMap && IsVehicleState(cachedMoveState))
            {
                cachedMoveState = MOVE_STATE_WALK;
                cachedTransportType = TRANSPORT_NONE;
                lastAnnouncedState = MOVE_STATE_WALK;

                // Sync the patches tracking to on-foot as well
                FFIV_ScreenReader.Patches.MovementSpeechPatches.SyncToOnFoot();

                FFIV_ScreenReader.Core.FFIV_ScreenReaderMod.SpeakText("On foot", interrupt: false);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Check if a state is a vehicle state (ship, chocobo, airship, low flying).
        /// </summary>
        public static bool IsVehicleState(int state)
        {
            return state == MOVE_STATE_SHIP || state == MOVE_STATE_CHOCOBO ||
                   state == MOVE_STATE_AIRSHIP || state == MOVE_STATE_LOWFLYING;
        }

        /// <summary>
        /// Announce movement state changes (called from ChangeMoveState patch).
        /// Handles transitions between vehicle and on-foot states.
        /// </summary>
        public static void AnnounceStateChange(int previousState, int newState)
        {
            // Skip if same as last announced state (prevents duplicates from GetOn/GetOff + ChangeMoveState)
            if (newState == lastAnnouncedState)
                return;

            string announcement = null;

            // Transitioning TO a vehicle state
            if (newState == MOVE_STATE_SHIP)
            {
                announcement = "On hovercraft";
                cachedMoveState = MOVE_STATE_SHIP;
            }
            else if (newState == MOVE_STATE_CHOCOBO)
            {
                announcement = "On chocobo";
                cachedMoveState = MOVE_STATE_CHOCOBO;
            }
            else if (newState == MOVE_STATE_AIRSHIP || newState == MOVE_STATE_LOWFLYING)
            {
                announcement = "On airship";
                cachedMoveState = newState;
            }
            // Transitioning FROM vehicle TO on-foot
            else if (IsVehicleState(previousState) &&
                     (newState == MOVE_STATE_WALK || newState == MOVE_STATE_DUSH))
            {
                announcement = "On foot";
                cachedMoveState = newState;
                cachedTransportType = TRANSPORT_NONE;
            }
            else
            {
                // Just update cached state without announcement
                cachedMoveState = newState;
            }

            if (announcement != null)
            {
                lastAnnouncedState = newState;
                FFIV_ScreenReader.Core.FFIV_ScreenReaderMod.SpeakText(announcement, interrupt: false);
            }
        }

        /// <summary>
        /// Convert TransportationType to MoveState.
        /// </summary>
        private static int TransportTypeToMoveState(int transportationType)
        {
            switch (transportationType)
            {
                case TRANSPORT_SHIP:
                    return MOVE_STATE_SHIP;
                case TRANSPORT_PLANE:
                case TRANSPORT_SPECIAL_PLANE:
                    return MOVE_STATE_AIRSHIP;
                case TRANSPORT_LOWFLYING:
                    return MOVE_STATE_LOWFLYING;
                case TRANSPORT_YELLOW_CHOCOBO:
                case TRANSPORT_BLACK_CHOCOBO:
                    return MOVE_STATE_CHOCOBO;
                default:
                    return MOVE_STATE_WALK;
            }
        }

        /// <summary>
        /// Get current MoveState (returns cached state set by GetOn/GetOff).
        /// </summary>
        public static int GetCurrentMoveState()
        {
            return cachedMoveState;
        }

        /// <summary>
        /// Get current TransportationType.
        /// </summary>
        public static int GetCurrentTransportType()
        {
            return cachedTransportType;
        }

        /// <summary>
        /// Check if currently controlling hovercraft.
        /// </summary>
        public static bool IsControllingHovercraft()
        {
            return cachedMoveState == MOVE_STATE_SHIP;
        }

        /// <summary>
        /// Check if currently on foot (walking or dashing).
        /// </summary>
        public static bool IsOnFoot()
        {
            return cachedMoveState == MOVE_STATE_WALK || cachedMoveState == MOVE_STATE_DUSH;
        }

        /// <summary>
        /// Check if currently riding chocobo.
        /// </summary>
        public static bool IsRidingChocobo()
        {
            return cachedMoveState == MOVE_STATE_CHOCOBO;
        }

        /// <summary>
        /// Check if currently controlling airship.
        /// </summary>
        public static bool IsControllingAirship()
        {
            return cachedMoveState == MOVE_STATE_AIRSHIP;
        }

        /// <summary>
        /// Get pathfinding scope multiplier based on current MoveState.
        /// </summary>
        public static float GetPathfindingMultiplier()
        {
            switch (cachedMoveState)
            {
                case MOVE_STATE_WALK:
                case MOVE_STATE_DUSH:
                    return 1.0f;  // Baseline (on foot)

                case MOVE_STATE_SHIP:
                    return 2.5f;  // 2.5x scope for hovercraft

                case MOVE_STATE_CHOCOBO:
                    return 1.5f;  // Moderate increase for chocobo

                case MOVE_STATE_AIRSHIP:
                case MOVE_STATE_LOWFLYING:
                    return 1.0f;  // Airship uses different navigation system

                default:
                    return 1.0f;  // Default to baseline
            }
        }

        /// <summary>
        /// Get human-readable name for MoveState.
        /// </summary>
        public static string GetMoveStateName(int moveState)
        {
            switch (moveState)
            {
                case MOVE_STATE_WALK: return "Walking";
                case MOVE_STATE_DUSH: return "Dashing";
                case MOVE_STATE_SHIP: return "Hovercraft";
                case MOVE_STATE_AIRSHIP: return "Airship";
                case MOVE_STATE_LOWFLYING: return "Low Flying";
                case MOVE_STATE_CHOCOBO: return "Chocobo";
                case MOVE_STATE_GIMMICK: return "Gimmick";
                default: return "Unknown";
            }
        }
    }
}
