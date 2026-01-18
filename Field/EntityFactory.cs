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

            // DEBUG: Log entity scan header
            MelonLogger.Msg("=== ENTITY SCAN START ===");
            MelonLogger.Msg($"Player scan position: ({playerPos.x:F1}, {playerPos.y:F1}, {playerPos.z:F1})");

            foreach (var fieldEntity in fieldEntities)
            {
                var entity = CreateFromFieldEntity(fieldEntity, playerPos);
                if (entity != null)
                {
                    results.Add(entity);
                }
            }

            MelonLogger.Msg($"=== ENTITY SCAN END ({results.Count} entities) ===");
            return results;
        }

        /// <summary>
        /// Checks if an ObjectType represents a non-interactive entity
        /// </summary>
        private static bool IsNonInteractiveType(Il2Cpp.MapConstants.ObjectType objectType)
        {
            // Filter out visual/effect entities, area constraints, hazards
            return objectType == Il2Cpp.MapConstants.ObjectType.PointIn ||
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
                    return new NPCEntity { GameEntity = fieldEntity };

                case Il2Cpp.MapConstants.ObjectType.GotoMap:
                    // Filter out same-map teleports (internal transitions)
                    var gotoMapProp = fieldEntity.Property?.TryCast<PropertyGotoMap>();
                    if (gotoMapProp != null)
                    {
                        int destMapId = gotoMapProp.MapId;
                        int currentMapId = GetCurrentMapId();

                        // DEBUG: Log GotoMap entity positions and layer
                        try
                        {
                            Vector3 worldPos = fieldEntity.transform.position;
                            Vector3 localPos = fieldEntity.transform.localPosition;
                            int entityLayer = fieldEntity.gameObject.layer;
                            int pathfindingZ = entityLayer >= 9 ? entityLayer - 9 : 0;
                            string entityName = fieldEntity.Property?.Name ?? "Unknown";
                            MelonLogger.Msg($"[GotoMap] {entityName} -> MapId {destMapId}");
                            MelonLogger.Msg($"  WorldPos: ({worldPos.x:F1}, {worldPos.y:F1}, {worldPos.z:F1})");
                            MelonLogger.Msg($"  LocalPos: ({localPos.x:F1}, {localPos.y:F1}, {localPos.z:F1})");
                            MelonLogger.Msg($"  Layer: {entityLayer} (PathfindZ={pathfindingZ})");
                            if (worldPos != localPos)
                            {
                                Vector3 diff = worldPos - localPos;
                                MelonLogger.Msg($"  DIFF: ({diff.x:F1}, {diff.y:F1}, {diff.z:F1})");
                            }
                        }
                        catch { }

                        // Skip if destination is current map (same-map teleport)
                        if (destMapId == currentMapId && currentMapId != -1)
                            return null;
                    }
                    return new MapExitEntity { GameEntity = fieldEntity };

                case Il2Cpp.MapConstants.ObjectType.SavePoint:
                    return new SavePointEntity { GameEntity = fieldEntity };

                case Il2Cpp.MapConstants.ObjectType.OpenTrigger:
                    return new DoorTriggerEntity { GameEntity = fieldEntity };

                case Il2Cpp.MapConstants.ObjectType.TelepoPoint:
                case Il2Cpp.MapConstants.ObjectType.Event:
                case Il2Cpp.MapConstants.ObjectType.TransportationEventAction:
                default:
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
