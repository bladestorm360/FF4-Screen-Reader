using System;
using System.Collections.Generic;
using MelonLoader;

namespace FFIV_ScreenReader.Core
{
    /// <summary>
    /// Central registry for menu state management.
    /// Uses dictionary-based tracking instead of individual boolean fields.
    /// </summary>
    public static class MenuStateRegistry
    {
        // Extras menu state keys
        public const string BESTIARY_LIST = "BestiaryList";
        public const string BESTIARY_DETAIL = "BestiaryDetail";
        public const string BESTIARY_FORMATION = "BestiaryFormation";
        public const string BESTIARY_MAP = "BestiaryMap";
        public const string MUSIC_PLAYER = "MusicPlayer";
        public const string GALLERY = "Gallery";

        private static readonly Dictionary<string, bool> _states = new();
        private static readonly Dictionary<string, Action> _resetHandlers = new();

        /// <summary>
        /// Register a menu with its reset handler.
        /// Called by wrapper classes during static initialization.
        /// </summary>
        public static void Register(string menu, Action resetHandler)
        {
            _states[menu] = false;
            _resetHandlers[menu] = resetHandler;
        }

        public static bool IsActive(string menu) => _states.TryGetValue(menu, out var v) && v;

        /// <summary>
        /// Returns true if any registered menu state is currently active.
        /// Used by CursorSuppressionCheck to replace per-state ShouldSuppress cascade.
        /// </summary>
        public static bool IsAnyActive()
        {
            foreach (var kvp in _states)
                if (kvp.Value) return true;
            return false;
        }

        public static void SetState(string menu, bool active) => _states[menu] = active;

        /// <summary>
        /// Sets a menu's active state. Registers the key if not already present.
        /// Used by extras menu patches (bestiary, gallery, music player).
        /// </summary>
        public static void SetActive(string menu, bool active)
        {
            _states[menu] = active;
        }

        /// <summary>
        /// Resets a specific menu state to inactive.
        /// </summary>
        public static void Reset(string menu)
        {
            SetActive(menu, false);
        }

        /// <summary>
        /// Resets multiple menu states to inactive.
        /// </summary>
        public static void Reset(params string[] menus)
        {
            foreach (var menu in menus)
                SetActive(menu, false);
        }

        /// <summary>
        /// Sets a menu active and clears all other menus.
        /// </summary>
        public static void SetActiveExclusive(string menu)
        {
            ClearOtherMenuStates(menu);
            _states[menu] = true;
        }

        /// <summary>
        /// Clears all menu states except the specified one.
        /// </summary>
        public static void ClearOtherMenuStates(string exceptMenu)
        {
            foreach (var kvp in _resetHandlers)
            {
                if (kvp.Key != exceptMenu)
                    kvp.Value?.Invoke();
            }

            // External state resets (not managed by this registry)
            if (exceptMenu != "SaveLoad") Patches.SaveLoadMenuState.ResetState();
            if (exceptMenu != "Popup") Patches.PopupState.Clear();
            if (exceptMenu != "Naming") Patches.NamingPatches.ClearState();
        }

        /// <summary>
        /// Clears all menu states.
        /// </summary>
        public static void ClearAllMenuStates()
        {
            foreach (var kvp in _resetHandlers)
                kvp.Value?.Invoke();

            Patches.SaveLoadMenuState.ResetState();
            Patches.PopupState.Clear();
            Patches.NamingPatches.ClearState();
        }

        /// <summary>
        /// Forces initialization of all wrapper classes.
        /// Call during mod initialization to ensure all handlers are registered.
        /// </summary>
        public static void Initialize()
        {
            // Access static members to trigger static constructors
            _ = BattleState.IsInBattle;
            _ = ShopState.IsActive;
            _ = ItemMenuState.IsActive;
            _ = EquipmentMenuState.IsActive;

            // SimpleMenuState instances - access to ensure construction and registration
            _ = MenuStates.Ability.IsActive;
            _ = MenuStates.Config.IsActive;
            _ = MenuStates.Status.IsActive;
            _ = MenuStates.Party.IsActive;
            _ = MenuStates.Title.IsActive;
        }
    }

    // Backward-compatible wrapper classes
    // Each delegates to MenuStateRegistry for state management

    public static class BattleState
    {
        static BattleState() => MenuStateRegistry.Register("Battle", Reset);

        public static bool IsInBattle => MenuStateRegistry.IsActive("Battle");

        // Pre-suppression navigation state storage
        private static bool _preBattleWallTones = false;
        private static bool _preBattleFootsteps = false;
        private static bool _preBattleAudioBeacons = false;
        private static bool _preBattlePathfindingFilter = false;
        private static bool _hasStoredState = false;

        public static void SetActive()
        {
            // Store and suppress navigation (only if not already stored)
            if (!_hasStoredState && FFIV_ScreenReaderMod.Instance != null)
            {
                _preBattleWallTones = AudioLoopManager.WallTonesEnabled;
                _preBattleFootsteps = AudioLoopManager.FootstepsEnabled;
                _preBattleAudioBeacons = AudioLoopManager.AudioBeaconsEnabled;
                _preBattlePathfindingFilter = FFIV_ScreenReaderMod.PathfindingFilterEnabled;
                _hasStoredState = true;
                FFIV_ScreenReaderMod.NavigationState?.SuppressNavigationForBattle();
            }
            MenuStateRegistry.SetActiveExclusive("Battle");
        }

        public static void Reset()
        {
            MenuStateRegistry.SetState("Battle", false);
            Patches.GlobalBattleMessageTracker.Reset();

            // Restore navigation state
            if (_hasStoredState && FFIV_ScreenReaderMod.NavigationState != null)
            {
                FFIV_ScreenReaderMod.NavigationState.RestoreNavigationAfterBattle(
                    _preBattleWallTones, _preBattleFootsteps,
                    _preBattleAudioBeacons, _preBattlePathfindingFilter);
                _hasStoredState = false;
            }
        }

        public static bool ShouldSuppress() => IsInBattle;
    }

    public static class ShopState
    {
        static ShopState() => MenuStateRegistry.Register("Shop", Reset);

        public static bool IsActive => MenuStateRegistry.IsActive("Shop");

        /// <summary>
        /// Tracks when we've entered Equipment submenu from shop.
        /// Used to restore ShopState when returning from equipment command bar.
        /// Preserved across menu state transitions (e.g., when EquipmentMenuState activates).
        /// </summary>
        public static bool EnteredEquipmentSubmenu { get; set; }

        public static void SetActive() => MenuStateRegistry.SetActiveExclusive("Shop");

        /// <summary>
        /// Clears IsActive but preserves EnteredEquipmentSubmenu flag.
        /// Used when entering Equipment submenu to allow generic cursor to read.
        /// </summary>
        public static void ClearForEquipmentSubmenu()
        {
            EnteredEquipmentSubmenu = true;
            MenuStateRegistry.SetState("Shop", false);
        }

        /// <summary>
        /// Resets shop state. Called by MenuStateRegistry when other menus activate.
        /// Preserves EnteredEquipmentSubmenu since it tracks a multi-menu transition.
        /// </summary>
        public static void Reset()
        {
            MenuStateRegistry.SetState("Shop", false);
            // NOTE: EnteredEquipmentSubmenu is intentionally NOT cleared here.
            // It tracks shop→equipment→shop transitions and must survive
            // EquipmentMenuState.SetActive() which triggers this Reset.
            Patches.ShopMenuTracker.Reset();
        }

        /// <summary>
        /// Full reset including EnteredEquipmentSubmenu. Called when leaving shop entirely.
        /// </summary>
        public static void FullReset()
        {
            MenuStateRegistry.SetState("Shop", false);
            EnteredEquipmentSubmenu = false;
            Patches.ShopMenuTracker.Reset();
        }

        public static bool ShouldSuppress() => IsActive;
    }

    public static class ItemMenuState
    {
        static ItemMenuState() => MenuStateRegistry.Register("Item", Reset);

        public static bool IsActive => MenuStateRegistry.IsActive("Item");
        public static Il2CppLast.UI.ItemListContentData LastSelectedItem { get; set; }

        public static void SetActive() => MenuStateRegistry.SetActiveExclusive("Item");

        public static void Reset()
        {
            MenuStateRegistry.SetState("Item", false);
            LastSelectedItem = null;
        }

        public static bool ShouldSuppress() => IsActive;
    }

    public static class EquipmentMenuState
    {
        static EquipmentMenuState() => MenuStateRegistry.Register("Equipment", Reset);

        public static bool IsActive => MenuStateRegistry.IsActive("Equipment");
        public static void SetActive() => MenuStateRegistry.SetActiveExclusive("Equipment");

        public static void Reset()
        {
            MenuStateRegistry.SetState("Equipment", false);
            Patches.EquipmentAnnouncementDeduplicator.Reset();
        }

        public static bool ShouldSuppress() => IsActive;
    }

    /// <summary>
    /// Reusable menu state wrapper that replaces identical boilerplate static classes.
    /// Each instance registers with MenuStateRegistry on construction.
    /// </summary>
    public class SimpleMenuState
    {
        public string Name { get; }

        public SimpleMenuState(string name)
        {
            Name = name;
            MenuStateRegistry.Register(name, Reset);
        }

        public bool IsActive => MenuStateRegistry.IsActive(Name);
        public void SetActive() => MenuStateRegistry.SetActiveExclusive(Name);
        public void Reset() => MenuStateRegistry.SetState(Name, false);
        public bool ShouldSuppress() => IsActive;
    }

    /// <summary>
    /// Holds all simple menu state instances (Ability, Config, Status, Party, Title).
    /// These replaced the identical boilerplate static wrapper classes.
    /// </summary>
    public static class MenuStates
    {
        public static readonly SimpleMenuState Ability = new SimpleMenuState("Ability");
        public static readonly SimpleMenuState Config = new SimpleMenuState("Config");
        public static readonly SimpleMenuState Status = new SimpleMenuState("Status");
        public static readonly SimpleMenuState Party = new SimpleMenuState("Party");
        public static readonly SimpleMenuState Title = new SimpleMenuState("Title");
    }

    /// <summary>
    /// Legacy compatibility - delegates to MenuStateRegistry.
    /// </summary>
    public static class MenuState
    {
        public static void ClearOtherMenuStates(string exceptMenu)
            => MenuStateRegistry.ClearOtherMenuStates(exceptMenu);

        public static void ClearAllMenuStates()
            => MenuStateRegistry.ClearAllMenuStates();
    }
}
