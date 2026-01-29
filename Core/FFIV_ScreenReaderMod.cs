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

[assembly: MelonInfo(typeof(FFIV_ScreenReader.Core.FFIV_ScreenReaderMod), "FFIV Screen Reader", "1.0.0", "Zachary Kline")]
[assembly: MelonGame("SQUARE ENIX, Inc.", "FINAL FANTASY IV")]

namespace FFIV_ScreenReader.Core
{
    /// <summary>
    /// Entity category for filtering navigation targets
    /// </summary>
    public enum EntityCategory
    {
        All = 0,
        Chests = 1,
        NPCs = 2,
        MapExits = 3,
        Events = 4,
        Vehicles = 5
    }

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

        // Static instance for access from patches
        internal static FFIV_ScreenReaderMod Instance { get; private set; }

        // Stored delegate for proper event unsubscription (fixes memory leak)
        private static UnityAction<Scene, LoadSceneMode> _onSceneLoadedHandler;

        // Category count derived from enum for safe cycling
        private static readonly int CategoryCount = System.Enum.GetValues(typeof(EntityCategory)).Length;

        // Pathfinding filter toggle
        private bool filterByPathfinding = false;

        // Map exit filter toggle
        private bool filterMapExits = false;

        // Audio feedback toggles
        private bool enableWallTones = false;
        private bool enableFootsteps = false;
        private bool enableAudioBeacons = false;

        // Coroutine-based wall tone loop
        private IEnumerator wallToneCoroutine = null;
        private const float WALL_TONE_LOOP_INTERVAL = 0.1f;

        // Coroutine-based audio beacon loop
        private IEnumerator beaconCoroutine = null;
        private const float BEACON_INTERVAL = 2.0f;

        // Map transition suppression for wall tones
        private int wallToneMapId = -1;
        private float wallToneSuppressedUntil = 0f;

        // Map transition suppression for beacons
        private float beaconSuppressedUntil = 0f;

        // Reusable direction list buffer to avoid per-cycle allocations
        private static readonly List<SoundPlayer.Direction> wallDirectionsBuffer = new List<SoundPlayer.Direction>(4);

        // Preferences
        private static MelonPreferences_Category prefsCategory;
        private static MelonPreferences_Entry<bool> prefPathfindingFilter;
        private static MelonPreferences_Entry<bool> prefMapExitFilter;
        private static MelonPreferences_Entry<bool> prefWallTones;
        private static MelonPreferences_Entry<bool> prefFootsteps;
        private static MelonPreferences_Entry<bool> prefAudioBeacons;

        // Volume controls (0-100, default 50)
        private static MelonPreferences_Entry<int> prefWallBumpVolume;
        private static MelonPreferences_Entry<int> prefFootstepVolume;
        private static MelonPreferences_Entry<int> prefWallToneVolume;
        private static MelonPreferences_Entry<int> prefBeaconVolume;

        // Pre-cached direction vectors for map exit checks (avoids per-cycle Vector3 allocations)
        private static readonly Vector3 DirNorth = new Vector3(0, 16, 0);
        private static readonly Vector3 DirSouth = new Vector3(0, -16, 0);
        private static readonly Vector3 DirEast = new Vector3(16, 0, 0);
        private static readonly Vector3 DirWest = new Vector3(-16, 0, 0);

        // Beacon debouncing
        private float lastBeaconPlayedAt = 0f;

        public override void OnInitializeMelon()
        {
            Instance = this;
            LoggerInstance.Msg("FFIV Screen Reader Mod loaded!");

            // Subscribe to scene load events for automatic component caching
            // Store delegate as field to ensure proper unsubscription
            _onSceneLoadedHandler = (UnityAction<Scene, LoadSceneMode>)OnSceneLoaded;
            SceneManager.sceneLoaded += _onSceneLoadedHandler;

            // Initialize preferences
            prefsCategory = MelonPreferences.CreateCategory("FFIV_ScreenReader");
            prefPathfindingFilter = prefsCategory.CreateEntry<bool>("PathfindingFilter", false, "Pathfinding Filter", "Only show entities with valid paths when cycling");
            prefMapExitFilter = prefsCategory.CreateEntry<bool>("MapExitFilter", false, "Map Exit Filter", "Filter multiple map exits to the same destination, showing only the closest one");
            prefWallTones = prefsCategory.CreateEntry<bool>("WallTones", false, "Wall Tones", "Play directional tones when approaching walls");
            prefFootsteps = prefsCategory.CreateEntry<bool>("Footsteps", false, "Footsteps", "Play click sound on each tile movement");
            prefAudioBeacons = prefsCategory.CreateEntry<bool>("AudioBeacons", false, "Audio Beacons", "Play directional pings toward selected entity");

            // Volume controls (0-100, default 50)
            prefWallBumpVolume = prefsCategory.CreateEntry<int>("WallBumpVolume", 50, "Wall Bump Volume", "Volume for wall bump sounds (0-100)");
            prefFootstepVolume = prefsCategory.CreateEntry<int>("FootstepVolume", 50, "Footstep Volume", "Volume for footstep sounds (0-100)");
            prefWallToneVolume = prefsCategory.CreateEntry<int>("WallToneVolume", 50, "Wall Tone Volume", "Volume for wall proximity tones (0-100)");
            prefBeaconVolume = prefsCategory.CreateEntry<int>("BeaconVolume", 50, "Beacon Volume", "Volume for audio beacon pings (0-100)");

            // Load saved preferences
            filterByPathfinding = prefPathfindingFilter.Value;
            filterMapExits = prefMapExitFilter.Value;
            enableWallTones = prefWallTones.Value;
            enableFootsteps = prefFootsteps.Value;
            enableAudioBeacons = prefAudioBeacons.Value;

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
            entityNavigator.FilterByPathfinding = filterByPathfinding;
            entityNavigator.FilterMapExits = filterMapExits;

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
            StopWallToneLoop();
            StopBeaconLoop();

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
                LoggerInstance.Msg("[FieldReady] Triggering initial entity scan");
                entityCache.ForceScan();
                LoggerInstance.Msg($"[FieldReady] Entity scan complete, found {entityCache.Entities.Count} entities");
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
                var openPostfix = typeof(EntityInteractionPatches).GetMethod("TreasureBox_Open_Postfix", BindingFlags.Public | BindingFlags.Static);

                if (openMethod != null && openPostfix != null)
                {
                    harmony.Patch(openMethod, postfix: new HarmonyMethod(openPostfix));
                    LoggerInstance.Msg("Patched FieldTresureBox.Open for entity refresh");
                }
                else
                {
                    LoggerInstance.Warning($"FieldTresureBox.Open patch failed. Method: {openMethod != null}, Postfix: {openPostfix != null}");
                }

                // Patch MessageWindowManager.Close() - triggers entity refresh when dialogue ends
                Type messageManagerType = typeof(MessageWindowManager);
                var closeMethod = messageManagerType.GetMethod("Close", BindingFlags.Public | BindingFlags.Instance);
                var closePostfix = typeof(EntityInteractionPatches).GetMethod("MessageWindow_Close_Postfix", BindingFlags.Public | BindingFlags.Static);

                if (closeMethod != null && closePostfix != null)
                {
                    harmony.Patch(closeMethod, postfix: new HarmonyMethod(closePostfix));
                    LoggerInstance.Msg("Patched MessageWindowManager.Close for entity refresh");
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
            LoggerInstance.Msg("[EntityRefresh] Rescanned entities after interaction");
        }

        /// <summary>
        /// Called when a new scene is loaded.
        /// Automatically caches commonly-used Unity components to avoid expensive FindObjectOfType calls.
        /// </summary>
        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            try
            {
                LoggerInstance.Msg($"[ComponentCache] Scene loaded: {scene.name}");

                // Clear speaker context on scene change to re-establish who is speaking
                DialogueTracker.ClearLastAnnouncedSpeaker();

                // Reset location message tracker to prevent stale FadeMessage from blocking new locations
                LocationMessageTracker.Reset();

                // Clear ALL menu states on scene change to prevent stale state from suppressing announcements
                // This fixes the issue where popups don't read on first game load
                MenuState.ClearAllMenuStates();

                // Clear stale object cache before scene transition to prevent lag
                Utils.GameObjectCache.ClearAll();

                // Stop audio loops during scene transition and suppress wall tones/beacons briefly
                StopWallToneLoop();
                StopBeaconLoop();
                wallToneSuppressedUntil = Time.time + 1.0f;
                beaconSuppressedUntil = Time.time + 1.0f;

                // Reset footstep tracking for new map
                FootstepPatches.ResetState();

                // Try to find and cache FieldPlayerController
                var playerController = UnityEngine.Object.FindObjectOfType<Il2CppLast.Map.FieldPlayerController>();
                if (playerController != null)
                {
                    Utils.GameObjectCache.Register(playerController);
                    LoggerInstance.Msg($"[ComponentCache] Cached FieldPlayerController: {playerController.gameObject?.name}");
                }
                else
                {
                    LoggerInstance.Msg("[ComponentCache] No FieldPlayerController found in scene");
                }

                // Try to find and cache FieldMap
                var fieldMap = UnityEngine.Object.FindObjectOfType<Il2Cpp.FieldMap>();
                if (fieldMap != null)
                {
                    Utils.GameObjectCache.Register(fieldMap);
                    LoggerInstance.Msg($"[ComponentCache] Cached FieldMap: {fieldMap.gameObject?.name}");
                }
                else
                {
                    LoggerInstance.Msg("[ComponentCache] No FieldMap found in scene");
                }

                // Restart audio loops after scene has settled (if enabled)
                if (enableWallTones || enableAudioBeacons)
                {
                    CoroutineManager.StartManaged(DelayedAudioRestart());
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

        /// <summary>
        /// Checks if player is on an active field map.
        /// Returns true if on valid map (ready for entity navigation), false otherwise.
        /// Note: Entity scan is now triggered automatically via MainGame.set_FieldReady hook.
        /// </summary>
        private bool EnsureFieldContextAndScan()
        {
            // Check if FieldMap exists and is active
            var fieldMap = Utils.GameObjectCache.Get<Il2Cpp.FieldMap>();
            if (fieldMap == null || !fieldMap.gameObject.activeInHierarchy)
            {
                SpeakText("Not on map");
                return false;
            }

            // Check if player controller exists
            var playerController = Utils.GameObjectCache.Get<FieldPlayerController>();
            if (playerController?.fieldPlayer == null)
            {
                SpeakText("Not on map");
                return false;
            }

            return true;
        }

        internal void AnnounceCurrentEntity()
        {
            var entity = entityNavigator.CurrentEntity;
            if (entity == null)
            {
                SpeakText("No entities nearby");
                return;
            }

            var playerController = Utils.GameObjectCache.Get<FieldPlayerController>();
            if (playerController?.fieldPlayer == null)
            {
                SpeakText("Not in field");
                return;
            }

            // CRITICAL: Touch controller uses localPosition, NOT position!
            Vector3 playerPos = playerController.fieldPlayer.transform.localPosition;
            Vector3 targetPos = entity.GameEntity.transform.localPosition;

            var pathInfo = Field.FieldNavigationHelper.FindPathTo(
                playerPos,
                targetPos,
                playerController.mapHandle,
                playerController.fieldPlayer
            );

            string announcement;
            if (pathInfo.Success)
            {
                // Just announce the path - user knows what entity they're navigating to from cycling
                announcement = $"{pathInfo.Description}";
            }
            else
            {
                announcement = "no path";
            }

            SpeakText(announcement);
        }

        internal void CycleNext()
        {
            // Ensure we're on a valid map and trigger scan if needed
            if (!EnsureFieldContextAndScan())
                return;

            if (entityNavigator.CycleNext())
            {
                AnnounceEntityOnly();
            }
            else
            {
                // Either no entities or no pathable entities found
                if (entityNavigator.EntityCount == 0)
                {
                    SpeakText("No entities nearby");
                }
                else
                {
                    SpeakText("No pathable entities found");
                }
            }
        }

        internal void CyclePrevious()
        {
            // Ensure we're on a valid map and trigger scan if needed
            if (!EnsureFieldContextAndScan())
                return;

            if (entityNavigator.CyclePrevious())
            {
                AnnounceEntityOnly();
            }
            else
            {
                // Either no entities or no pathable entities found
                if (entityNavigator.EntityCount == 0)
                {
                    SpeakText("No entities nearby");
                }
                else
                {
                    SpeakText("No pathable entities found");
                }
            }
        }

        internal void AnnounceEntityOnly()
        {
            var entity = entityNavigator.CurrentEntity;
            if (entity == null)
            {
                SpeakText("No entities nearby");
                return;
            }

            var playerController = Utils.GameObjectCache.Get<FieldPlayerController>();
            if (playerController?.fieldPlayer == null)
            {
                SpeakText("Not in field");
                return;
            }

            // Use localPosition for pathfinding (matches touch controller behavior)
            Vector3 playerPos = playerController.fieldPlayer.transform.localPosition;
            Vector3 targetPos = entity.GameEntity.transform.localPosition;

            string formatted = entity.FormatDescription(playerController.fieldPlayer.transform.position);

            // Check if path exists
            var pathInfo = Field.FieldNavigationHelper.FindPathTo(
                playerPos,
                targetPos,
                playerController.mapHandle,
                playerController.fieldPlayer
            );

            // Announce entity info + path status + count at the end
            string countSuffix = $", {entityNavigator.CurrentIndex + 1} of {entityNavigator.EntityCount}";
            string pathStatus = pathInfo.Success ? "" : ", no path";
            string announcement = $"{formatted}{pathStatus}{countSuffix}";
            SpeakText(announcement);
        }

        internal void CycleNextCategory()
        {
            // Ensure we're on a valid map and trigger scan if needed
            if (!EnsureFieldContextAndScan())
                return;

            // Cycle to next category
            int nextCategory = ((int)entityNavigator.CurrentCategory + 1) % CategoryCount;
            EntityCategory newCategory = (EntityCategory)nextCategory;

            // Update navigator category (automatically rebuilds list)
            entityNavigator.SetCategory(newCategory);

            // Announce new category and count
            AnnounceCategoryChange();
        }

        internal void CyclePreviousCategory()
        {
            // Ensure we're on a valid map and trigger scan if needed
            if (!EnsureFieldContextAndScan())
                return;

            // Cycle to previous category
            int prevCategory = (int)entityNavigator.CurrentCategory - 1;
            if (prevCategory < 0)
                prevCategory = CategoryCount - 1;

            EntityCategory newCategory = (EntityCategory)prevCategory;

            // Update navigator category (automatically rebuilds list)
            entityNavigator.SetCategory(newCategory);

            // Announce new category and count
            AnnounceCategoryChange();
        }

        internal void ResetToAllCategory()
        {
            // Ensure we're on a valid map and trigger scan if needed
            if (!EnsureFieldContextAndScan())
                return;

            if (entityNavigator.CurrentCategory == EntityCategory.All)
            {
                SpeakText("Already in All category");
                return;
            }

            // Update navigator category (automatically rebuilds list)
            entityNavigator.SetCategory(EntityCategory.All);

            // Announce category change
            AnnounceCategoryChange();
        }

        internal void TogglePathfindingFilter()
        {
            filterByPathfinding = !filterByPathfinding;

            // Update navigator
            entityNavigator.FilterByPathfinding = filterByPathfinding;

            // Save to preferences
            prefPathfindingFilter.Value = filterByPathfinding;
            prefsCategory.SaveToFile(false);

            string status = filterByPathfinding ? "on" : "off";
            SpeakText($"Pathfinding filter {status}");
        }

        internal void ToggleMapExitFilter()
        {
            filterMapExits = !filterMapExits;

            // Update navigator and rebuild list
            entityNavigator.FilterMapExits = filterMapExits;
            entityNavigator.RebuildNavigationList();

            // Save to preferences
            prefMapExitFilter.Value = filterMapExits;
            prefsCategory.SaveToFile(false);

            string status = filterMapExits ? "on" : "off";
            SpeakText($"Map exit filter {status}");
        }

        private void AnnounceCategoryChange()
        {
            string categoryName = EntityNavigator.GetCategoryName(entityNavigator.CurrentCategory);
            int entityCount = entityNavigator.EntityCount;

            string announcement = $"Category: {categoryName}, {entityCount} {(entityCount == 1 ? "entity" : "entities")}";
            SpeakText(announcement);
        }

        internal void TeleportInDirection(Vector2 offset)
        {
            var entity = entityNavigator.CurrentEntity;
            if (entity == null)
            {
                SpeakText("No entity selected");
                return;
            }

            var playerController = Utils.GameObjectCache.Get<Il2CppLast.Map.FieldPlayerController>();
            if (playerController?.fieldPlayer == null)
            {
                SpeakText("Player not available");
                return;
            }

            var player = playerController.fieldPlayer;

            // Calculate offset position relative to the target entity
            // One cell = 16 units
            Vector3 targetPos = entity.Position;
            Vector3 newPos = new Vector3(targetPos.x + offset.x, targetPos.y + offset.y, targetPos.z);

            // Instantly teleport by setting localPosition directly
            player.transform.localPosition = newPos;

            // Announce direction
            string direction = GetDirectionName(offset);
            SpeakText($"Teleported {direction} of {entity.Name}");
            LoggerInstance.Msg($"Teleported {direction} of {entity.Name} to position {newPos}");
        }

        private string GetDirectionName(Vector2 offset)
        {
            if (offset.y > 0) return "north";
            if (offset.y < 0) return "south";
            if (offset.x < 0) return "west";
            if (offset.x > 0) return "east";
            return "unknown";
        }

        internal void AnnounceCurrentCharacterStatus()
        {
            try
            {
                // Get the currently active character from the battle patch
                var activeCharacter = FFIV_ScreenReader.Patches.BattleMenuController_SetCommandSelectTarget_Patch.CurrentActiveCharacter;

                if (activeCharacter == null)
                {
                    SpeakText("Not in battle or no active character");
                    return;
                }

                if (activeCharacter.ownedCharacterData == null)
                {
                    SpeakText("No character data available");
                    return;
                }

                var charData = activeCharacter.ownedCharacterData;
                string characterName = charData.Name;

                // Read HP/MP directly from character parameter
                if (charData.parameter == null)
                {
                    SpeakText($"{characterName}, status information not available");
                    return;
                }

                var param = charData.parameter;
                var statusParts = new System.Collections.Generic.List<string>();
                statusParts.Add(characterName);

                // Add HP
                int currentHP = param.CurrentHP;
                int maxHP = param.ConfirmedMaxHp();
                statusParts.Add($"HP {currentHP} of {maxHP}");

                // Add MP
                int currentMP = param.CurrentMP;
                int maxMP = param.ConfirmedMaxMp();
                statusParts.Add($"MP {currentMP} of {maxMP}");

                // Add status conditions
                if (param.CurrentConditionList != null && param.CurrentConditionList.Count > 0)
                {
                    var conditionNames = new System.Collections.Generic.List<string>();
                    foreach (var condition in param.CurrentConditionList)
                    {
                        if (condition != null)
                        {
                            // Get the condition name from the message ID
                            string conditionMesId = condition.MesIdName;
                            if (!string.IsNullOrEmpty(conditionMesId))
                            {
                                var messageManager = Il2CppLast.Management.MessageManager.Instance;
                                if (messageManager != null)
                                {
                                    string conditionName = messageManager.GetMessage(conditionMesId);
                                    if (!string.IsNullOrEmpty(conditionName))
                                    {
                                        conditionNames.Add(conditionName);
                                    }
                                }
                            }
                        }
                    }

                    if (conditionNames.Count > 0)
                    {
                        statusParts.Add("Status: " + string.Join(", ", conditionNames));
                    }
                }

                string statusMessage = string.Join(", ", statusParts);
                SpeakText(statusMessage);
            }
            catch (System.Exception ex)
            {
                LoggerInstance.Warning($"Error announcing character status: {ex.Message}");
                SpeakText("Error reading character status");
            }
        }

        internal void AnnounceGilAmount()
        {
            try
            {
                var userDataManager = Il2CppLast.Management.UserDataManager.Instance();

                if (userDataManager == null)
                {
                    SpeakText("User data not available");
                    return;
                }

                int gil = userDataManager.OwendGil;
                string gilMessage = $"{gil:N0} gil";

                SpeakText(gilMessage);
            }
            catch (System.Exception ex)
            {
                LoggerInstance.Warning($"Error announcing gil amount: {ex.Message}");
                SpeakText("Error reading gil amount");
            }
        }

        internal void AnnounceCurrentMap()
        {
            try
            {
                string mapName = Field.MapNameResolver.GetCurrentMapName();
                SpeakText(mapName);
            }
            catch (System.Exception ex)
            {
                LoggerInstance.Warning($"Error announcing current map: {ex.Message}");
                SpeakText("Error reading map name");
            }
        }

        #region Audio Loop Management

        /// <summary>
        /// Starts the wall tone coroutine loop. Safe to call if already running (no-op).
        /// </summary>
        private void StartWallToneLoop()
        {
            if (!enableWallTones) return;  // Don't start if disabled
            if (wallToneCoroutine != null) return;
            wallToneCoroutine = WallToneLoop();
            CoroutineManager.StartManaged(wallToneCoroutine);
        }

        /// <summary>
        /// Stops the wall tone coroutine loop and silences any playing tone.
        /// </summary>
        private void StopWallToneLoop()
        {
            if (wallToneCoroutine != null)
            {
                CoroutineManager.StopManaged(wallToneCoroutine);
                wallToneCoroutine = null;
            }
            if (SoundPlayer.IsWallTonePlaying())
                SoundPlayer.StopWallTone();
        }

        /// <summary>
        /// Coroutine that delays audio loop restart after scene load to let map settle.
        /// Only starts loops if FieldPlayerController exists (valid field scene).
        /// </summary>
        private IEnumerator DelayedAudioRestart()
        {
            yield return new WaitForSeconds(0.5f);

            // Only start loops if on valid field (FieldPlayerController exists)
            var playerController = Utils.GameObjectCache.Get<FieldPlayerController>();
            if (playerController == null)
                playerController = Utils.GameObjectCache.Refresh<FieldPlayerController>();

            if (playerController != null)
            {
                if (enableWallTones) StartWallToneLoop();
                if (enableAudioBeacons) StartBeaconLoop();
            }
        }

        /// <summary>
        /// Starts the audio beacon coroutine loop. Safe to call if already running (no-op).
        /// </summary>
        private void StartBeaconLoop()
        {
            if (!enableAudioBeacons) return;  // Don't start if disabled
            if (beaconCoroutine != null) return;
            beaconCoroutine = BeaconLoop();
            CoroutineManager.StartManaged(beaconCoroutine);
        }

        /// <summary>
        /// Stops the audio beacon coroutine loop.
        /// </summary>
        private void StopBeaconLoop()
        {
            if (beaconCoroutine != null)
            {
                CoroutineManager.StopManaged(beaconCoroutine);
                beaconCoroutine = null;
            }
        }

        /// <summary>
        /// Coroutine loop that pings toward the selected entity every 2 seconds.
        /// Uses manual time-based waiting for IL2CPP compatibility.
        /// Exits when enableAudioBeacons becomes false.
        /// </summary>
        private IEnumerator BeaconLoop()
        {
            float nextBeaconTime = Time.time + 0.3f;  // Delay first beacon by 300ms for scene stability

            while (enableAudioBeacons)  // Exit when disabled
            {
                // Manual time-based waiting (WaitForSeconds doesn't work reliably in IL2CPP wrapper)
                if (Time.time < nextBeaconTime)
                {
                    yield return null;
                    continue;
                }
                nextBeaconTime = Time.time + BEACON_INTERVAL;

                // Suppress beacons briefly after scene load (same pattern as wall tones)
                if (Time.time < beaconSuppressedUntil)
                    continue;

                try
                {
                    var entity = entityNavigator?.CurrentEntity;
                    if (entity == null) continue;

                    var playerController = Utils.GameObjectCache.Get<FieldPlayerController>();
                    if (playerController?.fieldPlayer == null) continue;

                    Vector3 playerPos = playerController.fieldPlayer.transform.localPosition;
                    Vector3 entityPos = entity.Position;

                    // Sanity check: skip if positions look invalid (garbage data during load)
                    if (float.IsNaN(playerPos.x) || float.IsNaN(entityPos.x) ||
                        Mathf.Abs(playerPos.x) > 10000f || Mathf.Abs(entityPos.x) > 10000f)
                        continue;

                    float distance = Vector3.Distance(playerPos, entityPos);
                    float maxDist = 500f;
                    float volumeScale = Mathf.Clamp(1f - (distance / maxDist), 0.15f, 0.60f);

                    float deltaX = entityPos.x - playerPos.x;
                    float pan = Mathf.Clamp(deltaX / 100f, -1f, 1f) * 0.5f + 0.5f;

                    bool isSouth = entityPos.y < playerPos.y - 8f;

                    // Debounce: ensure minimum interval between beacons (protects against timing issues on first load)
                    float timeSinceLast = Time.time - lastBeaconPlayedAt;
                    if (timeSinceLast < BEACON_INTERVAL * 0.8f)  // 80% of interval = 1.6s minimum
                        continue;

                    SoundPlayer.PlayBeacon(isSouth, pan, volumeScale);
                    lastBeaconPlayedAt = Time.time;
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[Beacon] Error: {ex.Message}");
                }
            }

            // Clean up when exiting
            beaconCoroutine = null;
        }

        /// <summary>
        /// Coroutine loop that checks for adjacent walls every 100ms and plays looping tones.
        /// Uses manual time-based waiting for IL2CPP compatibility.
        /// Exits when enableWallTones becomes false.
        /// </summary>
        private IEnumerator WallToneLoop()
        {
            float nextCheckTime = Time.time + 0.3f;  // Delay first check by 300ms for scene stability

            while (enableWallTones)  // Exit when disabled
            {
                // Manual time-based waiting (WaitForSeconds doesn't work reliably in IL2CPP wrapper)
                if (Time.time < nextCheckTime)
                {
                    yield return null;
                    continue;
                }
                nextCheckTime = Time.time + WALL_TONE_LOOP_INTERVAL;

                try
                {
                    float currentTime = Time.time;

                    // Detect sub-map transitions and suppress tones briefly
                    int currentMapId = GetCurrentMapId();
                    if (currentMapId > 0 && wallToneMapId > 0 && currentMapId != wallToneMapId)
                    {
                        wallToneSuppressedUntil = currentTime + 1.0f;
                        if (SoundPlayer.IsWallTonePlaying())
                            SoundPlayer.StopWallTone();
                    }
                    if (currentMapId > 0)
                        wallToneMapId = currentMapId;

                    if (currentTime < wallToneSuppressedUntil)
                    {
                        if (SoundPlayer.IsWallTonePlaying())
                            SoundPlayer.StopWallTone();
                        continue;
                    }

                    if (MapTransitionPatches.IsScreenFading)
                    {
                        if (SoundPlayer.IsWallTonePlaying())
                            SoundPlayer.StopWallTone();
                        continue;
                    }

                    var player = GetFieldPlayer();
                    if (player == null)
                    {
                        if (SoundPlayer.IsWallTonePlaying())
                            SoundPlayer.StopWallTone();
                        continue;
                    }

                    var walls = FieldNavigationHelper.GetNearbyWallsWithDistance(player);
                    var mapExitPositions = entityCache?.GetMapExitPositions();
                    Vector3 playerPos = player.transform.localPosition;

                    // Reuse static buffer to avoid per-cycle allocations
                    wallDirectionsBuffer.Clear();

                    if (walls.NorthDist == 0 &&
                        !FieldNavigationHelper.IsDirectionNearMapExit(playerPos, DirNorth, mapExitPositions))
                        wallDirectionsBuffer.Add(SoundPlayer.Direction.North);

                    if (walls.SouthDist == 0 &&
                        !FieldNavigationHelper.IsDirectionNearMapExit(playerPos, DirSouth, mapExitPositions))
                        wallDirectionsBuffer.Add(SoundPlayer.Direction.South);

                    if (walls.EastDist == 0 &&
                        !FieldNavigationHelper.IsDirectionNearMapExit(playerPos, DirEast, mapExitPositions))
                        wallDirectionsBuffer.Add(SoundPlayer.Direction.East);

                    if (walls.WestDist == 0 &&
                        !FieldNavigationHelper.IsDirectionNearMapExit(playerPos, DirWest, mapExitPositions))
                        wallDirectionsBuffer.Add(SoundPlayer.Direction.West);

                    // Pass buffer directly (IList<Direction>) - no ToArray() allocation
                    SoundPlayer.PlayWallTonesLooped(wallDirectionsBuffer);
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[WallTones] Error: {ex.Message}");
                }
            }

            // Clean up when exiting
            wallToneCoroutine = null;
            if (SoundPlayer.IsWallTonePlaying())
                SoundPlayer.StopWallTone();
        }

        /// <summary>
        /// Gets the current map ID from UserDataManager.
        /// Returns -1 if unable to retrieve.
        /// </summary>
        private int GetCurrentMapId()
        {
            try
            {
                var userDataManager = Il2CppLast.Management.UserDataManager.Instance();
                if (userDataManager != null)
                    return userDataManager.CurrentMapId;
            }
            catch { }
            return -1;
        }

        /// <summary>
        /// Gets the FieldPlayer from the FieldPlayerController cache.
        /// </summary>
        private Il2CppLast.Entity.Field.FieldPlayer GetFieldPlayer()
        {
            try
            {
                var playerController = Utils.GameObjectCache.Get<FieldPlayerController>();
                if (playerController?.fieldPlayer != null)
                    return playerController.fieldPlayer;

                // Fallback: try to find if cache returned null (e.g., after scene transition)
                playerController = UnityEngine.Object.FindObjectOfType<FieldPlayerController>();
                if (playerController?.fieldPlayer != null)
                    return playerController.fieldPlayer;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error getting field player: {ex.Message}");
            }
            return null;
        }

        #endregion

        #region Audio Toggle Methods

        internal void ToggleWallTones()
        {
            enableWallTones = !enableWallTones;

            if (enableWallTones)
                StartWallToneLoop();
            else
                StopWallToneLoop();

            // Save to preferences
            prefWallTones.Value = enableWallTones;
            prefsCategory.SaveToFile(false);

            string status = enableWallTones ? "on" : "off";
            SpeakText($"Wall tones {status}");
        }

        internal void ToggleFootsteps()
        {
            enableFootsteps = !enableFootsteps;

            // Save to preferences
            prefFootsteps.Value = enableFootsteps;
            prefsCategory.SaveToFile(false);

            string status = enableFootsteps ? "on" : "off";
            SpeakText($"Footsteps {status}");
        }

        internal void ToggleAudioBeacons()
        {
            enableAudioBeacons = !enableAudioBeacons;

            if (enableAudioBeacons)
                StartBeaconLoop();
            else
                StopBeaconLoop();

            // Save to preferences
            prefAudioBeacons.Value = enableAudioBeacons;
            prefsCategory.SaveToFile(false);

            string status = enableAudioBeacons ? "on" : "off";
            SpeakText($"Audio beacons {status}");
        }

        // Accessors for audio feedback state (used by FootstepPatches)
        internal bool IsWallTonesEnabled() => enableWallTones;
        internal bool IsFootstepsEnabled() => enableFootsteps;
        internal bool IsAudioBeaconsEnabled() => enableAudioBeacons;

        // Public static accessors for volume settings (used by SoundPlayer and ModMenu)
        public static int WallBumpVolume => prefWallBumpVolume?.Value ?? 50;
        public static int FootstepVolume => prefFootstepVolume?.Value ?? 50;
        public static int WallToneVolume => prefWallToneVolume?.Value ?? 50;
        public static int BeaconVolume => prefBeaconVolume?.Value ?? 50;

        // Public static accessors for filter settings (used by ModMenu)
        public static bool PathfindingFilterEnabled => Instance?.filterByPathfinding ?? false;
        public static bool MapExitFilterEnabled => Instance?.filterMapExits ?? false;
        public static bool WallTonesEnabled => Instance?.enableWallTones ?? false;
        public static bool FootstepsEnabled => Instance?.enableFootsteps ?? false;
        public static bool AudioBeaconsEnabled => Instance?.enableAudioBeacons ?? false;

        // Public static setters for ModMenu
        public static void SetWallBumpVolume(int value)
        {
            if (prefWallBumpVolume != null)
            {
                prefWallBumpVolume.Value = Math.Clamp(value, 0, 100);
                prefsCategory?.SaveToFile(false);
            }
        }

        public static void SetFootstepVolume(int value)
        {
            if (prefFootstepVolume != null)
            {
                prefFootstepVolume.Value = Math.Clamp(value, 0, 100);
                prefsCategory?.SaveToFile(false);
            }
        }

        public static void SetWallToneVolume(int value)
        {
            if (prefWallToneVolume != null)
            {
                prefWallToneVolume.Value = Math.Clamp(value, 0, 100);
                prefsCategory?.SaveToFile(false);
            }
        }

        public static void SetBeaconVolume(int value)
        {
            if (prefBeaconVolume != null)
            {
                prefBeaconVolume.Value = Math.Clamp(value, 0, 100);
                prefsCategory?.SaveToFile(false);
            }
        }

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

    /// <summary>
    /// Postfix patches for entity interaction hooks.
    /// Triggers entity refresh when treasure chests are opened or dialogue ends.
    /// </summary>
    public static class EntityInteractionPatches
    {
        /// <summary>
        /// Postfix for FieldTresureBox.Open - triggers entity refresh when chest is opened.
        /// Updates the entity cache to reflect the chest's new opened state.
        /// </summary>
        public static void TreasureBox_Open_Postfix()
        {
            MelonLoader.MelonLogger.Msg("[TreasureBox] Chest opened, scheduling entity refresh");
            FFIV_ScreenReaderMod.Instance?.ScheduleEntityRefresh();
        }

        /// <summary>
        /// Postfix for MessageWindowManager.Close - triggers entity refresh when dialogue ends.
        /// Also resets dialogue tracker state for clean next conversation.
        /// </summary>
        public static void MessageWindow_Close_Postfix()
        {
            // Reset dialogue tracker for next conversation
            Patches.DialogueTracker.Reset();

            // Trigger entity refresh after dialogue ends (NPC interaction complete)
            FFIV_ScreenReaderMod.Instance?.ScheduleEntityRefresh();
        }
    }
}
