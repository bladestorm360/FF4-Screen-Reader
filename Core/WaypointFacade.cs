using System;
using MelonLoader;
using UnityEngine;
using Il2CppLast.Map;
using FFIV_ScreenReader.Utils;
using static FFIV_ScreenReader.Utils.ModTextTranslator;
using FFIV_ScreenReader.Field;
using UserDataManager = Il2CppLast.Management.UserDataManager;

namespace FFIV_ScreenReader.Core
{
    /// <summary>
    /// Facade for waypoint navigation and management.
    /// Owns all waypoint operations: cycling, categories, pathfinding, CRUD.
    /// </summary>
    public class WaypointFacade
    {
        private readonly WaypointManager waypointManager;
        private readonly WaypointNavigator waypointNavigator;

        public WaypointFacade(WaypointManager waypointManager, WaypointNavigator waypointNavigator)
        {
            this.waypointManager = waypointManager;
            this.waypointNavigator = waypointNavigator;
        }

        /// <summary>
        /// Gets the current map ID as a string for waypoint storage.
        /// </summary>
        private string GetCurrentMapIdString()
        {
            try
            {
                var userDataManager = UserDataManager.Instance();
                if (userDataManager != null)
                    return userDataManager.CurrentMapId.ToString();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error getting map ID: {ex.Message}");
            }
            return "0";
        }

        /// <summary>
        /// Guard: checks battle state and map ID, refreshes waypoint list.
        /// Returns mapId if ready, null if blocked.
        /// </summary>
        private string EnsureWaypointContext()
        {
            if (BattleState.IsInBattle)
            {
                FFIV_ScreenReaderMod.SpeakText(T("Not available in battle"), interrupt: true);
                return null;
            }
            return GetCurrentMapIdString();
        }

        public void CycleNextWaypoint()
        {
            string mapId = EnsureWaypointContext();
            if (mapId == null) return;

            waypointNavigator.RefreshList(mapId);

            if (waypointNavigator.Count == 0)
            {
                FFIV_ScreenReaderMod.SpeakText(T("No waypoints on this map"));
                return;
            }

            waypointNavigator.CycleNext();
            FFIV_ScreenReaderMod.SpeakText(waypointNavigator.FormatCurrentWaypoint());
        }

        public void CyclePreviousWaypoint()
        {
            string mapId = EnsureWaypointContext();
            if (mapId == null) return;

            waypointNavigator.RefreshList(mapId);

            if (waypointNavigator.Count == 0)
            {
                FFIV_ScreenReaderMod.SpeakText(T("No waypoints on this map"));
                return;
            }

            waypointNavigator.CyclePrevious();
            FFIV_ScreenReaderMod.SpeakText(waypointNavigator.FormatCurrentWaypoint());
        }

        public void CycleNextWaypointCategory()
        {
            string mapId = EnsureWaypointContext();
            if (mapId == null) return;

            waypointNavigator.CycleNextCategory(mapId);
            FFIV_ScreenReaderMod.SpeakText(waypointNavigator.GetCategoryAnnouncement());
        }

        public void CyclePreviousWaypointCategory()
        {
            string mapId = EnsureWaypointContext();
            if (mapId == null) return;

            waypointNavigator.CyclePreviousCategory(mapId);
            FFIV_ScreenReaderMod.SpeakText(waypointNavigator.GetCategoryAnnouncement());
        }

        public void PathfindToCurrentWaypoint()
        {
            string mapId = EnsureWaypointContext();
            if (mapId == null) return;

            var waypoint = waypointNavigator.SelectedWaypoint;
            if (waypoint == null)
            {
                FFIV_ScreenReaderMod.SpeakText(T("No waypoint selected"));
                return;
            }

            var playerController = GameObjectCache.Get<FieldPlayerController>();
            if (playerController?.fieldPlayer == null)
            {
                FFIV_ScreenReaderMod.SpeakText(T("Not on map"));
                return;
            }

            Vector3 playerPos = playerController.fieldPlayer.transform.localPosition;
            Vector3 targetPos = waypoint.Position;

            var pathInfo = FieldNavigationHelper.FindPathTo(
                playerPos, targetPos,
                playerController.mapHandle, playerController.fieldPlayer);

            FFIV_ScreenReaderMod.SpeakText(pathInfo.Success
                ? $"{waypoint.Name}: {pathInfo.Description}"
                : $"{waypoint.Name}: {T("no path")}");
        }

        public void AddNewWaypointWithNaming()
        {
            string mapId = EnsureWaypointContext();
            if (mapId == null) return;

            var playerController = GameObjectCache.Get<FieldPlayerController>();
            if (playerController?.fieldPlayer == null)
            {
                FFIV_ScreenReaderMod.SpeakText(T("Not on map"));
                return;
            }

            TextInputWindow.Open(
                T("Enter waypoint name"),
                "",
                (name) =>
                {
                    Vector3 position = playerController.fieldPlayer.transform.localPosition;
                    waypointManager.AddWaypoint(name, position, mapId, WaypointCategory.Miscellaneous);
                    waypointNavigator.RefreshList(mapId);
                    FFIV_ScreenReaderMod.SpeakText(string.Format(T("Waypoint created: {0}"), name));
                }
            );
        }

        public void RenameCurrentWaypoint()
        {
            string mapId = EnsureWaypointContext();
            if (mapId == null) return;

            var waypoint = waypointNavigator.SelectedWaypoint;
            if (waypoint == null)
            {
                FFIV_ScreenReaderMod.SpeakText(T("No waypoint selected"));
                return;
            }

            TextInputWindow.Open(
                string.Format(T("Rename {0}"), waypoint.Name),
                waypoint.Name,
                (newName) =>
                {
                    if (waypointManager.RenameWaypoint(waypoint.WaypointId, newName))
                    {
                        waypointNavigator.RefreshList(GetCurrentMapIdString());
                        FFIV_ScreenReaderMod.SpeakText(string.Format(T("Renamed to {0}"), newName));
                    }
                    else
                    {
                        FFIV_ScreenReaderMod.SpeakText(T("Failed to rename waypoint"));
                    }
                }
            );
        }

        public void RemoveCurrentWaypoint()
        {
            string mapId = EnsureWaypointContext();
            if (mapId == null) return;

            var waypoint = waypointNavigator.SelectedWaypoint;
            if (waypoint == null)
            {
                FFIV_ScreenReaderMod.SpeakText(T("No waypoint selected"));
                return;
            }

            ConfirmationDialog.Open(
                string.Format(T("Delete {0}?"), waypoint.Name),
                () =>
                {
                    string name = waypoint.Name;
                    if (waypointManager.RemoveWaypoint(waypoint.WaypointId))
                    {
                        waypointNavigator.RefreshList(GetCurrentMapIdString());
                        waypointNavigator.ClearSelection();
                        ConfirmationDialog.CloseWithAnnouncement(string.Format(T("Deleted {0}"), name));
                    }
                    else
                    {
                        ConfirmationDialog.CloseWithAnnouncement(T("Failed to delete waypoint"));
                    }
                },
                () => ConfirmationDialog.CloseWithAnnouncement(T("Canceled"))
            );
        }

        public void ClearAllWaypointsForMap()
        {
            string mapId = EnsureWaypointContext();
            if (mapId == null) return;

            int count = waypointManager.GetWaypointCountForMap(mapId);

            if (count == 0)
            {
                FFIV_ScreenReaderMod.SpeakText(T("No waypoints on this map"));
                return;
            }

            ConfirmationDialog.Open(
                string.Format(T("Clear all {0} waypoints on this map?"), count),
                () =>
                {
                    ConfirmationDialog.Open(
                        T("Are you sure? This cannot be undone."),
                        () =>
                        {
                            int cleared = waypointManager.ClearMapWaypoints(mapId);
                            waypointNavigator.RefreshList(mapId);
                            waypointNavigator.ClearSelection();
                            ConfirmationDialog.CloseWithAnnouncement(string.Format(T("Cleared {0} waypoints"), cleared));
                        },
                        () => ConfirmationDialog.CloseWithAnnouncement(T("Canceled"))
                    );
                },
                () => ConfirmationDialog.CloseWithAnnouncement(T("Canceled"))
            );
        }
    }
}
