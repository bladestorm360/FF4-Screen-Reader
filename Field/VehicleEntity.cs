using FFIV_ScreenReader.Core;
using FFIV_ScreenReader.Utils;
using UnityEngine;

namespace FFIV_ScreenReader.Field
{
    /// <summary>
    /// Represents a vehicle (chocobo, etc.)
    /// </summary>
    public class VehicleEntity : NavigableEntity
    {
        /// <summary>
        /// Transportation type ID
        /// </summary>
        public int TransportationId { get; set; }

        /// <summary>
        /// Message ID for localized vehicle name (e.g., "Falcon", "Lunar Whale")
        /// </summary>
        public string MessageId { get; set; }

        public override EntityCategory Category => EntityCategory.Vehicles;

        public override int Priority => 10;

        public override bool BlocksPathing => false;

        protected override string GetDisplayName()
        {
            return GetVehicleName(TransportationId, MessageId);
        }

        protected override string GetEntityTypeName()
        {
            return "Vehicle";
        }

        /// <summary>
        /// Gets the vehicle name, preferring the localized MessageId name if available.
        /// Falls back to generic type-based name if MessageId is not set or lookup fails.
        /// </summary>
        public static string GetVehicleName(int id, string messageId = null)
        {
            // Try MessageId first for specific name (e.g., "Falcon" vs generic "Special Airship")
            if (!string.IsNullOrEmpty(messageId))
            {
                try
                {
                    var msg = Il2CppLast.Management.MessageManager.Instance?.GetMessage(messageId);
                    if (!string.IsNullOrEmpty(msg))
                        return msg;
                }
                catch { }
            }

            // Fall back to type-based generic name
            switch (id)
            {
                case 1: return "Player";
                case 2: return "Ship";
                case 3: return "Enterprise";
                case 4: return "Symbol";
                case 5: return "Content";
                case 6: return "Submarine";
                case 7: return "Hovercraft";
                case 8: return "Special Airship";
                case 9: return "Yellow Chocobo";
                case 10: return "Black Chocobo";
                case 11: return "Boko";
                case 12: return "Magical Armor";
                default: return $"Vehicle {id}";
            }
        }
    }
}
