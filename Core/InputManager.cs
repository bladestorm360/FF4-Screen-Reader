using UnityEngine;
using UnityEngine.EventSystems;
using Il2Cpp;
using MelonLoader;
using FFIV_ScreenReader.Utils;
using FFIV_ScreenReader.Menus;
using FFIV_ScreenReader.Patches;
using ConfigActualDetailsControllerBase_KeyInput = Il2CppLast.UI.KeyInput.ConfigActualDetailsControllerBase;

namespace FFIV_ScreenReader.Core
{
    /// <summary>
    /// Manages all keyboard input handling for the screen reader mod.
    /// Detects hotkeys and routes them to appropriate mod functions.
    /// </summary>
    public class InputManager
    {
        private readonly FFIV_ScreenReaderMod mod;

        public InputManager(FFIV_ScreenReaderMod mod)
        {
            this.mod = mod;
        }

        /// <summary>
        /// Called every frame to check for input and route hotkeys.
        /// </summary>
        public void Update()
        {
            // Early exit if no keys pressed this frame - avoids expensive FindObjectOfType calls
            if (!Input.anyKeyDown)
            {
                return;
            }

            // Check if ANY Unity InputField is focused - if so, let all keys pass through
            if (IsInputFieldFocused())
            {
                // Player is typing text - skip all hotkey processing
                return;
            }

            // Handle status screen navigation (highest priority when active)
            if (HandleStatusInput())
            {
                return; // Consumed by status screen
            }

            // Handle field input (entity navigation)
            HandleFieldInput();

            // Global hotkeys (work in both field and menus)
            HandleGlobalInput();
        }

        /// <summary>
        /// Checks if a Unity InputField is currently focused (player is typing).
        /// Uses EventSystem for efficient O(1) lookup instead of FindObjectOfType scene search.
        /// </summary>
        private bool IsInputFieldFocused()
        {
            try
            {
                // Check if EventSystem exists and has a selected object
                if (EventSystem.current == null)
                    return false;

                var currentObj = EventSystem.current.currentSelectedGameObject;

                // 1. Check if anything is selected
                if (currentObj == null)
                    return false;

                // 2. Check if the selected object is a standard InputField
                // TryGetComponent avoids memory allocation overhead
                return currentObj.TryGetComponent(out UnityEngine.UI.InputField inputField);
            }
            catch (System.Exception ex)
            {
                // If we can't check input field state, continue with normal hotkey processing
                MelonLoader.MelonLogger.Warning($"Error checking input field state: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Handles input for status screen stat navigation.
        /// Returns true if input was consumed, false otherwise.
        /// </summary>
        private bool HandleStatusInput()
        {
            // Only active when status navigation is enabled
            var tracker = StatusNavigationTracker.Instance;
            if (!tracker.IsNavigationActive)
            {
                return false;
            }

            // Up Arrow - Previous stat
            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                StatusNavigationReader.NavigatePrevious();
                return true;
            }

            // Down Arrow - Next stat
            if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                StatusNavigationReader.NavigateNext();
                return true;
            }

            // Page Up - Jump to previous group
            if (Input.GetKeyDown(KeyCode.PageUp))
            {
                StatusNavigationReader.JumpToPreviousGroup();
                return true;
            }

            // Page Down - Jump to next group
            if (Input.GetKeyDown(KeyCode.PageDown))
            {
                StatusNavigationReader.JumpToNextGroup();
                return true;
            }

            // Home - Jump to first stat
            if (Input.GetKeyDown(KeyCode.Home))
            {
                StatusNavigationReader.JumpToTop();
                return true;
            }

            // End - Jump to last stat
            if (Input.GetKeyDown(KeyCode.End))
            {
                StatusNavigationReader.JumpToBottom();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Handles input when on the field (entity navigation).
        /// </summary>
        private void HandleFieldInput()
        {
            // Hotkey: J or [ to cycle backwards
            if (Input.GetKeyDown(KeyCode.J) || Input.GetKeyDown(KeyCode.LeftBracket))
            {
                // Check for Shift+J/[ (cycle categories backward)
                if (IsShiftHeld())
                {
                    mod.CyclePreviousCategory();
                }
                else
                {
                    // Just J/[ (cycle entities backward)
                    mod.CyclePrevious();
                }
            }

            // Hotkey: K to repeat current entity
            if (Input.GetKeyDown(KeyCode.K))
            {
                mod.AnnounceEntityOnly();
            }

            // Hotkey: L or ] to cycle forwards
            if (Input.GetKeyDown(KeyCode.L) || Input.GetKeyDown(KeyCode.RightBracket))
            {
                // Check for Shift+L/] (cycle categories forward)
                if (IsShiftHeld())
                {
                    mod.CycleNextCategory();
                }
                else
                {
                    // Just L/] (cycle entities forward)
                    mod.CycleNext();
                }
            }

            // Hotkey: P or \ to pathfind to current entity
            if (Input.GetKeyDown(KeyCode.P) || Input.GetKeyDown(KeyCode.Backslash))
            {
                // Check for Shift+P/\ (toggle pathfinding filter)
                if (IsShiftHeld())
                {
                    mod.TogglePathfindingFilter();
                }
                else
                {
                    // Just P/\ (pathfind to current entity)
                    mod.AnnounceCurrentEntity();
                }
            }
        }

        /// <summary>
        /// Handles global input (works in both field and menus).
        /// </summary>
        private void HandleGlobalInput()
        {
            // Hotkey: Ctrl+Arrow to teleport in the direction of the arrow
            if (IsCtrlHeld())
            {
                if (Input.GetKeyDown(KeyCode.UpArrow))
                {
                    mod.TeleportInDirection(new Vector2(0, 16)); // North
                }
                else if (Input.GetKeyDown(KeyCode.DownArrow))
                {
                    mod.TeleportInDirection(new Vector2(0, -16)); // South
                }
                else if (Input.GetKeyDown(KeyCode.LeftArrow))
                {
                    mod.TeleportInDirection(new Vector2(-16, 0)); // West
                }
                else if (Input.GetKeyDown(KeyCode.RightArrow))
                {
                    mod.TeleportInDirection(new Vector2(16, 0)); // East
                }
            }

            // Hotkey: H to announce character health
            // AnnounceCurrentCharacterStatus handles the "not in battle" case
            if (Input.GetKeyDown(KeyCode.H))
            {
                mod.AnnounceCurrentCharacterStatus();
            }

            // Hotkey: I to announce item details, equipment requirements, or config option description/tooltip
            if (Input.GetKeyDown(KeyCode.I))
            {
                // Check shop menu first (highest priority)
                if (FFIV_ScreenReader.Patches.ShopMenuTracker.ValidateState())
                {
                    FFIV_ScreenReader.Patches.ShopDetailsAnnouncer.AnnounceCurrentItemDetails();
                }
                // Check item menu for equipment compatibility
                else if (ItemMenuState.IsActive)
                {
                    FFIV_ScreenReader.Patches.ItemDetailsAnnouncer.AnnounceEquipRequirements();
                }
                else
                {
                    AnnounceConfigTooltip();
                }
            }

            // Hotkey: G to announce current gil amount
            if (Input.GetKeyDown(KeyCode.G))
            {
                mod.AnnounceGilAmount();
            }

            // Hotkey: M to announce current map name
            if (Input.GetKeyDown(KeyCode.M))
            {
                // Check for Shift+M (toggle map exit filter)
                if (IsShiftHeld())
                {
                    mod.ToggleMapExitFilter();
                }
                else
                {
                    // Just M (announce current map)
                    mod.AnnounceCurrentMap();
                }
            }

            // Hotkey: 0 (Alpha0) or Shift+K to reset to All category
            if (Input.GetKeyDown(KeyCode.Alpha0))
            {
                mod.ResetToAllCategory();
            }

            if (Input.GetKeyDown(KeyCode.K) && IsShiftHeld())
            {
                mod.ResetToAllCategory();
            }

            // Hotkey: = (Equals) or Shift+L/] to cycle to next category
            if (Input.GetKeyDown(KeyCode.Equals))
            {
                mod.CycleNextCategory();
            }

            // Hotkey: - (Minus) or Shift+J/[ to cycle to previous category
            if (Input.GetKeyDown(KeyCode.Minus))
            {
                mod.CyclePreviousCategory();
            }

            // Hotkey: T to announce active timers
            if (Input.GetKeyDown(KeyCode.T))
            {
                // Check for Shift+T (freeze/resume timers)
                if (IsShiftHeld())
                {
                    Patches.TimerHelper.ToggleTimerFreeze();
                }
                else
                {
                    // Just T (announce timers)
                    Patches.TimerHelper.AnnounceActiveTimers();
                }
            }

            // Hotkey: V to announce current vehicle/movement state (diagnostic)
            if (Input.GetKeyDown(KeyCode.V))
            {
                AnnounceVehicleState();
            }
        }

        /// <summary>
        /// Announces the current vehicle/movement state.
        /// </summary>
        private void AnnounceVehicleState()
        {
            try
            {
                int moveState = MoveStateHelper.GetCurrentMoveState();
                string stateName = MoveStateHelper.GetMoveStateName(moveState);

                FFIV_ScreenReaderMod.SpeakText(stateName, interrupt: true);
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[Vehicle State] Error: {ex.Message}");
                FFIV_ScreenReaderMod.SpeakText("Unable to detect vehicle state", interrupt: true);
            }
        }

        /// <summary>
        /// Checks if either Shift key is held.
        /// </summary>
        private bool IsShiftHeld()
        {
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        }

        /// <summary>
        /// Checks if either Ctrl key is held.
        /// </summary>
        private bool IsCtrlHeld()
        {
            return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        }

        /// <summary>
        /// Checks if the game is currently in battle.
        /// Uses the presence of an active BattleMenuController as the indicator.
        /// </summary>
        private bool IsInBattle()
        {
            try
            {
                // Check if there's an active character from the battle patch
                var activeCharacter = FFIV_ScreenReader.Patches.BattleMenuController_SetCommandSelectTarget_Patch.CurrentActiveCharacter;
                return activeCharacter != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Announces the description/tooltip text for the currently highlighted config option.
        /// Only works when in the in-game config menu.
        /// </summary>
        private void AnnounceConfigTooltip()
        {
            try
            {
                // Try KeyInput controller (in-game config menu)
                var keyInputController = Object.FindObjectOfType<ConfigActualDetailsControllerBase_KeyInput>();
                if (keyInputController != null && keyInputController.gameObject.activeInHierarchy)
                {
                    string description = GetDescriptionText(keyInputController);
                    if (!string.IsNullOrEmpty(description))
                    {
                        FFIV_ScreenReaderMod.SpeakText(description);
                        return;
                    }
                }

                // Not in a config menu or no description available
                // Silently ignore
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"Error reading config tooltip: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the description text from a KeyInput ConfigActualDetailsControllerBase.
        /// </summary>
        private string GetDescriptionText(ConfigActualDetailsControllerBase_KeyInput controller)
        {
            if (controller == null) return null;

            try
            {
                // Access the descriptionText field
                var descText = controller.descriptionText;
                if (descText != null && !string.IsNullOrWhiteSpace(descText.text))
                {
                    return descText.text.Trim();
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"Error accessing description text: {ex.Message}");
            }

            return null;
        }
    }
}
