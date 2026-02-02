using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppLast.Entity.Field;
using Il2CppLast.Management;
using Il2CppLast.Map;
using MelonLoader;
using UnityEngine;

namespace FFIV_ScreenReader.Field
{
    /// <summary>
    /// Factory for creating NavigableEntity instances from game FieldEntity objects.
    /// Handles type detection and property population.
    /// </summary>
    public static class EntityFactory
    {
        /// <summary>
        /// Creates a NavigableEntity from a FieldEntity.
        /// Returns null if the entity type is not supported or not interactive.
        /// </summary>
        public static NavigableEntity CreateFromFieldEntity(FieldEntity fieldEntity, Vector3 playerPos)
        {
            if (fieldEntity == null || fieldEntity.transform == null)
                return null;

            // Skip entities with inactive GameObjects
            try
            {
                if (fieldEntity.gameObject == null || !fieldEntity.gameObject.activeInHierarchy)
                    return null;
            }
            catch
            {
                // Entity is destroyed or invalid
                return null;
            }

            // CHECK VEHICLE TYPE MAP FIRST (before ObjectType check)
            // VehicleTypeMap is populated by GetAllFieldEntities from Transportation.ModelList
            if (FieldNavigationHelper.VehicleTypeMap.TryGetValue(fieldEntity, out var vehicleInfo))
            {
                // Skip non-displayable vehicle types (these are already filtered in GetAllFieldEntities,
                // but double-check here for safety)
                if (vehicleInfo.Type > 1 && vehicleInfo.Type != 4 && vehicleInfo.Type != 5)
                {
                    var vehicle = new VehicleEntity
                    {
                        GameEntity = fieldEntity,
                        TransportationId = vehicleInfo.Type,
                        MessageId = vehicleInfo.MessageId
                    };

                    // Capture the entity's Unity layer
                    try
                    {
                        vehicle.Layer = fieldEntity.gameObject.layer;
                    }
                    catch
                    {
                        vehicle.Layer = 10;
                    }

                    return vehicle;
                }
            }

            // Get ObjectType from property
            Il2Cpp.MapConstants.ObjectType objectType = Il2Cpp.MapConstants.ObjectType.PointIn;
            if (fieldEntity.Property != null)
            {
                objectType = (Il2Cpp.MapConstants.ObjectType)fieldEntity.Property.ObjectType;
            }

            // Filter out non-interactive types
            if (IsNonInteractiveType(objectType))
                return null;

            // Create appropriate entity type based on ObjectType
            NavigableEntity entity = CreateEntityByType(fieldEntity, objectType);

            // Capture the entity's Unity layer for elevation-aware pathfinding
            if (entity != null)
            {
                try
                {
                    entity.Layer = fieldEntity.gameObject.layer;
                }
                catch
                {
                    entity.Layer = 10; // Default to GroundLayer if we can't read it
                }
            }

            return entity;
        }

        /// <summary>
        /// Creates NavigableEntity list from a collection of FieldEntity objects
        /// </summary>
        public static List<NavigableEntity> CreateFromFieldEntities(
            IEnumerable<FieldEntity> fieldEntities,
            Vector3 playerPos)
        {
            var results = new List<NavigableEntity>();

            foreach (var fieldEntity in fieldEntities)
            {
                var entity = CreateFromFieldEntity(fieldEntity, playerPos);
                if (entity != null)
                {
                    results.Add(entity);
                }
            }

            return results;
        }

        /// <summary>
        /// Normalizes a string by converting full-width ASCII characters to half-width.
        /// Japanese games often use full-width variants of Latin characters.
        /// </summary>
        private static string NormalizeToHalfWidth(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            var sb = new System.Text.StringBuilder(input.Length);
            foreach (char c in input)
            {
                // Full-width space (U+3000) -> regular space
                if (c == '\u3000')
                    sb.Append(' ');
                // Full-width ASCII (U+FF01-U+FF5E) -> half-width (U+0021-U+007E)
                else if (c >= '\uFF01' && c <= '\uFF5E')
                    sb.Append((char)(c - 0xFEE0));
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Checks if an entity name indicates a placeholder that should be filtered out.
        /// Uses normalized string comparison to handle full-width character variants.
        /// </summary>
        private static bool IsPlaceholderEntity(string entityName)
        {
            if (string.IsNullOrEmpty(entityName))
                return true;

            // Filter "Unknown" entities (no name defined)
            if (entityName == "Unknown")
                return true;

            // Normalize for pattern matching (handles full-width spaces etc.)
            string normalized = NormalizeToHalfWidth(entityName);

            // Filter vehicle spawn point defaults - check both original and normalized
            if (normalized.StartsWith("Default ") || normalized.StartsWith("Default_") ||
                entityName.StartsWith("Default ") || entityName.StartsWith("Default_"))
                return true;

            // Filter generic/placeholder prefixes (汎用 = "generic/general-purpose")
            if (entityName.StartsWith("汎用"))
                return true;

            // Filter visual effects and decorative entities (exact match)
            string[] visualEffects = new[]
            {
                "渦",             // Whirlpool
                "水飛沫",         // Water splash
            };
            foreach (var effect in visualEffects)
            {
                if (entityName == effect)
                    return true;
            }

            // Filter state indicators - these appear on overworld but have no interaction
            // Applied to ALL entity types (not just GotoMap) since they may be Events
            string[] stateIndicators = new[]
            {
                "城１",           // Castle 1 (state)
                "城２",           // Castle 2 (state)
                "壊れたお城",     // Broken castle
                "塔（地上）",     // Tower (ground)
            };
            foreach (var indicator in stateIndicators)
            {
                if (entityName == indicator)
                    return true;
            }

            // Filter collapsed state suffix (e.g., "ダムシアン城_崩壊後")
            if (entityName.EndsWith("_崩壊後"))
                return true;

            // Filter vehicle water/demo versions (decorative, not interactable)
            if (entityName.Contains("（水引版）"))  // Water-pulling version
                return true;

            // Filter vehicle spawn point names (vehicles that appear as NPC entities)
            // Note: Active vehicles are handled via VehicleTypeMap BEFORE this check,
            // so this only filters spawn point placeholders, not actual vehicles
            string[] vehicleSpawnNames = new[]
            {
                "魔導船",         // Lunar Whale spawn point
            };
            foreach (var spawnName in vehicleSpawnNames)
            {
                if (entityName == spawnName)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Checks if an ObjectType represents a non-interactive entity
        /// </summary>
        private static bool IsNonInteractiveType(Il2Cpp.MapConstants.ObjectType objectType)
        {
            // Filter out visual/effect entities, area constraints, hazards, and automatic triggers
            return objectType == Il2Cpp.MapConstants.ObjectType.PointIn ||
                   objectType == Il2Cpp.MapConstants.ObjectType.ToLayer ||  // Layer transitions work automatically with pathfinding
                   objectType == Il2Cpp.MapConstants.ObjectType.OpenTrigger ||  // Door triggers activate automatically when walked over
                   objectType == Il2Cpp.MapConstants.ObjectType.CollisionEntity ||
                   objectType == Il2Cpp.MapConstants.ObjectType.EffectEntity ||
                   objectType == Il2Cpp.MapConstants.ObjectType.ScreenEffect ||
                   objectType == Il2Cpp.MapConstants.ObjectType.TileAnimation ||
                   objectType == Il2Cpp.MapConstants.ObjectType.MoveArea ||
                   objectType == Il2Cpp.MapConstants.ObjectType.Polyline ||
                   objectType == Il2Cpp.MapConstants.ObjectType.ChangeOffset ||
                   objectType == Il2Cpp.MapConstants.ObjectType.IgnoreRoute ||
                   objectType == Il2Cpp.MapConstants.ObjectType.NonEncountArea ||
                   objectType == Il2Cpp.MapConstants.ObjectType.DamageFloorGimmickArea ||
                   objectType == Il2Cpp.MapConstants.ObjectType.SlidingFloorGimmickArea ||
                   objectType == Il2Cpp.MapConstants.ObjectType.TimeSwitchingGimmickArea;
        }

        /// <summary>
        /// Creates the appropriate NavigableEntity subclass based on ObjectType
        /// </summary>
        private static NavigableEntity CreateEntityByType(
            FieldEntity fieldEntity,
            Il2Cpp.MapConstants.ObjectType objectType)
        {
            switch (objectType)
            {
                case Il2Cpp.MapConstants.ObjectType.TreasureBox:
                    return new TreasureChestEntity { GameEntity = fieldEntity };

                case Il2Cpp.MapConstants.ObjectType.NPC:
                case Il2Cpp.MapConstants.ObjectType.ShopNPC:
                    // Filter placeholder NPCs (vehicle spawn points use NPC type)
                    string npcName = fieldEntity.Property?.Name;
                    if (IsPlaceholderEntity(npcName))
                        return null;
                    return new NPCEntity { GameEntity = fieldEntity };

                case Il2Cpp.MapConstants.ObjectType.GotoMap:
                    // Filter out placeholder GotoMap entities
                    string gotoMapName = fieldEntity.Property?.Name;
                    if (IsPlaceholderEntity(gotoMapName))
                        return null;

                    // Check for valid destination property
                    var gotoMapProp = fieldEntity.Property?.TryCast<PropertyGotoMap>();
                    if (gotoMapProp == null)
                        return null;  // No destination = placeholder

                    return new MapExitEntity { GameEntity = fieldEntity };

                case Il2Cpp.MapConstants.ObjectType.SavePoint:
                    return new SavePointEntity { GameEntity = fieldEntity };

                case Il2Cpp.MapConstants.ObjectType.OpenTrigger:
                    return new DoorTriggerEntity { GameEntity = fieldEntity };

                case Il2Cpp.MapConstants.ObjectType.TelepoPoint:
                case Il2Cpp.MapConstants.ObjectType.Event:
                case Il2Cpp.MapConstants.ObjectType.TransportationEventAction:
                    // CONSERVATIVE filter - only removes obvious placeholders
                    // TransportationEventAction is preserved unless it has a clearly placeholder name
                    // (e.g., "Default _xxx", "汎用xxx", visual effects)
                    // State indicators are NOT filtered here - they may be legitimate events
                    string eventName = fieldEntity.Property?.Name;
                    if (IsPlaceholderEntity(eventName))
                        return null;
                    return new EventEntity { GameEntity = fieldEntity };

                default:
                    // CONSERVATIVE filter for unknown types
                    string defaultName = fieldEntity.Property?.Name;
                    if (IsPlaceholderEntity(defaultName))
                        return null;
                    return new EventEntity { GameEntity = fieldEntity };
            }
        }

        /// <summary>
        /// Gets the current map ID from UserDataManager.
        /// </summary>
        /// <returns>Current map ID, or -1 if unable to determine</returns>
        private static int GetCurrentMapId()
        {
            try
            {
                var userDataManager = UserDataManager.Instance();
                return userDataManager?.CurrentMapId ?? -1;
            }
            catch
            {
                return -1;
            }
        }

    }
}
