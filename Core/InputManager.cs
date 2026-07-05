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

        private void RegisterFieldOnly(KeyCode key, KeyModifier modifier, Action action, string description)
        {
            // Field-only action. Off-field (menu/battle/title) the active context is never
            // Field, so this binding has no match and dispatch silently does nothing.
            registry.Register(key, modifier, KeyContext.Field, action, description);
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

            // --- Field: entity navigation (brackets + backslash) — field-only ---
            RegisterFieldOnly(KeyCode.LeftBracket, KeyModifier.Shift, entityNav.CyclePreviousCategory, "Previous entity category");
            RegisterFieldOnly(KeyCode.LeftBracket, KeyModifier.None, entityNav.CyclePrevious, "Previous entity");
            RegisterFieldOnly(KeyCode.RightBracket, KeyModifier.Shift, entityNav.CycleNextCategory, "Next entity category");
            RegisterFieldOnly(KeyCode.RightBracket, KeyModifier.None, entityNav.CycleNext, "Next entity");
            RegisterFieldOnly(KeyCode.Backslash, KeyModifier.Ctrl, entityNav.ToggleToLayerFilter, "Toggle layer filter");
            RegisterFieldOnly(KeyCode.Backslash, KeyModifier.Shift, entityNav.TogglePathfindingFilter, "Toggle pathfinding filter");
            RegisterFieldOnly(KeyCode.Backslash, KeyModifier.None, entityNav.AnnounceCurrentEntity, "Announce current entity");

            // --- Field: alternate keys (J/K/L/P) — field-only ---
            RegisterFieldOnly(KeyCode.J, KeyModifier.Shift, entityNav.CyclePreviousCategory, "Previous entity category (alt)");
            RegisterFieldOnly(KeyCode.J, KeyModifier.None, entityNav.CyclePrevious, "Previous entity (alt)");
            RegisterFieldOnly(KeyCode.K, KeyModifier.None, entityNav.AnnounceEntityOnly, "Announce entity name (alt)");
            RegisterFieldOnly(KeyCode.L, KeyModifier.Shift, entityNav.CycleNextCategory, "Next entity category (alt)");
            RegisterFieldOnly(KeyCode.L, KeyModifier.None, entityNav.CycleNext, "Next entity (alt)");
            RegisterFieldOnly(KeyCode.P, KeyModifier.Shift, entityNav.TogglePathfindingFilter, "Toggle pathfinding filter (alt)");
            RegisterFieldOnly(KeyCode.P, KeyModifier.None, entityNav.AnnounceCurrentEntity, "Announce current entity (alt)");

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

            // --- Field-only toggles ---
            RegisterFieldOnly(KeyCode.Quote, KeyModifier.None, mod.ToggleFootsteps, "Toggle footsteps");
            RegisterFieldOnly(KeyCode.Semicolon, KeyModifier.None, mod.ToggleWallTones, "Toggle wall tones");
            RegisterFieldOnly(KeyCode.Alpha9, KeyModifier.None, mod.ToggleAudioBeacons, "Toggle audio beacons");

            // --- Field-only category shortcuts ---
            RegisterFieldOnly(KeyCode.K, KeyModifier.Shift, entityNav.ResetToAllCategory, "Reset to All category");
            RegisterFieldOnly(KeyCode.Equals, KeyModifier.None, entityNav.CycleNextCategory, "Next entity category (global)");
            RegisterFieldOnly(KeyCode.Minus, KeyModifier.None, entityNav.CyclePreviousCategory, "Previous entity category (global)");

            // Sort for correct modifier precedence
            registry.FinalizeRegistration();
        }

        public void Update()
        {
            // Poll SDL3 gamepad + GetAsyncKeyState keyboard once per frame.
            // Must come before any mod input handling so edge-detection state is fresh.
            GamepadManager.Update();

            // Suppress Unity legacy Input when the mod is consuming. Safe because the mod reads
            // keyboard via GetAsyncKeyState (unaffected by ResetInputAxes). This + the
            // InputPassthroughPatches = complete game keyboard suppression, and it works even
            // when no gamepad is connected (the passthrough patches early-return without one).
            if (ControllerRouter.SuppressGameInput)
                Input.ResetInputAxes();

            // Determine context AFTER polling so the router (and dispatch below) sees fresh
            // input for this frame.
            KeyContext activeContext = DetermineContext();

            // Route controller inputs to the appropriate state-machine bucket. Runs every frame
            // so the router can interrupt speech / drive nav even without a gamepad.
            ControllerRouter.Update(activeContext);

            if (GamepadManager.AnyKeyboardKeyDown())
                ControllerRouter.NotifyKeyboardInput();

            // Handle modal dialogs first (each consumes all input when open)
            if (ConfirmationDialog.HandleInput()) return;
            if (TextInputWindow.HandleInput()) return;
            if (ModMenu.HandleInput()) return;

            // Game-context hotkeys below only fire when the game window is the foreground
            // window, so mod functions don't trigger while the player is in another app.
            // Placed AFTER the modals so the now-virtual dialogs/menu keep working even when
            // the game window isn't foreground.
            if (!WindowsFocusHelper.IsGameWindowFocused())
                return;

            if (!GamepadManager.AnyKeyboardKeyDown())
                return;

            // Skip ALL mod hotkeys (including F8 and the function keys) while the player is
            // typing in the game's own text field, so naming/input screens aren't disrupted.
            if (IsInputFieldFocused()) return;

            // Bare F-keys only fire with no modifier held, so OS shortcuts like Alt+F4
            // (close window), Ctrl+F-keys and Shift+F-keys don't trigger the screen
            // reader. Explicit Shift/Ctrl bindings still match via GetCurrentModifiers.
            bool anyModifierHeld = IsAnyModifierHeld();

            // F8 to open mod menu — gated to field-only via ControllerRouter.IsFieldActive
            // (blocks battle, in-game menus, title screen). Rejection wording lives in
            // ControllerRouter.SpeakModMenuUnavailable so Start-button and F8 stay in sync.
            if (!anyModifierHeld && GamepadManager.IsKeyCodePressed(KeyCode.F8))
            {
                if (ControllerRouter.IsFieldActive)
                    ModMenu.Open();
                else
                    ControllerRouter.SpeakModMenuUnavailable();
                return;
            }

            // Handle function keys (F1/F3/F5 — special coroutine/battle logic) — bare keypress only
            if (!anyModifierHeld)
                HandleFunctionKeyInput();

            KeyModifier currentModifiers = GetCurrentModifiers();

            // Alt held with no registered Alt-binding → skip dispatch so Alt+<key> doesn't
            // accidentally trigger the unmodified binding. (Shift/Ctrl are routed through
            // currentModifiers and matched exactly by the registry, so they still work.)
            if (IsAltHeld())
                return;

            // Dispatch all registered bindings
            DispatchRegisteredBindings(activeContext, currentModifiers);
        }

        private static bool IsAltHeld()
        {
            return GamepadManager.IsKeyCodeHeld(KeyCode.LeftAlt)
                || GamepadManager.IsKeyCodeHeld(KeyCode.RightAlt);
        }

        private static bool IsAnyModifierHeld()
        {
            return GamepadManager.IsKeyCodeHeld(KeyCode.LeftShift)
                || GamepadManager.IsKeyCodeHeld(KeyCode.RightShift)
                || GamepadManager.IsKeyCodeHeld(KeyCode.LeftControl)
                || GamepadManager.IsKeyCodeHeld(KeyCode.RightControl)
                || GamepadManager.IsKeyCodeHeld(KeyCode.LeftAlt)
                || GamepadManager.IsKeyCodeHeld(KeyCode.RightAlt);
        }

        private KeyContext DetermineContext()
        {
            var tracker = StatusNavigationTracker.Instance;
            if (tracker.IsNavigationActive)
                return KeyContext.Status;

            if (BattleState.IsInBattle)
                return KeyContext.Battle;

            // Field keys only fire while actively on a field map with no menu open.
            // Otherwise fall through to Global so field/entity/waypoint/toggle hotkeys
            // are silent no-ops off-field, while Global info keys still work everywhere.
            if (IsOnValidMap() && !MenuStateRegistry.AnyActive())
                return KeyContext.Field;

            return KeyContext.Global;
        }

        private static bool IsOnValidMap()
        {
            return GameObjectCache.Get<Il2CppLast.Map.FieldPlayerController>()?.fieldPlayer != null;
        }

        private KeyModifier GetCurrentModifiers()
        {
            bool shift = GamepadManager.IsKeyCodeHeld(KeyCode.LeftShift) || GamepadManager.IsKeyCodeHeld(KeyCode.RightShift);
            bool ctrl = GamepadManager.IsKeyCodeHeld(KeyCode.LeftControl) || GamepadManager.IsKeyCodeHeld(KeyCode.RightControl);

            if (ctrl && shift) return KeyModifier.CtrlShift;
            if (ctrl) return KeyModifier.Ctrl;
            if (shift) return KeyModifier.Shift;
            return KeyModifier.None;
        }

        private void DispatchRegisteredBindings(KeyContext activeContext, KeyModifier currentModifiers)
        {
            foreach (var key in registry.RegisteredKeys)
            {
                if (GamepadManager.IsKeyCodePressed(key))
                    registry.TryExecute(key, currentModifiers, activeContext);
            }
        }

        private void HandleFunctionKeyInput()
        {
            if (GamepadManager.IsKeyCodePressed(KeyCode.F1))
            {
                CoroutineManager.StartUntracked(AnnounceWalkRunState());
                return;
            }

            if (GamepadManager.IsKeyCodePressed(KeyCode.F3))
            {
                CoroutineManager.StartUntracked(AnnounceEncounterState());
                return;
            }

            if (GamepadManager.IsKeyCodePressed(KeyCode.F5))
            {
                // Enemy HP Display is a battle feature, so gate on in-battle (not IsFieldActive,
                // which is false during battle). Restores the pre-refactor behavior.
                if (BattleState.IsInBattle)
                {
                    int current = PreferencesManager.EnemyHPDisplay;
                    int next = (current + 1) % 3;
                    PreferencesManager.SetEnemyHPDisplay(next);
                    string[] options = { T("Numbers"), T("Percentage"), T("Hidden") };
                    FFIV_ScreenReaderMod.SpeakText(string.Format(T("Enemy HP: {0}"), options[next]), interrupt: true);
                }
                else
                {
                    ControllerRouter.SpeakModMenuUnavailable();
                }
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
