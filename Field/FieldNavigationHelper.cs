using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Il2Cpp;
using Il2CppLast.Entity.Field;
using Il2CppLast.Map;
using MelonLoader;
using UnityEngine;
using FieldPlayerController = Il2CppLast.Map.FieldPlayerController;
using MapRouteSearcher = Il2Cpp.MapRouteSearcher;

namespace FFIV_ScreenReader.Field
{
    public static class FieldNavigationHelper
    {
        /// <summary>
        /// Maps FieldEntity to its TransportationType and MessageId for vehicle name resolution.
        /// Populated when scanning Transportation.ModelList via pointer offsets.
        /// Tuple: (Type = TransportationType, MessageId = localized name key)
        /// </summary>
        public static Dictionary<FieldEntity, (int Type, string MessageId)> VehicleTypeMap { get; }
            = new Dictionary<FieldEntity, (int, string)>();

        // Debug logging flag - logs transportation info once per map until reset
        private static bool hasLoggedTransportation = false;

        /// <summary>
        /// Resets transportation debug logging and clears VehicleTypeMap.
        /// Call on map change to prevent stale vehicle entries.
        /// </summary>
        public static void ResetVehicleTypeMap()
        {
            hasLoggedTransportation = false;
            VehicleTypeMap.Clear();
            MelonLogger.Msg("[Vehicle Debug] VehicleTypeMap cleared");
        }

        /// <summary>
        /// Gets all FieldEntity objects currently in the world (no filtering or wrapping).
        /// Returns raw game entities from both the main entity list and transportation system.
        /// Also populates VehicleTypeMap for vehicle name resolution.
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

            // Check if we should log transportation debug (once per map until reset)
            bool shouldLogTransport = !hasLoggedTransportation;

            // Add transportation entities and populate VehicleTypeMap
            if (fieldMap.fieldController.transportation != null)
            {
                var transportation = fieldMap.fieldController.transportation;

                if (shouldLogTransport)
                {
                    MelonLogger.Msg($"[Vehicle Debug] Transportation controller exists, checking for vehicles...");
                }

                // Method 1: NeedInteractiveList - returns dynamic vehicle entities
                try
                {
                    var transportationEntities = transportation.NeedInteractiveList();
                    if (shouldLogTransport)
                    {
                        MelonLogger.Msg($"[Vehicle Debug] NeedInteractiveList returned: {(transportationEntities != null ? transportationEntities.Count.ToString() : "null")} items");
                    }

                    if (transportationEntities != null)
                    {
                        foreach (var interactiveEntity in transportationEntities)
                        {
                            if (interactiveEntity == null) continue;

                            if (shouldLogTransport)
                            {
                                MelonLogger.Msg($"[Vehicle Debug] NeedInteractiveList item: {interactiveEntity.GetType().Name}");
                            }

                            var fieldEntity = interactiveEntity.TryCast<FieldEntity>();
                            if (fieldEntity != null && !results.Contains(fieldEntity))
                            {
                                if (shouldLogTransport)
                                {
                                    MelonLogger.Msg($"[Vehicle Debug] -> TryCast<FieldEntity> succeeded: {fieldEntity.GetType().Name}");
                                }
                                results.Add(fieldEntity);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (shouldLogTransport)
                    {
                        MelonLogger.Msg($"[Vehicle Debug] NeedInteractiveList exception: {ex.Message}");
                    }
                }

                // Method 2: Access Transportation.ModelList dictionary via pointer offsets
                // TransportationController.infoData (Transportation) at offset 0x18
                // Transportation.modelList (Dictionary<int, TransportationInfo>) at offset 0x18
                try
                {
                    unsafe
                    {
                        IntPtr transportControllerPtr = transportation.Pointer;
                        if (transportControllerPtr == IntPtr.Zero)
                        {
                            if (shouldLogTransport) MelonLogger.Msg($"[Vehicle Debug] TransportationController pointer is null");
                        }
                        else
                        {
                            // Get infoData (Transportation) at offset 0x18
                            IntPtr infoDataPtr = *(IntPtr*)((byte*)transportControllerPtr + 0x18);
                            if (shouldLogTransport) MelonLogger.Msg($"[Vehicle Debug] infoData pointer: 0x{infoDataPtr.ToInt64():X}");

                            if (infoDataPtr != IntPtr.Zero)
                            {
                                // Get modelList (Dictionary) at offset 0x18 in Transportation
                                IntPtr modelListPtr = *(IntPtr*)((byte*)infoDataPtr + 0x18);
                                if (shouldLogTransport) MelonLogger.Msg($"[Vehicle Debug] modelList pointer: 0x{modelListPtr.ToInt64():X}");

                                if (modelListPtr != IntPtr.Zero)
                                {
                                    // Try to cast to Dictionary and iterate
                                    var modelListObj = new Il2CppSystem.Object(modelListPtr);
                                    var modelDict = modelListObj.TryCast<Il2CppSystem.Collections.Generic.Dictionary<int, TransportationInfo>>();

                                    if (modelDict != null)
                                    {
                                        if (shouldLogTransport) MelonLogger.Msg($"[Vehicle Debug] ModelList dictionary count: {modelDict.Count}");

                                        foreach (var kvp in modelDict)
                                        {
                                            int transportId = kvp.Key;
                                            var transportInfo = kvp.Value;

                                            if (transportInfo == null) continue;

                                            bool enabled = transportInfo.Enable;
                                            int transportType = transportInfo.Type;

                                            // Get MessageId for specific vehicle name (e.g., "Falcon", "Lunar Whale")
                                            string messageId = transportInfo.MessageId ?? "";

                                            if (shouldLogTransport)
                                            {
                                                MelonLogger.Msg($"[Vehicle Debug] Transport ID={transportId}, Type={transportType}, Enable={enabled}, MessageId={messageId}");
                                            }

                                            // Skip non-vehicle types and disabled vehicles
                                            // Type 0 = None, Type 1 = Player, Type 4 = Symbol, Type 5 = Content (internal markers)
                                            if (transportType == 0 || transportType == 1 || transportType == 4 || transportType == 5 || !enabled) continue;

                                            var mapObject = transportInfo.MapObject;
                                            if (mapObject != null)
                                            {
                                                string goName = "";
                                                try { goName = mapObject.gameObject?.name ?? ""; } catch { }

                                                if (shouldLogTransport)
                                                {
                                                    MelonLogger.Msg($"[Vehicle Debug] -> MapObject: {mapObject.GetType().Name}, GO: {goName}");
                                                }

                                                if (!results.Contains(mapObject))
                                                {
                                                    results.Add(mapObject);
                                                }

                                                // Store the transport type and messageId for EntityFactory to use
                                                VehicleTypeMap[mapObject] = (transportType, messageId);
                                                if (shouldLogTransport)
                                                {
                                                    MelonLogger.Msg($"[Vehicle Debug] -> Added vehicle to results and VehicleTypeMap (Type={transportType}, MessageId={messageId})");
                                                }
                                            }
                                            else if (shouldLogTransport)
                                            {
                                                MelonLogger.Msg($"[Vehicle Debug] -> MapObject is null for Transport ID={transportId}");
                                            }
                                        }
                                    }
                                    else if (shouldLogTransport)
                                    {
                                        MelonLogger.Msg($"[Vehicle Debug] ModelList TryCast failed");
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (shouldLogTransport)
                    {
                        MelonLogger.Msg($"[Vehicle Debug] ModelList access exception: {ex.Message}");
                    }
                }

                hasLoggedTransportation = true;
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
                catch (Exception) { }

                // Use layer-based Z resolution for all maps
                int pathfindZ = playerLayer >= 9 ? playerLayer - 9 : 0;

                // Store layer info in path result for debugging
                pathInfo.PlayerZ = pathfindZ;
                pathInfo.DestinationZ = pathfindZ;

                // Set Z coordinates for pathfinding
                startCell.z = pathfindZ;
                destCell.z = pathfindZ;

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
                    // Try pathfinding in reverse (from destination to player)
                    bool useCollision = player.IsOnCollision;
                    var reversePathPoints = Il2Cpp.MapRouteSearcher.Search(mapHandle, destCell, startCell, useCollision);

                    if (reversePathPoints == null || reversePathPoints.Count == 0)
                    {
                        pathInfo.ErrorMessage = "Path crosses one-way ledge";
                        pathInfo.StepCount = 0;
                        pathInfo.Description = "Path blocked by ledge";
                        return pathInfo;
                    }
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
        /// Result structure for wall proximity detection.
        /// Distance values: -1 = no wall within range, 0 = adjacent/blocked
        /// </summary>
        public struct WallDistances
        {
            public int NorthDist;
            public int SouthDist;
            public int EastDist;
            public int WestDist;

            public WallDistances(int north, int south, int east, int west)
            {
                NorthDist = north;
                SouthDist = south;
                EastDist = east;
                WestDist = west;
            }
        }

        /// <summary>
        /// Gets distance to nearest wall in each cardinal direction (in tiles).
        /// Returns -1 for a direction if no wall adjacent (1 tile away).
        /// </summary>
        public static WallDistances GetNearbyWallsWithDistance(FieldPlayer player)
        {
            if (player == null || player.transform == null)
                return new WallDistances(-1, -1, -1, -1);

            Vector3 pos = player.transform.localPosition;

            return new WallDistances(
                GetWallDistance(player, pos, new Vector3(0, 16, 0)),
                GetWallDistance(player, pos, new Vector3(0, -16, 0)),
                GetWallDistance(player, pos, new Vector3(16, 0, 0)),
                GetWallDistance(player, pos, new Vector3(-16, 0, 0))
            );
        }

        /// <summary>
        /// Gets the distance to a wall in a given direction using pathfinding.
        /// Returns: 0 = adjacent/blocked, -1 = no wall adjacent
        /// </summary>
        private static int GetWallDistance(FieldPlayer player, Vector3 pos, Vector3 step)
        {
            if (IsAdjacentTileBlocked(player, pos, step))
                return 0;
            return -1;
        }

        /// <summary>
        /// Checks if an adjacent tile is blocked using pathfinding.
        /// More reliable than IsCanMoveToDestPosition for predictive checks.
        /// </summary>
        private static bool IsAdjacentTileBlocked(FieldPlayer player, Vector3 playerPos, Vector3 direction)
        {
            try
            {
                var playerController = Utils.GameObjectCache.Get<FieldPlayerController>();
                if (playerController == null)
                {
                    // Cache miss - try refresh (FindObjectOfType)
                    playerController = Utils.GameObjectCache.Refresh<FieldPlayerController>();
                    if (playerController == null)
                        return false;
                }

                var mapHandle = playerController.mapHandle;
                if (mapHandle == null)
                    return false;

                int mapWidth = mapHandle.GetCollisionLayerWidth();
                int mapHeight = mapHandle.GetCollisionLayerHeight();

                if (mapWidth <= 0 || mapHeight <= 0 || mapWidth > 10000 || mapHeight > 10000)
                    return false;

                int pathfindZ = player.gameObject.layer >= 9 ? player.gameObject.layer - 9 : 0;

                Vector3 startCell = new Vector3(
                    Mathf.FloorToInt(mapWidth * 0.5f + playerPos.x * 0.0625f),
                    Mathf.FloorToInt(mapHeight * 0.5f - playerPos.y * 0.0625f),
                    pathfindZ
                );

                Vector3 targetPos = playerPos + direction;
                Vector3 destCell = new Vector3(
                    Mathf.FloorToInt(mapWidth * 0.5f + targetPos.x * 0.0625f),
                    Mathf.FloorToInt(mapHeight * 0.5f - targetPos.y * 0.0625f),
                    pathfindZ
                );

                bool useCollision = player.IsOnCollision;
                var pathPoints = MapRouteSearcher.Search(mapHandle, startCell, destCell, useCollision);
                return pathPoints == null || pathPoints.Count == 0;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[WallTones] IsAdjacentTileBlocked error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Checks if a direction from the player position leads to a map exit entity.
        /// Used to suppress wall tones at map exits, doors, and stairs where
        /// MapRouteSearcher.Search() reports blocked but the tile is actually accessible.
        /// </summary>
        public static bool IsDirectionNearMapExit(Vector3 playerPos, Vector3 direction,
            List<Vector3> mapExitPositions, float tolerance = 12.0f)
        {
            if (mapExitPositions == null || mapExitPositions.Count == 0)
                return false;

            Vector3 adjacentTilePos = playerPos + direction;

            foreach (var exitPos in mapExitPositions)
            {
                float dx = adjacentTilePos.x - exitPos.x;
                float dy = adjacentTilePos.y - exitPos.y;
                float dist2D = Mathf.Sqrt(dx * dx + dy * dy);

                if (dist2D <= tolerance)
                    return true;
            }

            return false;
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
