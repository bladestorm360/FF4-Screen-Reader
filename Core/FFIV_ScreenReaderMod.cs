using MelonLoader;
using HarmonyLib;
using FFIV_ScreenReader.Utils;
using static FFIV_ScreenReader.Utils.ModTextTranslator;
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

        // Facades (public so ControllerRouter can drive them)
        public EntityNavigationFacade entityNavFacade;
        public WaypointFacade waypointFacade;

        // Controller normalization (L3/R3 pass-through to game when not in mod mode)
        private bool enableStickClickNormalization = false;

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

            // Initialize mod text translator for localization
            ModTextTranslator.Initialize();

            // Initialize preferences
            PreferencesManager.Initialize();

            // Initialize Tolk for screen reader support
            tolk = new TolkWrapper();
            tolk.Load();

            // Initialize external sound player for distinct audio feedback (wall bumps, tones, footsteps)
            SoundPlayer.Initialize();

            // Initialize SDL3 gamepad/keyboard input manager
            GamepadManager.Initialize();

            // Load stick click normalization preference
            enableStickClickNormalization = PreferencesManager.StickClickNormalizationEnabled;

            // Initialize entity name translator for Japanese-to-English entity names
            EntityTranslator.Initialize();

            // Initialize entity cache and navigator (event-driven, no timer)
            entityCache = new EntityCache();
            entityNavigator = new EntityNavigator(entityCache);

            // Initialize waypoint system FIRST so AudioLoopManager can target waypoints
            waypointManager = new WaypointManager();
            waypointNavigator = new WaypointNavigator(waypointManager);

            // Initialize audio feedback manager (now with waypointNavigator for beacon targeting).
            // No preference preload needed — the loops read PreferencesManager live (single source of truth).
            audioManager = new AudioLoopManager(entityNavigator, entityCache, waypointNavigator);

            // Initialize navigation state manager (battle/dialogue suppression)
            navigationState = new NavigationStateManager(audioManager, entityNavigator);

            // Initialize entity navigation facade (filter prefs, cycling, teleport)
            entityNavFacade = new EntityNavigationFacade(entityNavigator, navigationState);
            entityNavFacade.LoadPreferences();

            // Initialize waypoint facade
            waypointFacade = new WaypointFacade(waypointManager, waypointNavigator);

            // Initialize input manager
            inputManager = new InputManager(this);

            // Initialize mod menu
            ModMenu.Initialize();

            // Initialize menu state registry (ensures all handlers are registered)
            MenuStateRegistry.Initialize();

            // Apply manual Harmony patches for popups, save/load dialogs, naming, vehicle state, main menu, and menu state transitions
            var harmony = new HarmonyLib.Harmony("FFIV_ScreenReader.ManualPatches");
            InputPassthroughPatches.ApplyPatches(harmony);
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

            // Game's own walk/run (F1) and encounter (F3) toggles, from the game's setters
            GameTogglePatches.ApplyPatches(harmony);


            // Initialize fade detection for wall tone suppression during map transitions
            MapTransitionPatches.Initialize(harmony);

            // NOTE: Audio loops (wall tones, beacons) are NOT started here.
            // AudioLoopManager.RestartLoopsIfOnField starts them once a FieldPlayerController
            // exists (scene load on the field, or MainGame.set_FieldReady) to avoid lag during game load.
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

            // Shutdown SDL3 gamepad/keyboard input
            GamepadManager.Shutdown();

            // Shutdown sound player (destroys SDL audio streams + device, frees scratch buffer)
            SoundPlayer.Shutdown();

            CoroutineManager.CleanupAll();
            tolk?.Unload();
        }

        /// <summary>
        /// Called when the field is ready (via MainGame.set_FieldReady hook).
        /// Triggers entity scan so entities are available immediately when user presses navigation keys,
        /// and (re)starts the enabled audio loops: the field player exists from this moment, which is
        /// what the old 0.5 s DelayedAudioRestart after every scene load waited for.
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

            try
            {
                if (Utils.GameObjectCache.Get<Il2CppLast.Map.FieldPlayerController>() == null)
                    Utils.GameObjectCache.Refresh<Il2CppLast.Map.FieldPlayerController>();
                audioManager?.RestartLoopsIfOnField();
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"[FieldReady] Error restarting audio loops: {ex.Message}");
            }
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

                // A message window destroyed by a scene change never reaches MessageWindowManager.Close,
                // which would leave dialogue mode stuck (navigation audio muted, controller mod mode
                // preferring dialogue). Reset it here, but only when no window is actually open, so an
                // additive scene load mid-dialogue can't wipe the pages being read. Runs before
                // OnSceneTransition so the loops Reset restarts are stopped and restarted cleanly below.
                if (DialogueTracker.IsInDialogue && !IsMessageWindowOpen())
                {
                    DialogueTracker.Reset();
                }

                // Clear ALL menu states on scene change to prevent stale state from suppressing announcements
                // This fixes the issue where popups don't read on first game load
                MenuState.ClearAllMenuStates();

                // Clear stale object cache before scene transition to prevent lag
                Utils.GameObjectCache.ClearAll();

                // Stop audio loops during scene transition and suppress wall tones/beacons briefly
                audioManager.OnSceneTransition();

                // Reset footstep tracking for new map
                FootstepPatches.ResetState();

                // Drop the cached battle pause controller (it belongs to the old scene)
                BattlePausePatches.Reset();

                // Drop the popups whose Yes/No moves are matched by cursor (they belong to the old scene)
                SaveLoadPatches.ResetSceneState();
                PopupPatches.ResetSceneState();

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

                // Skip audio restart for battle scenes (RestartLoopsIfOnField also checks)
                if (BattleState.IsInBattle || scene.name.Contains("Battle"))
                {
                    return;
                }

                // Restart audio loops now if the field player already exists (an additive load on the
                // field); otherwise MainGame.set_FieldReady(true) restarts them (OnFieldReadyCallback).
                // The loops stay silent for the first second after OnSceneTransition either way.
                audioManager.RestartLoopsIfOnField();
            }
            catch (System.Exception ex)
            {
                LoggerInstance.Error($"[ComponentCache] Error in OnSceneLoaded: {ex.Message}");
            }
        }

        /// <summary>
        /// Whether the game's message window is currently shown. MessageWindowManager.IsOpen checks
        /// currentWindowController (Unity-null once destroyed), isPlaying and the window's active state.
        /// </summary>
        private static bool IsMessageWindowOpen()
        {
            try
            {
                var manager = Il2CppLast.Message.MessageWindowManager.Instance;
                return manager != null && manager.IsOpen();
            }
            catch
            {
                return false; // Manager torn down with the scene
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

        /// <summary>
        /// Forces the audio beacon to ping immediately on the next loop iteration.
        /// Called by pathfinding commands when beacon navigation mode is enabled.
        /// </summary>
        internal void RestartBeacon() => audioManager?.RestartBeacon();

        /// <summary>
        /// Toggles stick click normalization mode.
        /// When on, L3/R3 fall through to the game (auto-dash, encounter toggle);
        /// mod functions move to mod mode.
        /// </summary>
        internal void ToggleStickClickNormalization()
        {
            enableStickClickNormalization = !enableStickClickNormalization;
            PreferencesManager.SaveStickClickNormalization(enableStickClickNormalization);
            SpeakText(string.Format(T("Stick click normalization {0}"),
                enableStickClickNormalization ? T("on") : T("off")), interrupt: true);
        }

        /// <summary>
        /// Whether stick click normalization is enabled (L3/R3 pass through to game).
        /// </summary>
        public static bool StickClickNormalizationEnabled =>
            Instance?.enableStickClickNormalization ?? false;

        /// <summary>
        /// Toggles the "Beacon Destination Announcement" feature. When on, restarting the
        /// audio beacon at the current nav target also re-speaks the destination name.
        /// </summary>
        internal void ToggleAnnounceOnBeaconRestart()
        {
            bool newValue = !PreferencesManager.AnnounceOnBeaconRestartEnabled;
            PreferencesManager.SaveAnnounceOnBeaconRestart(newValue);
            SpeakText(string.Format(T("Beacon destination announcement {0}"),
                newValue ? T("on") : T("off")), interrupt: true);
        }

        /// <summary>
        /// Whether restarting the beacon should also re-speak the current destination.
        /// </summary>
        public static bool AnnounceOnBeaconRestartEnabled => PreferencesManager.AnnounceOnBeaconRestartEnabled;

        /// <summary>
        /// Toggles the "Auto Detail" feature (F7 / mod menu). When on, focusing an item, spell,
        /// battle list entry or shop entry also reads the description normally reached with the
        /// details key; when off, the description is only read on the details key.
        /// </summary>
        internal void ToggleAutoDetail()
        {
            bool newValue = !PreferencesManager.AutoDetailEnabled;
            PreferencesManager.SaveAutoDetail(newValue);
            SpeakText(string.Format(T("Auto detail {0}"),
                newValue ? T("on") : T("off")), interrupt: true);
        }

        /// <summary>
        /// Whether menu focus should automatically read the details-key description.
        /// </summary>
        public static bool AutoDetailEnabled => PreferencesManager.AutoDetailEnabled;

        /// <summary>
        /// Whether the EXP counter sound plays while the EXP bar animates on battle results.
        /// </summary>
        public static bool ExpCounterEnabled => PreferencesManager.ExpCounterEnabled;

        /// <summary>
        /// Toggles the "EXP Counter Sound" feature. Prefs-backed; the mod menu re-announces the
        /// new state, so no extra speech here (mirrors the FF5 mod).
        /// </summary>
        public static void ToggleExpCounter()
        {
            bool newValue = !PreferencesManager.ExpCounterEnabled;
            PreferencesManager.SaveExpCounter(newValue);
        }

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

        /// <summary>
        /// Stops current speech without speaking anything (controller button/navigation interrupt).
        /// </summary>
        public static void InterruptSpeech()
        {
            tolk?.Silence();
        }
    }
}
