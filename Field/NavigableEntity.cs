using Il2Cpp;
using Il2CppLast.Entity.Field;
using UnityEngine;
using FFIV_ScreenReader.Core;
using FFIV_ScreenReader.Utils;

namespace FFIV_ScreenReader.Field
{
    /// <summary>
    /// Base class for all navigable entities on the field map.
    /// Provides common properties and behavior for entity navigation and pathfinding.
    /// </summary>
    public abstract class NavigableEntity
    {
        /// <summary>
        /// Reference to the underlying game entity
        /// </summary>
        public virtual FieldEntity GameEntity { get; set; }

        /// <summary>
        /// Current position in world coordinates
        /// </summary>
        public virtual Vector3 Position => GameEntity?.transform?.position ?? Vector3.zero;

        /// <summary>
        /// Unity layer this entity is on (BottomLayer=9, GroundLayer=10, UpperLayer=11, CeilLayer=12)
        /// Used for elevation-aware pathfinding on multi-level maps like the moon surface.
        /// </summary>
        public int Layer { get; set; } = 10; // Default to GroundLayer

        /// <summary>
        /// Gets the Z coordinate for pathfinding based on the entity's Unity layer.
        /// BottomLayer(9)=0, GroundLayer(10)=1, UpperLayer(11)=2, CeilLayer(12)=3
        /// </summary>
        public int PathfindingZ => Layer >= 9 ? Layer - 9 : 0;

        /// <summary>
        /// Entity name (localized if available)
        /// </summary>
        public virtual string Name
        {
            get
            {
                string rawName = GameEntity?.Property?.Name ?? "Unknown";
                return Utils.EntityTranslator.Translate(rawName);
            }
        }

        /// <summary>
        /// Category for filtering purposes
        /// </summary>
        public abstract EntityCategory Category { get; }

        /// <summary>
        /// Priority for deduplication (lower = more important)
        /// </summary>
        public abstract int Priority { get; }

        /// <summary>
        /// Whether this entity blocks pathfinding movement
        /// </summary>
        public abstract bool BlocksPathing { get; }

        /// <summary>
        /// Whether this entity is currently interactive
        /// </summary>
        public virtual bool IsInteractive => true;

        /// <summary>
        /// Gets the display name for this entity (without distance/direction)
        /// </summary>
        protected abstract string GetDisplayName();

        /// <summary>
        /// Gets the entity type name for this entity (e.g., "Treasure Chest", "NPC")
        /// </summary>
        protected abstract string GetEntityTypeName();

        /// <summary>
        /// Public accessor for entity type name (used by GroupEntity delegation).
        /// </summary>
        public string EntityTypeName => GetEntityTypeName();

        /// <summary>
        /// Formats this entity for screen reader announcement
        /// </summary>
        public virtual string FormatDescription(Vector3 playerPos)
        {
            float distance = Vector3.Distance(playerPos, Position);
            string direction = PlayerPositionHelper.GetDirection(playerPos, Position);
            return $"{GetDisplayName()} ({GetEntityTypeName()}) ({PlayerPositionHelper.FormatSteps(distance)} {direction})";
        }
    }

    /// <summary>
    /// Represents a treasure chest entity
    /// </summary>
    public class TreasureChestEntity : NavigableEntity
    {
        /// <summary>
        /// Whether this treasure chest has been opened
        /// </summary>
        public bool IsOpened => GameEntity?.TryCast<FieldTresureBox>()?.isOpen ?? false;

        public override EntityCategory Category => EntityCategory.Chests;

        public override int Priority => 3;

        public override bool BlocksPathing => true;

        /// <summary>
        /// Opened chests are not interactive
        /// </summary>
        public override bool IsInteractive => !IsOpened;

        protected override string GetDisplayName()
        {
            string status = IsOpened ? "Opened" : "Unopened";
            return $"{status} {GetEntityTypeName()}";
        }

        protected override string GetEntityTypeName()
        {
            return "Treasure Chest";
        }

        public override string FormatDescription(Vector3 playerPos)
        {
            float distance = Vector3.Distance(playerPos, Position);
            string direction = PlayerPositionHelper.GetDirection(playerPos, Position);
            string status = IsOpened ? "Opened" : "Unopened";
            return $"{status} {GetEntityTypeName()} ({PlayerPositionHelper.FormatSteps(distance)} {direction})";
        }
    }

    /// <summary>
    /// Represents a map exit/transition
    /// </summary>
    public class MapExitEntity : NavigableEntity
    {
        /// <summary>
        /// Destination map ID
        /// </summary>
        public int DestinationMapId => GameEntity?.Property?.TryCast<Il2CppLast.Map.PropertyGotoMap>()?.MapId ?? -1;

        /// <summary>
        /// Friendly name of destination map
        /// </summary>
        public string DestinationName => MapNameResolver.GetMapExitName(GameEntity?.Property?.TryCast<Il2CppLast.Map.PropertyGotoMap>());

        public override EntityCategory Category => EntityCategory.MapExits;

        public override int Priority => 1;

        public override bool BlocksPathing => true;

        protected override string GetDisplayName()
        {
            // Build enhanced name with destination
            return !string.IsNullOrEmpty(DestinationName)
                ? $"{Name} → {DestinationName}"
                : Name;
        }

        protected override string GetEntityTypeName()
        {
            return "Map Exit";
        }
    }

    /// <summary>
    /// Represents a save point
    /// </summary>
    public class SavePointEntity : NavigableEntity
    {
        public override EntityCategory Category => EntityCategory.Events;

        public override int Priority => 2;

        public override bool BlocksPathing => false;

        protected override string GetDisplayName()
        {
            return Name;
        }

        protected override string GetEntityTypeName()
        {
            return "Save Point";
        }

        public override string FormatDescription(Vector3 playerPos)
        {
            float distance = Vector3.Distance(playerPos, Position);
            string direction = PlayerPositionHelper.GetDirection(playerPos, Position);
            return $"Save Point ({PlayerPositionHelper.FormatSteps(distance)} {direction})";
        }
    }

    /// <summary>
    /// Represents a door or trigger
    /// </summary>
    public class DoorTriggerEntity : NavigableEntity
    {
        public override EntityCategory Category => EntityCategory.Events;

        public override int Priority => 6;

        public override bool BlocksPathing => false;

        protected override string GetDisplayName()
        {
            return Name;
        }

        protected override string GetEntityTypeName()
        {
            return "Door/Trigger";
        }
    }

    /// <summary>
    /// Represents a generic event (teleport, switch event, random event, etc.)
    /// </summary>
    public class EventEntity : NavigableEntity
    {
        /// <summary>
        /// Specific event type
        /// </summary>
        public Il2Cpp.MapConstants.ObjectType EventType =>
            GameEntity?.Property != null
                ? (Il2Cpp.MapConstants.ObjectType)GameEntity.Property.ObjectType
                : Il2Cpp.MapConstants.ObjectType.PointIn;

        public override EntityCategory Category => EntityCategory.Events;

        public override int Priority => 8;

        public override bool BlocksPathing => EventType == Il2Cpp.MapConstants.ObjectType.TelepoPoint;

        protected override string GetDisplayName()
        {
            return Name;
        }

        protected override string GetEntityTypeName()
        {
            return GetEventTypeNameStatic(EventType);
        }

        public static string GetEventTypeNameStatic(Il2Cpp.MapConstants.ObjectType type)
        {
            switch (type)
            {
                case Il2Cpp.MapConstants.ObjectType.TelepoPoint:
                    return "Teleport";
                case Il2Cpp.MapConstants.ObjectType.Event:
                    return "Event";
                default:
                    return type.ToString();
            }
        }
    }

}
