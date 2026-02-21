using System;
using System.Collections.Generic;
using Il2Cpp;
using FFIV_ScreenReader.Core;
using FFIV_ScreenReader.Utils;
using UnityEngine;

namespace FFIV_ScreenReader.Field
{
    /// <summary>
    /// Represents an NPC entity
    /// </summary>
    public class NPCEntity : NavigableEntity
    {
        /// <summary>
        /// Asset name used by the game (e.g., "P002" for Kain)
        /// </summary>
        public string AssetName => GameEntity?.Property?.TryCast<Il2CppLast.Map.PropertyNpc>()?.AssetName ?? "";

        /// <summary>
        /// Whether this NPC is a shop
        /// </summary>
        public bool IsShop => GameEntity?.Property?.TryCast<Il2CppLast.Map.PropertyNpc>()?.ProductGroupId > 0;

        /// <summary>
        /// NPC movement behavior
        /// </summary>
        public Il2Cpp.FieldEntityConstants.MoveType MovementType =>
            GameEntity?.Property?.TryCast<Il2CppLast.Map.PropertyNpc>()?.MoveType ?? Il2Cpp.FieldEntityConstants.MoveType.None;

        /// <summary>
        /// Character name if this is a playable character NPC
        /// </summary>
        public string CharacterName => GetCharacterName(AssetName);

        public override EntityCategory Category => EntityCategory.NPCs;

        public override int Priority => 4;

        public override bool BlocksPathing => true;

        private static readonly Dictionary<string, string> characterMap = new Dictionary<string, string>
        {
            { "P001", "Cecil" },
            { "P002", "Kain" },
            { "P003", "Rosa" },
            { "P004", "Cid" },
            { "P005", "Rydia" },
            { "P006", "Tellah" },
            { "P007", "Edward" },
            { "P008", "Yang" },
            { "P009", "Palom" },
            { "P010", "Porom" },
            { "P011", "Edge" },
            { "P012", "FuSoYa" },
            { "P013", "Golbez" }
        };

        /// <summary>
        /// Gets friendly character name from asset name.
        /// Checks P-codes for playable characters, then queries NPC master data.
        /// </summary>
        public static string GetCharacterName(string assetName)
        {
            if (string.IsNullOrEmpty(assetName))
                return null;

            // Check if asset name contains a P-code
            foreach (var kvp in characterMap)
            {
                if (assetName.Contains(kvp.Key))
                {
                    return kvp.Value;
                }
            }

            // Try NPC master data
            try
            {
                var npcTemplateList = Il2CppLast.Data.Master.Npc.templateList;
                if (npcTemplateList != null && npcTemplateList.Count > 0)
                {
                    foreach (var kvp in npcTemplateList)
                    {
                        if (kvp.Value == null) continue;

                        var npcData = kvp.Value.TryCast<Il2CppLast.Data.Master.Npc>();
                        if (npcData != null &&
                            !string.IsNullOrEmpty(npcData.AssetName) &&
                            npcData.AssetName == assetName)
                        {
                            if (!string.IsNullOrEmpty(npcData.NpcName))
                            {
                                return npcData.NpcName;
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Master data not available yet
            }

            return null;
        }

        protected override string GetDisplayName()
        {
            var details = new List<string>();

            // Add character name if available (recalculate from asset name if not set)
            string characterName = CharacterName;
            if (string.IsNullOrEmpty(characterName) && !string.IsNullOrEmpty(AssetName))
            {
                characterName = GetCharacterName(AssetName);
            }

            if (!string.IsNullOrEmpty(characterName))
            {
                details.Add(characterName);
            }

            // Add shop indicator
            if (IsShop)
            {
                details.Add("shop");
            }

            // Add movement type
            if (MovementType == Il2Cpp.FieldEntityConstants.MoveType.None)
            {
                details.Add("stationary");
            }
            else if (MovementType == Il2Cpp.FieldEntityConstants.MoveType.Stamp)
            {
                details.Add("wandering");
            }
            else if (MovementType == Il2Cpp.FieldEntityConstants.MoveType.Area ||
                     MovementType == Il2Cpp.FieldEntityConstants.MoveType.Route)
            {
                details.Add("patrolling");
            }

            string detailStr = details.Count > 0 ? $" ({string.Join(", ", details)})" : "";
            return $"{Name}{detailStr}";
        }

        protected override string GetEntityTypeName()
        {
            return "NPC";
        }
    }
}
