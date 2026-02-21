using System;
using FFIV_ScreenReader.Core;
using UnityEngine;

namespace FFIV_ScreenReader.Utils
{
    /// <summary>
    /// Centralized player position retrieval.
    /// Replaces duplicate GetPlayerPosition() implementations across EntityNavigator,
    /// EntityCache, WaypointNavigator, and GroupEntity.
    /// </summary>
    public static class PlayerPositionHelper
    {
        /// <summary>
        /// Gets the player's world position (transform.position).
        /// Used by EntityNavigator, EntityCache, GroupEntity for distance sorting.
        /// </summary>
        public static Vector3 GetWorldPosition()
        {
            var playerController = GameObjectCache.Get<Il2CppLast.Map.FieldPlayerController>();
            if (playerController?.fieldPlayer?.transform == null)
                return Vector3.zero;

            return playerController.fieldPlayer.transform.position;
        }

        /// <summary>
        /// Gets the player's local position (transform.localPosition).
        /// Used by WaypointNavigator for waypoint coordinate space.
        /// </summary>
        public static Vector3 GetLocalPosition()
        {
            var playerController = GameObjectCache.Get<Il2CppLast.Map.FieldPlayerController>();
            if (playerController?.fieldPlayer?.transform == null)
                return Vector3.zero;

            return playerController.fieldPlayer.transform.localPosition;
        }

        /// <summary>
        /// Gets cardinal/intercardinal direction from one position to another.
        /// Uses Atan2(diff.x, diff.y) to match game coordinate system where +Y is North.
        /// </summary>
        public static string GetDirection(Vector3 from, Vector3 to)
        {
            Vector3 diff = to - from;
            float angle = Mathf.Atan2(diff.x, diff.y) * Mathf.Rad2Deg;

            // Normalize to 0-360
            if (angle < 0) angle += 360;

            // Convert to cardinal/intercardinal directions
            if (angle >= 337.5 || angle < 22.5) return "North";
            else if (angle >= 22.5 && angle < 67.5) return "Northeast";
            else if (angle >= 67.5 && angle < 112.5) return "East";
            else if (angle >= 112.5 && angle < 157.5) return "Southeast";
            else if (angle >= 157.5 && angle < 202.5) return "South";
            else if (angle >= 202.5 && angle < 247.5) return "Southwest";
            else if (angle >= 247.5 && angle < 292.5) return "West";
            else if (angle >= 292.5 && angle < 337.5) return "Northwest";
            else return "Unknown";
        }

        /// <summary>
        /// Formats a distance (in world units) as a step count.
        /// One cell = 16 world units = 1 step.
        /// </summary>
        public static string FormatSteps(float distance)
        {
            float steps = distance / Constants.CellSize;
            string stepLabel = Math.Abs(steps - 1f) < 0.1f ? "step" : "steps";
            return $"{steps:F1} {stepLabel}";
        }
    }
}
