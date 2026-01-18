using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Il2Cpp;
using Il2CppLast.Entity.Field;
using Il2CppLast.Map;
using MelonLoader;
using UnityEngine;

namespace FFIV_ScreenReader.Field
{
    public static class FieldNavigationHelper
    {
        /// <summary>
        /// Gets all FieldEntity objects currently in the world (no filtering or wrapping).
        /// Returns raw game entities from both the main entity list and transportation system.
        /// </summary>
        public static List<FieldEntity> GetAllFieldEntities()
        {
            var results = new List<FieldEntity>();

            // Get the game's master entity list from FieldController
            var fieldMap = Utils.GameObjectCache.Get<FieldMap>();
            if (fieldMap?.fieldController == null)
                return results;

            // Add all entities from main entity list
            var entityList = fieldMap.fieldController.entityList;
            if (entityList != null)
            {
                foreach (var fieldEntity in entityList)
                {
                    if (fieldEntity != null)
                    {
                        results.Add(fieldEntity);
                    }
                }
            }

            // Add transportation entities (chocobo, etc.)
            if (fieldMap.fieldController.transportation != null)
            {
                var transportationEntities = fieldMap.fieldController.transportation.NeedInteractiveList();
                if (transportationEntities != null)
                {
                    foreach (var interactiveEntity in transportationEntities)
                    {
                        if (interactiveEntity == null) continue;

                        // Try to cast to FieldEntity
                        var fieldEntity = interactiveEntity.TryCast<Il2CppLast.Entity.Field.FieldEntity>();
                        if (fieldEntity != null)
                        {
                            results.Add(fieldEntity);
                        }
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Checks which directions are walkable from the current position
        /// </summary>
        public static string GetWalkableDirections(FieldPlayer player, IMapAccessor mapHandle)
        {
            if (player == null || mapHandle == null)
                return "Cannot check directions";

            Vector3 currentPos = player.transform.position;
            float stepSize = 16f; // One cell = 16 units

            // Check cardinal directions
            var directions = new List<string>();

            // North (+Y)
            Vector3 northPos = currentPos + new Vector3(0, stepSize, 0);
            if (CheckPositionWalkable(player, northPos, mapHandle))
                directions.Add("North");

            // South (-Y)
            Vector3 southPos = currentPos + new Vector3(0, -stepSize, 0);
            if (CheckPositionWalkable(player, southPos, mapHandle))
                directions.Add("South");

            // East (+X)
            Vector3 eastPos = currentPos + new Vector3(stepSize, 0, 0);
            if (CheckPositionWalkable(player, eastPos, mapHandle))
                directions.Add("East");

            // West (-X)
            Vector3 westPos = currentPos + new Vector3(-stepSize, 0, 0);
            if (CheckPositionWalkable(player, westPos, mapHandle))
                directions.Add("West");

            if (directions.Count == 0)
                return "STUCK - No walkable directions!";

            return string.Join(", ", directions);
        }

        private static bool CheckPositionWalkable(FieldPlayer player, Vector3 position, IMapAccessor mapHandle)
        {
            try
            {
                // Use the field controller's movement validation
                var fieldMap = Utils.GameObjectCache.Get<FieldMap>();
                if (fieldMap?.fieldController != null)
                {
                    return fieldMap.fieldController.IsCanMoveToDestPosition(player, ref position);
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Finds a path from player to target using the game's pathfinding system.
        /// On the moon (MapId=3), validates with reverse-path check to detect one-way ledges.
        /// </summary>
        /// <param name="playerWorldPos">Player's world position</param>
        /// <param name="targetWorldPos">Target's world position</param>
        /// <param name="mapHandle">Map accessor for collision data</param>
        /// <param name="player">Player entity (for layer info)</param>
        /// <param name="destinationLayer">Unity layer of the destination entity (default 10 = GroundLayer)</param>
        public static PathInfo FindPathTo(Vector3 playerWorldPos, Vector3 targetWorldPos, IMapAccessor mapHandle, FieldPlayer player = null, int destinationLayer = 10)
        {
            var pathInfo = new PathInfo { Success = false };

            if (mapHandle == null)
            {
                pathInfo.ErrorMessage = "Map handle not available";
                return pathInfo;
            }

            try
            {
                // CRITICAL: Use the SAME conversion formula as the touch controller!
                // Touch controller uses WorldPositionToCellPositionXY, NOT ConvertWorldPositionToCellPosition
                int mapWidth = mapHandle.GetCollisionLayerWidth();
                int mapHeight = mapHandle.GetCollisionLayerHeight();

                // Touch controller formula: cellX = FloorToInt((mapWidth * 0.5) + (worldPos.x * 0.0625))
                Vector3 startCell = new Vector3(
                    Mathf.FloorToInt(mapWidth * 0.5f + playerWorldPos.x * 0.0625f),
                    Mathf.FloorToInt(mapHeight * 0.5f - playerWorldPos.y * 0.0625f),  // Note: MINUS for Y!
                    0
                );

                Vector3 destCell = new Vector3(
                    Mathf.FloorToInt(mapWidth * 0.5f + targetWorldPos.x * 0.0625f),
                    Mathf.FloorToInt(mapHeight * 0.5f - targetWorldPos.y * 0.0625f),
                    0
                );

                // Calculate Z coordinates for pathfinding
                // Most maps use layer-based Z: BottomLayer=9 -> Z=0, GroundLayer=10 -> Z=1, etc.
                // Moon (MapId=3) is a special case - always use Z=0 for pathfinding
                int playerLayer = player?.gameObject?.layer ?? 10;
                int currentMapId = -1;
                try
                {
                    currentMapId = Il2CppLast.Management.UserDataManager.Instance()?.CurrentMapId ?? -1;
                }
                catch { }

                // Use layer-based Z resolution for all maps
                int pathfindZ = playerLayer >= 9 ? playerLayer - 9 : 0;

                // Store layer info in path result for debugging
                pathInfo.PlayerZ = pathfindZ;
                pathInfo.DestinationZ = pathfindZ;

                // Set Z coordinates for pathfinding
                startCell.z = pathfindZ;
                destCell.z = pathfindZ;

                // DEBUG: Log comprehensive position and state data
                try
                {
                    var fieldMap = Utils.GameObjectCache.Get<FieldMap>();
                    Vector3 playerLocalPos = player?.transform?.localPosition ?? Vector3.zero;
                    Vector2 scrollOffset = Vector2.zero;
                    bool isWorldMap = false;
                    bool isMoonMap = (currentMapId == 3);

                    if (fieldMap?.fieldController != null)
                    {
                        scrollOffset = fieldMap.fieldController.MapScrollOffset;
                        try
                        {
                            var mapManager = Il2CppLast.Map.MapLoadProcessor.GetMapManager();
                            if (mapManager?.CurrentMapModel != null)
                            {
                                isWorldMap = mapManager.CurrentMapModel.IsAreaTypeWorld;
                            }
                        }
                        catch { }
                    }

                    MelonLogger.Msg("=== PATHFIND DEBUG ===");
                    MelonLogger.Msg($"MapId={currentMapId}, IsWorldMap={isWorldMap}, IsMoon={isMoonMap}");
                    MelonLogger.Msg($"MapSize: {mapWidth}x{mapHeight}");
                    MelonLogger.Msg($"ScrollOffset: ({scrollOffset.x}, {scrollOffset.y})");
                    MelonLogger.Msg($"Player WorldPos: ({playerWorldPos.x:F1}, {playerWorldPos.y:F1}, {playerWorldPos.z:F1})");
                    MelonLogger.Msg($"Player LocalPos: ({playerLocalPos.x:F1}, {playerLocalPos.y:F1}, {playerLocalPos.z:F1})");
                    MelonLogger.Msg($"Target WorldPos: ({targetWorldPos.x:F1}, {targetWorldPos.y:F1}, {targetWorldPos.z:F1})");
                    MelonLogger.Msg($"StartCell: ({startCell.x}, {startCell.y}, {startCell.z})");
                    MelonLogger.Msg($"DestCell: ({destCell.x}, {destCell.y}, {destCell.z})");
                    MelonLogger.Msg($"PlayerLayer={playerLayer}, PathfindZ={pathfindZ}, IsOnCollision={player?.IsOnCollision}");

                    // Try to get game's cell conversion for comparison
                    try
                    {
                        Vector3 gameStartCell = fieldMap.fieldController.ConvertWorldPositionToCellPosition(playerWorldPos);
                        Vector3 gameDestCell = fieldMap.fieldController.ConvertWorldPositionToCellPosition(targetWorldPos);
                        MelonLogger.Msg($"Game StartCell: ({gameStartCell.x:F1}, {gameStartCell.y:F1}, {gameStartCell.z:F1})");
                        MelonLogger.Msg($"Game DestCell: ({gameDestCell.x:F1}, {gameDestCell.y:F1}, {gameDestCell.z:F1})");
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Msg($"Game cell conversion failed: {ex.Message}");
                    }
                    MelonLogger.Msg("======================");
                }
                catch (Exception ex)
                {
                    MelonLogger.Msg($"Debug logging error: {ex.Message}");
                }

                Il2CppSystem.Collections.Generic.List<Vector3> pathPoints = null;

                if (player != null)
                {
                    // Use player's actual collision state for pathfinding
                    bool useCollision = player.IsOnCollision;

                    // Pathfind on the SAME layer (elevation check already passed above)
                    pathPoints = Il2Cpp.MapRouteSearcher.Search(mapHandle, startCell, destCell, useCollision);

                    // If direct path failed, try adjacent tiles (still on same layer)
                    if (pathPoints == null || pathPoints.Count == 0)
                    {
                        Vector3[] adjacentOffsets = new Vector3[] {
                            new Vector3(0, 16, 0),    // north
                            new Vector3(16, 0, 0),    // east
                            new Vector3(0, -16, 0),   // south
                            new Vector3(-16, 0, 0),   // west
                            new Vector3(16, 16, 0),   // northeast
                            new Vector3(16, -16, 0),  // southeast
                            new Vector3(-16, -16, 0), // southwest
                            new Vector3(-16, 16, 0)   // northwest
                        };

                        foreach (var offset in adjacentOffsets)
                        {
                            Vector3 adjacentTargetWorld = targetWorldPos + offset;
                            Vector3 adjacentDestCell = new Vector3(
                                Mathf.FloorToInt(mapWidth * 0.5f + adjacentTargetWorld.x * 0.0625f),
                                Mathf.FloorToInt(mapHeight * 0.5f - adjacentTargetWorld.y * 0.0625f),
                                pathfindZ  // Use same Z as pathfinding
                            );

                            pathPoints = Il2Cpp.MapRouteSearcher.Search(mapHandle, startCell, adjacentDestCell, useCollision);

                            if (pathPoints != null && pathPoints.Count > 0)
                                break;
                        }
                    }
                }
                else
                {
                    pathPoints = Il2Cpp.MapRouteSearcher.SearchSimple(mapHandle, startCell, destCell);
                }

                if (pathPoints == null || pathPoints.Count == 0)
                {
                    return pathInfo;
                }

                // Search returns world coordinates (verified from logs)
                pathInfo.WorldPath = new List<Vector3>();

                for (int i = 0; i < pathPoints.Count; i++)
                {
                    pathInfo.WorldPath.Add(pathPoints[i]);
                }

                // Moon-specific reverse-path validation: detect one-way ledges
                // If we can path A→B but NOT B→A, the path crosses a ledge we can't climb back up
                if (currentMapId == 3 && player != null)
                {
                    MelonLogger.Msg($"[MoonPath] Testing reverse path from dest to player...");

                    // Try pathfinding in reverse (from destination to player)
                    bool useCollision = player.IsOnCollision;
                    var reversePathPoints = Il2Cpp.MapRouteSearcher.Search(mapHandle, destCell, startCell, useCollision);

                    if (reversePathPoints == null || reversePathPoints.Count == 0)
                    {
                        MelonLogger.Msg($"[MoonPath] REJECTED: Reverse path failed - one-way ledge detected");
                        pathInfo.ErrorMessage = "Path crosses one-way ledge";
                        pathInfo.StepCount = 0;
                        pathInfo.Description = "Path blocked by ledge";
                        return pathInfo;
                    }

                    MelonLogger.Msg($"[MoonPath] Reverse path OK ({reversePathPoints.Count} points) - bidirectional path confirmed");
                }

                pathInfo.Success = true;
                // StepCount is number of moves (points - 1, since first point is starting position)
                pathInfo.StepCount = pathPoints.Count > 0 ? pathPoints.Count - 1 : 0;
                pathInfo.Description = DescribePath(pathInfo.WorldPath);

                return pathInfo;
            }
            catch (System.Exception ex)
            {
                pathInfo.ErrorMessage = $"Pathfinding error: {ex.Message}";
                return pathInfo;
            }
        }

        /// <summary>
        /// Describes a path in simple direction terms (e.g., "North 5, East 3")
        /// Takes world coordinates as input.
        /// </summary>
        private static string DescribePath(List<Vector3> worldPath)
        {
            if (worldPath == null || worldPath.Count < 2)
                return "No movement needed";

            var segments = new List<string>();
            Vector3 currentDir = Vector3.zero;
            int stepCount = 0;

            for (int i = 1; i < worldPath.Count; i++)
            {
                Vector3 dir = worldPath[i] - worldPath[i - 1];
                dir.Normalize();

                // Same direction, increment counter
                if (Vector3.Distance(dir, currentDir) < 0.1f)
                {
                    stepCount++;
                }
                else
                {
                    // Direction changed, save previous segment
                    if (stepCount > 0)
                    {
                        string dirName = GetCardinalDirectionName(currentDir);
                        segments.Add($"{dirName} {stepCount}");
                    }

                    currentDir = dir;
                    stepCount = 1;
                }
            }

            // Add final segment
            if (stepCount > 0)
            {
                string dirName = GetCardinalDirectionName(currentDir);
                segments.Add($"{dirName} {stepCount}");
            }

            return string.Join(", ", segments);
        }

        /// <summary>
        /// Gets direction name from a normalized direction vector (supports 8 directions)
        /// </summary>
        private static string GetCardinalDirectionName(Vector3 dir)
        {
            // Handle diagonals first (when both X and Y components are significant)
            // A normalized diagonal has components around ±0.707
            if (Mathf.Abs(dir.x) > 0.4f && Mathf.Abs(dir.y) > 0.4f)
            {
                if (dir.y > 0 && dir.x > 0) return "Northeast";
                if (dir.y > 0 && dir.x < 0) return "Northwest";
                if (dir.y < 0 && dir.x > 0) return "Southeast";
                if (dir.y < 0 && dir.x < 0) return "Southwest";
            }

            // Cardinal directions (when primarily on one axis)
            if (Mathf.Abs(dir.y) > Mathf.Abs(dir.x))
            {
                return dir.y > 0 ? "North" : "South";
            }
            else if (Mathf.Abs(dir.x) > 0.1f)  // Avoid "Unknown" for tiny movements
            {
                return dir.x > 0 ? "East" : "West";
            }

            return "Unknown";
        }

        /// <summary>
        /// Gets cardinal direction from player to target (uses WORLD coordinates)
        /// </summary>
        private static string GetDirection(Vector3 from, Vector3 to)
        {
            Vector3 diff = to - from;
            float angle = Mathf.Atan2(diff.x, diff.y) * Mathf.Rad2Deg;

            // Normalize to 0-360
            if (angle < 0) angle += 360;

            // Convert to cardinal/intercardinal directions
            string result;
            if (angle >= 337.5 || angle < 22.5) result = "North";
            else if (angle >= 22.5 && angle < 67.5) result = "Northeast";
            else if (angle >= 67.5 && angle < 112.5) result = "East";
            else if (angle >= 112.5 && angle < 157.5) result = "Southeast";
            else if (angle >= 157.5 && angle < 202.5) result = "South";
            else if (angle >= 202.5 && angle < 247.5) result = "Southwest";
            else if (angle >= 247.5 && angle < 292.5) result = "West";
            else if (angle >= 292.5 && angle < 337.5) result = "Northwest";
            else result = "Unknown";

            return result;
        }

    }

    /// <summary>
    /// Information about a pathfinding result
    /// </summary>
    public class PathInfo
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public int StepCount { get; set; }
        public string Description { get; set; }
        public System.Collections.Generic.List<Vector3> WorldPath { get; set; }

        /// <summary>
        /// True if the destination is at a different elevation than the player.
        /// This means there's no walkable path due to ledge/elevation barriers.
        /// </summary>
        public bool ElevationMismatch { get; set; }

        /// <summary>
        /// Player's pathfinding Z coordinate (layer - 9)
        /// </summary>
        public int PlayerZ { get; set; }

        /// <summary>
        /// Destination's pathfinding Z coordinate (layer - 9)
        /// </summary>
        public int DestinationZ { get; set; }
    }
}
