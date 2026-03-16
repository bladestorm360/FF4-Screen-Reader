using System;
using UnityEngine;
using Il2CppLast.Map;
using FFIV_ScreenReader.Utils;
using static FFIV_ScreenReader.Utils.ModTextTranslator;
using FFIV_ScreenReader.Field;

namespace FFIV_ScreenReader.Core
{
    /// <summary>
    /// Facade for entity navigation operations.
    /// Owns entity cycling, category management, filter toggles, and teleportation.
    /// </summary>
    public class EntityNavigationFacade
    {
        private readonly EntityNavigator entityNavigator;
        private readonly NavigationStateManager navigationState;

        // Category count derived from enum for safe cycling
        private static readonly int CategoryCount = Enum.GetValues(typeof(EntityCategory)).Length;

        // Filter state
        private bool filterByPathfinding;
        private bool filterMapExits;
        private bool filterToLayer;

        // Public static accessors for filter settings (used by ModMenu)
        public static bool MapExitFilterEnabled => FFIV_ScreenReaderMod.Instance?.entityNavFacade?.filterMapExits ?? false;
        public static bool ToLayerFilterEnabled => FFIV_ScreenReaderMod.Instance?.entityNavFacade?.filterToLayer ?? false;

        public EntityNavigationFacade(EntityNavigator entityNavigator, NavigationStateManager navigationState)
        {
            this.entityNavigator = entityNavigator;
            this.navigationState = navigationState;
        }

        /// <summary>
        /// Load saved filter preferences and apply them.
        /// </summary>
        public void LoadPreferences()
        {
            filterByPathfinding = PreferencesManager.PathfindingFilterDefault;
            filterMapExits = PreferencesManager.MapExitFilterDefault;
            filterToLayer = PreferencesManager.ToLayerFilterDefault;

            entityNavigator.FilterByPathfinding = filterByPathfinding;
            entityNavigator.FilterMapExits = filterMapExits;
            entityNavigator.FilterToLayer = filterToLayer;

            if (navigationState != null)
                navigationState.FilterByPathfinding = filterByPathfinding;
        }

        /// <summary>
        /// Checks if player is on an active field map.
        /// </summary>
        private bool EnsureFieldContextAndScan()
        {
            var fieldMap = GameObjectCache.Get<Il2Cpp.FieldMap>();
            if (fieldMap == null || !fieldMap.gameObject.activeInHierarchy)
            {
                FFIV_ScreenReaderMod.SpeakText(T("Not on map"));
                return false;
            }

            var playerController = GameObjectCache.Get<FieldPlayerController>();
            if (playerController?.fieldPlayer == null)
            {
                FFIV_ScreenReaderMod.SpeakText(T("Not on map"));
                return false;
            }

            return true;
        }

        public void AnnounceCurrentEntity()
        {
            if (!EnsureFieldContextAndScan())
                return;

            var entity = entityNavigator.CurrentEntity;
            if (entity == null)
            {
                FFIV_ScreenReaderMod.SpeakText(T("No entities nearby"));
                return;
            }

            var playerController = GameObjectCache.Get<FieldPlayerController>();
            if (playerController?.fieldPlayer == null)
            {
                FFIV_ScreenReaderMod.SpeakText(T("Not on map"));
                return;
            }

            Vector3 playerPos = playerController.fieldPlayer.transform.localPosition;
            Vector3 targetPos = entity.GameEntity.transform.localPosition;

            var pathInfo = FieldNavigationHelper.FindPathTo(
                playerPos, targetPos,
                playerController.mapHandle, playerController.fieldPlayer);

            string announcement = pathInfo.Success ? $"{pathInfo.Description}" : T("no path");
            FFIV_ScreenReaderMod.SpeakText(announcement);
        }

        public void CycleNext()
        {
            if (!EnsureFieldContextAndScan())
                return;

            if (entityNavigator.CycleNext())
            {
                AnnounceEntityOnly();
            }
            else
            {
                FFIV_ScreenReaderMod.SpeakText(entityNavigator.EntityCount == 0
                    ? T("No entities nearby") : T("No pathable entities found"));
            }
        }

        public void CyclePrevious()
        {
            if (!EnsureFieldContextAndScan())
                return;

            if (entityNavigator.CyclePrevious())
            {
                AnnounceEntityOnly();
            }
            else
            {
                FFIV_ScreenReaderMod.SpeakText(entityNavigator.EntityCount == 0
                    ? T("No entities nearby") : T("No pathable entities found"));
            }
        }

        public void AnnounceEntityOnly()
        {
            if (!EnsureFieldContextAndScan())
                return;

            var entity = entityNavigator.CurrentEntity;
            if (entity == null)
            {
                FFIV_ScreenReaderMod.SpeakText(T("No entities nearby"));
                return;
            }

            var playerController = GameObjectCache.Get<FieldPlayerController>();

            Vector3 playerPos = playerController.fieldPlayer.transform.localPosition;
            Vector3 targetPos = entity.GameEntity.transform.localPosition;

            string formatted = entity.FormatDescription(playerController.fieldPlayer.transform.position);

            var pathInfo = FieldNavigationHelper.FindPathTo(
                playerPos, targetPos,
                playerController.mapHandle, playerController.fieldPlayer);

            string countSuffix = $", {string.Format(T("{0} of {1}"), entityNavigator.CurrentIndex + 1, entityNavigator.EntityCount)}";
            string pathStatus = pathInfo.Success ? "" : $", {T("no path")}";
            FFIV_ScreenReaderMod.SpeakText($"{formatted}{pathStatus}{countSuffix}");
        }

        public void CycleNextCategory()
        {
            if (!EnsureFieldContextAndScan())
                return;

            int nextCategory = ((int)entityNavigator.CurrentCategory + 1) % CategoryCount;
            entityNavigator.SetCategory((EntityCategory)nextCategory);
            AnnounceCategoryChange();
        }

        public void CyclePreviousCategory()
        {
            if (!EnsureFieldContextAndScan())
                return;

            int prevCategory = (int)entityNavigator.CurrentCategory - 1;
            if (prevCategory < 0) prevCategory = CategoryCount - 1;
            entityNavigator.SetCategory((EntityCategory)prevCategory);
            AnnounceCategoryChange();
        }

        public void ResetToAllCategory()
        {
            if (!EnsureFieldContextAndScan())
                return;

            if (entityNavigator.CurrentCategory == EntityCategory.All)
            {
                FFIV_ScreenReaderMod.SpeakText(T("Already in All category"));
                return;
            }

            entityNavigator.SetCategory(EntityCategory.All);
            AnnounceCategoryChange();
        }

        public void TogglePathfindingFilter()
        {
            if (!EnsureFieldContextAndScan())
                return;

            filterByPathfinding = !filterByPathfinding;

            entityNavigator.FilterByPathfinding = filterByPathfinding;
            navigationState.FilterByPathfinding = filterByPathfinding;

            PreferencesManager.SavePathfindingFilter(filterByPathfinding);

            FFIV_ScreenReaderMod.SpeakText(string.Format(T("Pathfinding filter {0}"), filterByPathfinding ? T("on") : T("off")));
        }

        public void ToggleMapExitFilter()
        {
            filterMapExits = !filterMapExits;

            entityNavigator.FilterMapExits = filterMapExits;
            entityNavigator.RebuildNavigationList();

            PreferencesManager.SaveMapExitFilter(filterMapExits);

            FFIV_ScreenReaderMod.SpeakText(string.Format(T("Map exit filter {0}"), filterMapExits ? T("on") : T("off")));
        }

        public void ToggleToLayerFilter()
        {
            filterToLayer = !filterToLayer;

            entityNavigator.FilterToLayer = filterToLayer;

            PreferencesManager.SaveToLayerFilter(filterToLayer);

            FFIV_ScreenReaderMod.SpeakText(string.Format(T("Layer transition filter {0}"), filterToLayer ? T("on") : T("off")));
        }

        private void AnnounceCategoryChange()
        {
            string categoryName = EntityNavigator.GetCategoryName(entityNavigator.CurrentCategory);
            int entityCount = entityNavigator.EntityCount;
            string countUnit = entityCount == 1 ? T("entity") : T("entities");
            FFIV_ScreenReaderMod.SpeakText($"{string.Format(T("Category: {0}"), categoryName)}, {entityCount} {countUnit}");
        }

        public void TeleportInDirection(Vector2 offset)
        {
            var entity = entityNavigator.CurrentEntity;
            if (entity == null)
            {
                FFIV_ScreenReaderMod.SpeakText(T("No entity selected"));
                return;
            }

            var playerController = GameObjectCache.Get<FieldPlayerController>();
            if (playerController?.fieldPlayer == null)
            {
                FFIV_ScreenReaderMod.SpeakText(T("Player not available"));
                return;
            }

            Vector3 targetPos = entity.Position;
            Vector3 newPos = new Vector3(targetPos.x + offset.x, targetPos.y + offset.y, targetPos.z);
            playerController.fieldPlayer.transform.localPosition = newPos;

            string direction = GetDirectionName(offset);
            FFIV_ScreenReaderMod.SpeakText(string.Format(T("Teleported {0} of {1}"), direction, entity.Name));
        }

        private static string GetDirectionName(Vector2 offset)
        {
            if (offset.y > 0) return T("north");
            if (offset.y < 0) return T("south");
            if (offset.x < 0) return T("west");
            if (offset.x > 0) return T("east");
            return T("unknown");
        }
    }
}
