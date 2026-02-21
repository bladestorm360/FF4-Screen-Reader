using MelonLoader;
using HarmonyLib;
using FFIV_ScreenReader.Utils;
using FFIV_ScreenReader.Field;
using FFIV_ScreenReader.Patches;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Il2Cpp;
using Il2CppLast.Map;
using Il2CppLast.Message;
using FieldTresureBox = Il2CppLast.Entity.Field.FieldTresureBox;
using SubSceneManagerMainGame = Il2CppLast.Management.SubSceneManagerMainGame;
using UserDataManager = Il2CppLast.Management.UserDataManager;

[assembly: MelonInfo(typeof(FFIV_ScreenReader.Core.FFIV_ScreenReaderMod), "FFIV Screen Reader", "1.0.0", "Zachary Kline")]
[assembly: MelonGame("SQUARE ENIX, Inc.", "FINAL FANTASY IV")]

namespace FFIV_ScreenReader.Core
{
    /// <summary>
    /// Main mod class for FFIV Screen Reader.
    /// Provides screen reader accessibility support for Final Fantasy IV Pixel Remaster.
    /// </summary>
    public class FFIV_ScreenReaderMod : MelonMod
    {
        private static TolkWrapper tolk;
        private InputManager inputManager;
        private EntityCache entityCache;
        private EntityNavigator entityNavigator;

        // Audio feedback subsystem
        internal AudioLoopManager audioManager;

        // Waypoint system
        private WaypointManager waypointManager;
        private WaypointNavigator waypointNavigator;

        // Navigation state (battle/dialogue suppression)
        private NavigationStateManager navigationState;

        // Facades
        internal EntityNavigationFacade entityNavFacade;
        internal WaypointFacade waypointFacade;

        // Static instance for access from patches
        internal static FFIV_ScreenReaderMod Instance { get; private set; }

        // Static accessor for navigation state (used by BattleState, MessagePatches)
        internal static NavigationStateManager NavigationState => Instance?.navigationState;

        // Stored delegate for proper event unsubscription (fixes memory leak)
        private static UnityAction<Scene, LoadSceneMode> _onSceneLoadedHandler;

        public override void OnInitializeMelon()
        {
            Instance = this;

            // Subscribe to scene load events for automatic component caching
            // Store delegate as field to ensure proper unsubscription
            _onSceneLoadedHandler = (UnityAction<Scene, LoadSceneMode>)OnSceneLoaded;
            SceneManager.sceneLoaded += _onSceneLoadedHandler;

            // Initialize preferences
            PreferencesManager.Initialize();

            // Initialize Tolk for screen reader support
            tolk = new TolkWrapper();
            tolk.Load();

            // Initialize external sound player for distinct audio feedback (wall bumps, tones, footsteps)
            SoundPlayer.Initialize();

            // Initialize entity name translator for Japanese-to-English entity names
            EntityTranslator.Initialize();

            // Initialize entity cache and navigator (event-driven, no timer)
            entityCache = new EntityCache();
            entityNavigator = new EntityNavigator(entityCache);

            // Initialize audio feedback manager
            audioManager = new AudioLoopManager(entityNavigator, entityCache);
            audioManager.LoadPreferences();

            // Initialize navigation state manager (battle/dialogue suppression)
            navigationState = new NavigationStateManager(audioManager, entityNavigator);

            // Initialize entity navigation facade (filter prefs, cycling, teleport)
            entityNavFacade = new EntityNavigationFacade(entityNavigator, navigationState);
            entityNavFacade.LoadPreferences();

            // Initialize waypoint system
            waypointManager = new WaypointManager();
            waypointNavigator = new WaypointNavigator(waypointManager);
            waypointFacade = new WaypointFacade(waypointManager, waypointNavigator);

            // Initialize input manager
            inputManager = new InputManager(this);

            // Initialize mod menu
            ModMenu.Initialize();

            // Initialize menu state registry (ensures all handlers are registered)
            MenuStateRegistry.Initialize();

            // Apply manual Harmony patches for popups, save/load dialogs, naming, vehicle state, main menu, and menu state transitions
            var harmony = new HarmonyLib.Harmony("FFIV_ScreenReader.ManualPatches");
            PopupPatches.ApplyPatches(harmony);
            SaveLoadPatches.ApplyPatches(harmony);
            NamingPatches.ApplyPatches(harmony);
            MainMenuPatches.ApplyPatches(harmony);
            ItemMenuStatePatches.ApplyPatches(harmony);
            AbilityMenuStatePatches.ApplyPatches(harmony);
            ConfigMenuStatePatches.ApplyPatches(harmony);
            StatusMenuStatePatches.ApplyPatches(harmony);
            BattleCommandMessageManualPatches.ApplyManualPatches(harmony);

            // Set up callback for field ready event before applying patches
            MovementSpeechPatches.OnFieldReady = OnFieldReadyCallback;
            MovementSpeechPatches.ApplyPatches(harmony);

            // Patch game state transitions (map changes) - event-driven, no polling
            GameStatePatches.ApplyPatches(harmony);

            // Patch entity interactions for immediate entity refresh (treasure chest, dialogue end)
            TryPatchEntityInteractions(harmony);

            // Initialize fade detection for wall tone suppression during map transitions
            MapTransitionPatches.Initialize(harmony);

            // NOTE: Audio loops (wall tones, beacons) are NOT started here.
            // They are started in DelayedAudioRestart after FieldPlayerController exists
            // to avoid lag during game load.
        }

        public override void OnDeinitializeMelon()
        {
            // Unsubscribe from scene load events using stored delegate
            if (_onSceneLoadedHandler != null)
            {
                SceneManager.sceneLoaded -= _onSceneLoadedHandler;
                _onSceneLoadedHandler = null;
            }

            // Stop audio loops
            audioManager?.Shutdown();

            // Shutdown sound player (closes waveOut handles, frees unmanaged memory)
            SoundPlayer.Shutdown();

            CoroutineManager.CleanupAll();
            tolk?.Unload();
        }

        /// <summary>
        /// Called when the field is ready (via MainGame.set_FieldReady hook).
        /// Triggers entity scan so entities are available immediately when user presses navigation keys.
        /// </summary>
        private void OnFieldReadyCallback()
        {
            try
            {
                entityCache.ForceScan();
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"[FieldReady] Error during entity scan: {ex.Message}");
            }
        }

        /// <summary>
        /// Patches entity interaction methods for immediate entity refresh.
        /// Triggers rescan when treasure chests are opened or dialogue ends.
        /// </summary>
        private void TryPatchEntityInteractions(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Patch FieldTresureBox.Open() - triggers entity refresh when chest is opened
                Type treasureBoxType = typeof(FieldTresureBox);
                var openMethod = treasureBoxType.GetMethod("Open", BindingFlags.Public | BindingFlags.Instance);
                var openPostfix = typeof(Patches.EntityInteractionPatches).GetMethod("TreasureBox_Open_Postfix", BindingFlags.Public | BindingFlags.Static);

                if (openMethod != null && openPostfix != null)
                {
                    harmony.Patch(openMethod, postfix: new HarmonyMethod(openPostfix));
                }
                else
                {
                    LoggerInstance.Warning($"FieldTresureBox.Open patch failed. Method: {openMethod != null}, Postfix: {openPostfix != null}");
                }

                // Patch MessageWindowManager.Close() - triggers entity refresh when dialogue ends
                Type messageManagerType = typeof(MessageWindowManager);
                var closeMethod = messageManagerType.GetMethod("Close", BindingFlags.Public | BindingFlags.Instance);
                var closePostfix = typeof(Patches.EntityInteractionPatches).GetMethod("MessageWindow_Close_Postfix", BindingFlags.Public | BindingFlags.Static);

                if (closeMethod != null && closePostfix != null)
                {
                    harmony.Patch(closeMethod, postfix: new HarmonyMethod(closePostfix));
                }
                else
                {
                    LoggerInstance.Warning($"MessageWindowManager.Close patch failed. Method: {closeMethod != null}, Postfix: {closePostfix != null}");
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"Error patching entity interactions: {ex.Message}");
            }
        }

        /// <summary>
        /// Schedules an entity refresh after a 1-frame delay.
        /// Called by interaction hooks (treasure chest, dialogue end) to update entity states.
        /// </summary>
        internal void ScheduleEntityRefresh()
        {
            CoroutineManager.StartManaged(EntityRefreshCoroutine());
        }

        private IEnumerator EntityRefreshCoroutine()
        {
            // Wait one frame for game state to fully update
            yield return null;

            // Rescan entities to pick up state changes (e.g., chest opened)
            entityCache.ForceScan();
        }

        /// <summary>
        /// Called when a new scene is loaded.
        /// Automatically caches commonly-used Unity components to avoid expensive FindObjectOfType calls.
        /// </summary>
        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            try
            {
                // Clear speaker context on scene change to re-establish who is speaking
                DialogueTracker.ClearLastAnnouncedSpeaker();

                // Clear ALL menu states on scene change to prevent stale state from suppressing announcements
                // This fixes the issue where popups don't read on first game load
                MenuState.ClearAllMenuStates();

                // Clear stale object cache before scene transition to prevent lag
                Utils.GameObjectCache.ClearAll();

                // Stop audio loops during scene transition and suppress wall tones/beacons briefly
                audioManager.OnSceneTransition();

                // Reset footstep tracking for new map
                FootstepPatches.ResetState();

                // If we were in battle and are now loading a non-battle scene, reset battle state
                // This restores navigation settings (wall tones, footsteps, etc.) at the correct time
                if (BattleState.IsInBattle && !scene.name.Contains("Battle"))
                {
                    BattleState.Reset();
                }

                // Try to find and cache FieldPlayerController
                var playerController = UnityEngine.Object.FindObjectOfType<Il2CppLast.Map.FieldPlayerController>();
                if (playerController != null)
                {
                    Utils.GameObjectCache.Register(playerController);
                }

                // Try to find and cache FieldMap
                var fieldMap = UnityEngine.Object.FindObjectOfType<Il2Cpp.FieldMap>();
                if (fieldMap != null)
                {
                    Utils.GameObjectCache.Register(fieldMap);
                }

                // Skip audio restart for battle scenes (belt-and-suspenders with DelayedAudioRestart check)
                if (BattleState.IsInBattle || scene.name.Contains("Battle"))
                {
                    return;
                }

                // Restart audio loops after scene has settled (if enabled)
                if (audioManager.NeedsAudioRestart)
                {
                    CoroutineManager.StartManaged(audioManager.DelayedAudioRestart());
                }
            }
            catch (System.Exception ex)
            {
                LoggerInstance.Error($"[ComponentCache] Error in OnSceneLoaded: {ex.Message}");
            }
        }


        public override void OnUpdate()
        {
            // Handle all input
            inputManager.Update();
        }

        /// <summary>
        /// Forces an entity rescan. Called from GameStatePatches on map transitions.
        /// </summary>
        public void ForceEntityRescan()
        {
            entityCache?.ForceScan();
        }

        /// <summary>
        /// Check if the current map is a world map (overworld, underworld, moon surface).
        /// </summary>
        public bool IsCurrentMapWorldMap()
        {
            try
            {
                var fieldMap = Utils.GameObjectCache.Get<Il2Cpp.FieldMap>();
                if (fieldMap?.fieldController?.mapManager?.CurrentMapModel != null)
                {
                    return fieldMap.fieldController.mapManager.CurrentMapModel.IsAreaTypeWorld;
                }
            }
            catch (System.Exception ex)
            {
                LoggerInstance.Warning($"Error checking world map: {ex.Message}");
            }
            return false;
        }

        #region Audio Toggle Methods (delegates to AudioLoopManager)

        internal void ToggleWallTones() => audioManager.ToggleWallTones();
        internal void ToggleFootsteps() => audioManager.ToggleFootsteps();
        internal void ToggleAudioBeacons() => audioManager.ToggleAudioBeacons();

        // Public static accessors for filter settings (used by ModMenu, BattleState)
        public static bool PathfindingFilterEnabled => Instance?.navigationState?.FilterByPathfinding ?? false;
        public static bool MapExitFilterEnabled => EntityNavigationFacade.MapExitFilterEnabled;
        public static bool ToLayerFilterEnabled => EntityNavigationFacade.ToLayerFilterEnabled;

        #endregion

        /// <summary>
        /// Speak text through the screen reader.
        /// Thread-safe: TolkWrapper uses locking to prevent concurrent native calls.
        /// </summary>
        /// <param name="text">Text to speak</param>
        /// <param name="interrupt">Whether to interrupt current speech (true for user actions, false for game events)</param>
        public static void SpeakText(string text, bool interrupt = true)
        {
            tolk?.Speak(text, interrupt);
        }
    }
}
