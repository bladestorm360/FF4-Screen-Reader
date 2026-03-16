using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using MelonLoader;
using FFIV_ScreenReader.Utils;
using static FFIV_ScreenReader.Utils.ModTextTranslator;
using FFIV_ScreenReader.Menus;
using FFIV_ScreenReader.Patches;
using ConfigActualDetailsControllerBase_KeyInput = Il2CppLast.UI.KeyInput.ConfigActualDetailsControllerBase;

namespace FFIV_ScreenReader.Core
{
    /// <summary>
    /// Manages all keyboard input handling for the screen reader mod.
    /// Uses KeyBindingRegistry for declarative, context-aware dispatch.
    /// </summary>
    public class InputManager
    {
        private readonly FFIV_ScreenReaderMod mod;
        private readonly EntityNavigationFacade entityNav;
        private readonly WaypointFacade waypoints;
        private readonly KeyBindingRegistry registry = new KeyBindingRegistry();

        public InputManager(FFIV_ScreenReaderMod mod)
        {
            this.mod = mod;
            this.entityNav = mod.entityNavFacade;
            this.waypoints = mod.waypointFacade;
            InitializeBindings();
        }

        private void RegisterFieldWithBattleFeedback(KeyCode key, KeyModifier modifier, Action action, string description)
        {
            registry.Register(key, modifier, KeyContext.Field, action, description);
            registry.Register(key, modifier, KeyContext.Battle, NotAvailableInBattle, description + " (battle blocked)");
        }

        private static void NotAvailableInBattle()
        {
            FFIV_ScreenReaderMod.SpeakText(T("Not available in battle"), interrupt: true);
        }

        private void InitializeBindings()
        {
            // --- Status screen: navigation ---
            registry.Register(KeyCode.UpArrow, KeyContext.Status, StatusNavigationReader.NavigatePrevious, "Previous stat");
            registry.Register(KeyCode.DownArrow, KeyContext.Status, StatusNavigationReader.NavigateNext, "Next stat");
            registry.Register(KeyCode.UpArrow, KeyModifier.Shift, KeyContext.Status, StatusNavigationReader.JumpToPreviousGroup, "Jump to previous stat group");
            registry.Register(KeyCode.DownArrow, KeyModifier.Shift, KeyContext.Status, StatusNavigationReader.JumpToNextGroup, "Jump to next stat group");
            registry.Register(KeyCode.UpArrow, KeyModifier.Ctrl, KeyContext.Status, StatusNavigationReader.JumpToTop, "Jump to first stat");
            registry.Register(KeyCode.DownArrow, KeyModifier.Ctrl, KeyContext.Status, StatusNavigationReader.JumpToBottom, "Jump to last stat");

            // --- Field: entity navigation (brackets + backslash) — with battle feedback ---
            RegisterFieldWithBattleFeedback(KeyCode.LeftBracket, KeyModifier.Shift, entityNav.CyclePreviousCategory, "Previous entity category");
            RegisterFieldWithBattleFeedback(KeyCode.LeftBracket, KeyModifier.None, entityNav.CyclePrevious, "Previous entity");
            RegisterFieldWithBattleFeedback(KeyCode.RightBracket, KeyModifier.Shift, entityNav.CycleNextCategory, "Next entity category");
            RegisterFieldWithBattleFeedback(KeyCode.RightBracket, KeyModifier.None, entityNav.CycleNext, "Next entity");
            RegisterFieldWithBattleFeedback(KeyCode.Backslash, KeyModifier.Ctrl, entityNav.ToggleToLayerFilter, "Toggle layer filter");
            RegisterFieldWithBattleFeedback(KeyCode.Backslash, KeyModifier.Shift, entityNav.TogglePathfindingFilter, "Toggle pathfinding filter");
            RegisterFieldWithBattleFeedback(KeyCode.Backslash, KeyModifier.None, entityNav.AnnounceCurrentEntity, "Announce current entity");

            // --- Field: alternate keys (J/K/L/P) — with battle feedback ---
            RegisterFieldWithBattleFeedback(KeyCode.J, KeyModifier.Shift, entityNav.CyclePreviousCategory, "Previous entity category (alt)");
            RegisterFieldWithBattleFeedback(KeyCode.J, KeyModifier.None, entityNav.CyclePrevious, "Previous entity (alt)");
            RegisterFieldWithBattleFeedback(KeyCode.K, KeyModifier.None, entityNav.AnnounceEntityOnly, "Announce entity name (alt)");
            RegisterFieldWithBattleFeedback(KeyCode.L, KeyModifier.Shift, entityNav.CycleNextCategory, "Next entity category (alt)");
            RegisterFieldWithBattleFeedback(KeyCode.L, KeyModifier.None, entityNav.CycleNext, "Next entity (alt)");
            RegisterFieldWithBattleFeedback(KeyCode.P, KeyModifier.Shift, entityNav.TogglePathfindingFilter, "Toggle pathfinding filter (alt)");
            RegisterFieldWithBattleFeedback(KeyCode.P, KeyModifier.None, entityNav.AnnounceCurrentEntity, "Announce current entity (alt)");

            // --- Field: waypoint keys ---
            registry.Register(KeyCode.Comma, KeyModifier.Shift, KeyContext.Field, waypoints.CyclePreviousWaypointCategory, "Previous waypoint category");
            registry.Register(KeyCode.Comma, KeyModifier.None, KeyContext.Field, waypoints.CyclePreviousWaypoint, "Previous waypoint");
            registry.Register(KeyCode.Period, KeyModifier.Ctrl, KeyContext.Field, waypoints.RenameCurrentWaypoint, "Rename waypoint");
            registry.Register(KeyCode.Period, KeyModifier.Shift, KeyContext.Field, waypoints.CycleNextWaypointCategory, "Next waypoint category");
            registry.Register(KeyCode.Period, KeyModifier.None, KeyContext.Field, waypoints.CycleNextWaypoint, "Next waypoint");
            registry.Register(KeyCode.Slash, KeyModifier.CtrlShift, KeyContext.Field, waypoints.ClearAllWaypointsForMap, "Clear all waypoints for map");
            registry.Register(KeyCode.Slash, KeyModifier.Ctrl, KeyContext.Field, waypoints.RemoveCurrentWaypoint, "Remove current waypoint");
            registry.Register(KeyCode.Slash, KeyModifier.Shift, KeyContext.Field, waypoints.AddNewWaypointWithNaming, "Add waypoint with name");
            registry.Register(KeyCode.Slash, KeyModifier.None, KeyContext.Field, waypoints.PathfindToCurrentWaypoint, "Pathfind to waypoint");

            // --- Field: teleport (Ctrl+Arrow) ---
            registry.Register(KeyCode.UpArrow, KeyModifier.Ctrl, KeyContext.Field, () => entityNav.TeleportInDirection(new Vector2(0, 16)), "Teleport north");
            registry.Register(KeyCode.DownArrow, KeyModifier.Ctrl, KeyContext.Field, () => entityNav.TeleportInDirection(new Vector2(0, -16)), "Teleport south");
            registry.Register(KeyCode.LeftArrow, KeyModifier.Ctrl, KeyContext.Field, () => entityNav.TeleportInDirection(new Vector2(-16, 0)), "Teleport west");
            registry.Register(KeyCode.RightArrow, KeyModifier.Ctrl, KeyContext.Field, () => entityNav.TeleportInDirection(new Vector2(16, 0)), "Teleport east");

            // --- Global: info/announcements ---
            registry.Register(KeyCode.G, KeyContext.Global, GameAnnouncementHelper.AnnounceGilAmount, "Announce Gil");
            registry.Register(KeyCode.H, KeyContext.Global, GameAnnouncementHelper.AnnounceCurrentCharacterStatus, "Announce character status");
            registry.Register(KeyCode.M, KeyModifier.Shift, KeyContext.Global, entityNav.ToggleMapExitFilter, "Toggle map exit filter");
            registry.Register(KeyCode.M, KeyModifier.None, KeyContext.Global, GameAnnouncementHelper.AnnounceCurrentMap, "Announce current map");
            registry.Register(KeyCode.T, KeyModifier.Shift, KeyContext.Global, TimerHelper.ToggleTimerFreeze, "Toggle timer freeze");
            registry.Register(KeyCode.T, KeyModifier.None, KeyContext.Global, () => TimerHelper.AnnounceActiveTimers(), "Announce active timers");
            registry.Register(KeyCode.V, KeyContext.Global, AnnounceVehicleState, "Announce vehicle state");
            registry.Register(KeyCode.I, KeyModifier.Shift, KeyContext.Global, KeyHelpReader.AnnounceKeyHelp, "Announce controls");
            registry.Register(KeyCode.I, KeyModifier.None, KeyContext.Global, HandleItemDetailsKey, "Item details");

            // --- Field-only toggles (blocked in battle with feedback) ---
            RegisterFieldWithBattleFeedback(KeyCode.Quote, KeyModifier.None, mod.ToggleFootsteps, "Toggle footsteps");
            RegisterFieldWithBattleFeedback(KeyCode.Semicolon, KeyModifier.None, mod.ToggleWallTones, "Toggle wall tones");
            RegisterFieldWithBattleFeedback(KeyCode.Alpha9, KeyModifier.None, mod.ToggleAudioBeacons, "Toggle audio beacons");

            // --- Field-only category shortcuts ---
            RegisterFieldWithBattleFeedback(KeyCode.K, KeyModifier.Shift, entityNav.ResetToAllCategory, "Reset to All category");
            RegisterFieldWithBattleFeedback(KeyCode.Equals, KeyModifier.None, entityNav.CycleNextCategory, "Next entity category (global)");
            RegisterFieldWithBattleFeedback(KeyCode.Minus, KeyModifier.None, entityNav.CyclePreviousCategory, "Previous entity category (global)");

            // Sort for correct modifier precedence
            registry.FinalizeRegistration();
        }

        public void Update()
        {
            // ModMenu handles its own input via Windows API when open
            if (ModMenu.IsOpen)
            {
                ModMenu.HandleInput();
                return;
            }

            // Handle dialog input
            if (ConfirmationDialog.HandleInput()) return;
            if (TextInputWindow.HandleInput()) return;

            if (!Input.anyKeyDown)
                return;

            // F8 to open mod menu
            if (Input.GetKeyDown(KeyCode.F8))
            {
                ModMenu.Open();
                return;
            }

            // Handle function keys (F1/F3 — special coroutine logic)
            HandleFunctionKeyInput();

            // Skip hotkeys when player is typing in a text field
            if (IsInputFieldFocused())
                return;

            // Determine active context
            KeyContext activeContext = DetermineContext();
            KeyModifier currentModifiers = GetCurrentModifiers();

            // Dispatch all registered bindings
            DispatchRegisteredBindings(activeContext, currentModifiers);
        }

        private KeyContext DetermineContext()
        {
            var tracker = StatusNavigationTracker.Instance;
            if (tracker.IsNavigationActive)
                return KeyContext.Status;

            if (BattleState.IsInBattle)
                return KeyContext.Battle;

            return KeyContext.Field;
        }

        private KeyModifier GetCurrentModifiers()
        {
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

            if (ctrl && shift) return KeyModifier.CtrlShift;
            if (ctrl) return KeyModifier.Ctrl;
            if (shift) return KeyModifier.Shift;
            return KeyModifier.None;
        }

        private void DispatchRegisteredBindings(KeyContext activeContext, KeyModifier currentModifiers)
        {
            foreach (var key in registry.RegisteredKeys)
            {
                if (Input.GetKeyDown(key))
                    registry.TryExecute(key, currentModifiers, activeContext);
            }
        }

        private void HandleFunctionKeyInput()
        {
            if (Input.GetKeyDown(KeyCode.F1))
            {
                CoroutineManager.StartUntracked(AnnounceWalkRunState());
                return;
            }

            if (Input.GetKeyDown(KeyCode.F3))
            {
                CoroutineManager.StartUntracked(AnnounceEncounterState());
            }
        }

        private void AnnounceVehicleState()
        {
            try
            {
                int moveState = MoveStateHelper.GetCurrentMoveState();
                string stateName = MoveStateHelper.GetMoveStateName(moveState);
                FFIV_ScreenReaderMod.SpeakText(stateName, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Vehicle State] Error: {ex.Message}");
                FFIV_ScreenReaderMod.SpeakText(T("Unable to detect vehicle state"), interrupt: true);
            }
        }

        private void HandleItemDetailsKey()
        {
            if (ShopMenuTracker.ValidateState())
            {
                ShopDetailsAnnouncer.AnnounceCurrentItemDetails();
            }
            else if (ItemMenuState.IsActive)
            {
                ItemDetailsAnnouncer.AnnounceEquipRequirements();
            }
            else
            {
                AnnounceConfigTooltip();
            }
        }

        private void AnnounceConfigTooltip()
        {
            try
            {
                var keyInputController = UnityEngine.Object.FindObjectOfType<ConfigActualDetailsControllerBase_KeyInput>();
                if (keyInputController != null && keyInputController.gameObject.activeInHierarchy)
                {
                    var descText = keyInputController.descriptionText;
                    if (descText != null && !string.IsNullOrWhiteSpace(descText.text))
                    {
                        FFIV_ScreenReaderMod.SpeakText(descText.text.Trim());
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading config tooltip: {ex.Message}");
            }
        }

        private bool IsInputFieldFocused()
        {
            try
            {
                if (EventSystem.current == null)
                    return false;

                var currentObj = EventSystem.current.currentSelectedGameObject;
                if (currentObj == null)
                    return false;

                return currentObj.TryGetComponent(out UnityEngine.UI.InputField inputField);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error checking input field state: {ex.Message}");
                return false;
            }
        }

        private static IEnumerator AnnounceWalkRunState()
        {
            yield return null;
            yield return null;
            yield return null;

            try
            {
                bool isDashing = MoveStateHelper.GetDashFlag();
                string state = isDashing ? T("Run") : T("Walk");
                FFIV_ScreenReaderMod.SpeakText(state, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[F1] Error reading walk/run state: {ex.Message}");
            }
        }

        private static IEnumerator AnnounceEncounterState()
        {
            yield return null;
            try
            {
                var userData = Il2CppLast.Management.UserDataManager.Instance();
                if (userData?.CheatSettingsData != null)
                {
                    bool enabled = userData.CheatSettingsData.IsEnableEncount;
                    string state = enabled ? T("Encounters on") : T("Encounters off");
                    FFIV_ScreenReaderMod.SpeakText(state, interrupt: true);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[F3] Error reading encounter state: {ex.Message}");
            }
        }
    }
}
