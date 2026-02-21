using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using MelonLoader;
using Newtonsoft.Json;
using FFIV_ScreenReader.Field;

namespace FFIV_ScreenReader.Core
{
    /// <summary>
    /// Data structure for a single waypoint (for JSON serialization)
    /// </summary>
    [Serializable]
    public class WaypointData
    {
        public string id;
        public string name;
        public string category;
        public float x;
        public float y;
        public float z;
        public string created;

        public WaypointData() { }

        public WaypointData(string id, string name, WaypointCategory category, Vector3 position)
        {
            this.id = id;
            this.name = name;
            this.category = category.ToString();
            this.x = position.x;
            this.y = position.y;
            this.z = position.z;
            this.created = DateTime.UtcNow.ToString("o");
        }

        public Vector3 GetPosition()
        {
            return new Vector3(x, y, z);
        }

        public WaypointCategory GetCategory()
        {
            if (Enum.TryParse<WaypointCategory>(category, out var result))
                return result;
            return WaypointCategory.Miscellaneous;
        }
    }

    /// <summary>
    /// Root structure for waypoints.json
    /// </summary>
    [Serializable]
    public class WaypointFileData
    {
        public int version = 1;
        public Dictionary<string, List<WaypointData>> waypoints = new Dictionary<string, List<WaypointData>>();
    }

    /// <summary>
    /// Manages waypoint CRUD operations and persistence to JSON file.
    /// Waypoints are stored in the UserData folder for easy sharing.
    /// </summary>
    public class WaypointManager
    {
        // Store waypoints in the UserData directory (alongside the game executable)
        private static readonly string WaypointFilePath = GetWaypointFilePath();

        private static string GetWaypointFilePath()
        {
            // Use the game's base directory and create UserData folder if needed
            string baseDir = System.AppDomain.CurrentDomain.BaseDirectory;
            string userDataDir = Path.Combine(baseDir, "UserData");

            // Ensure UserData directory exists
            if (!System.IO.Directory.Exists(userDataDir))
            {
                System.IO.Directory.CreateDirectory(userDataDir);
            }

            return Path.Combine(userDataDir, "waypoints.json");
        }

        private WaypointFileData fileData;
        private Dictionary<string, WaypointEntity> waypointEntities = new Dictionary<string, WaypointEntity>();

        public WaypointManager()
        {
            LoadWaypoints();
        }

        /// <summary>
        /// Loads waypoints from JSON file, creating empty structure if file doesn't exist
        /// </summary>
        public void LoadWaypoints()
        {
            try
            {
                if (File.Exists(WaypointFilePath))
                {
                    string json = File.ReadAllText(WaypointFilePath);
                    fileData = ParseWaypointJson(json);
                }
                else
                {
                    fileData = new WaypointFileData();
                }

                RebuildEntityCache();
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error loading waypoints: {ex.Message}");
                fileData = new WaypointFileData();
            }
        }

        /// <summary>
        /// Saves waypoints to JSON file
        /// </summary>
        public void SaveWaypoints()
        {
            try
            {
                string json = SerializeWaypointJson(fileData);
                File.WriteAllText(WaypointFilePath, json);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error saving waypoints: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets all waypoints for the current map
        /// </summary>
        public List<WaypointEntity> GetWaypointsForMap(string mapId)
        {
            return waypointEntities.Values
                .Where(w => w.MapId == mapId)
                .ToList();
        }

        /// <summary>
        /// Gets waypoints for a specific category on the current map
        /// </summary>
        public List<WaypointEntity> GetWaypointsForCategory(string mapId, WaypointCategory category)
        {
            if (category == WaypointCategory.All)
                return GetWaypointsForMap(mapId);

            return waypointEntities.Values
                .Where(w => w.MapId == mapId && w.WaypointCategoryType == category)
                .ToList();
        }

        /// <summary>
        /// Adds a new waypoint at the specified position
        /// </summary>
        public WaypointEntity AddWaypoint(string name, Vector3 position, string mapId, WaypointCategory category = WaypointCategory.Miscellaneous)
        {
            string id = Guid.NewGuid().ToString();
            var data = new WaypointData(id, name, category, position);

            if (!fileData.waypoints.ContainsKey(mapId))
            {
                fileData.waypoints[mapId] = new List<WaypointData>();
            }

            fileData.waypoints[mapId].Add(data);

            var entity = new WaypointEntity(id, name, position, mapId, category);
            waypointEntities[id] = entity;

            SaveWaypoints();
            return entity;
        }

        /// <summary>
        /// Removes a waypoint by ID
        /// </summary>
        public bool RemoveWaypoint(string waypointId)
        {
            if (!waypointEntities.TryGetValue(waypointId, out var entity))
                return false;

            string mapId = entity.MapId;

            if (fileData.waypoints.ContainsKey(mapId))
            {
                fileData.waypoints[mapId].RemoveAll(w => w.id == waypointId);

                // Remove empty map entries
                if (fileData.waypoints[mapId].Count == 0)
                {
                    fileData.waypoints.Remove(mapId);
                }
            }

            waypointEntities.Remove(waypointId);

            SaveWaypoints();
            return true;
        }

        /// <summary>
        /// Renames a waypoint by ID
        /// </summary>
        public bool RenameWaypoint(string waypointId, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName))
                return false;

            if (!waypointEntities.TryGetValue(waypointId, out var entity))
                return false;

            string mapId = entity.MapId;

            // Update in file data
            if (fileData.waypoints.ContainsKey(mapId))
            {
                var waypointData = fileData.waypoints[mapId].FirstOrDefault(w => w.id == waypointId);
                if (waypointData != null)
                {
                    waypointData.name = newName;
                }
            }

            // Recreate entity with new name (WaypointEntity is immutable)
            var newEntity = new WaypointEntity(
                entity.WaypointId,
                newName,
                entity.Position,
                entity.MapId,
                entity.WaypointCategoryType
            );
            waypointEntities[waypointId] = newEntity;

            SaveWaypoints();
            return true;
        }

        /// <summary>
        /// Clears all waypoints for a map. Returns the count of cleared waypoints.
        /// </summary>
        public int ClearMapWaypoints(string mapId)
        {
            int count = GetWaypointsForMap(mapId).Count;

            if (count == 0)
                return 0;

            if (fileData.waypoints.ContainsKey(mapId))
            {
                // Remove entities from cache
                var toRemove = waypointEntities.Values
                    .Where(w => w.MapId == mapId)
                    .Select(w => w.WaypointId)
                    .ToList();

                foreach (var id in toRemove)
                {
                    waypointEntities.Remove(id);
                }

                fileData.waypoints.Remove(mapId);
            }

            SaveWaypoints();
            return count;
        }

        /// <summary>
        /// Gets the count of waypoints for a specific map
        /// </summary>
        public int GetWaypointCountForMap(string mapId)
        {
            return GetWaypointsForMap(mapId).Count;
        }

        /// <summary>
        /// Gets the next auto-generated waypoint name for the map
        /// </summary>
        public string GetNextWaypointName(string mapId)
        {
            int count = GetWaypointCountForMap(mapId) + 1;
            return $"Waypoint {count}";
        }

        private void RebuildEntityCache()
        {
            waypointEntities.Clear();

            foreach (var kvp in fileData.waypoints)
            {
                string mapId = kvp.Key;
                foreach (var data in kvp.Value)
                {
                    var entity = new WaypointEntity(
                        data.id,
                        data.name,
                        data.GetPosition(),
                        mapId,
                        data.GetCategory()
                    );
                    waypointEntities[data.id] = entity;
                }
            }
        }

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented
        };

        private WaypointFileData ParseWaypointJson(string json)
        {
            try
            {
                return JsonConvert.DeserializeObject<WaypointFileData>(json, JsonSettings) ?? new WaypointFileData();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error parsing waypoint JSON: {ex.Message}");
                return new WaypointFileData();
            }
        }

        private string SerializeWaypointJson(WaypointFileData data)
        {
            return JsonConvert.SerializeObject(data, JsonSettings);
        }
    }
}
