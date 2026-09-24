using System;
using System.Collections;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using FFIV_ScreenReader.Field;
using FFIV_ScreenReader.Patches;
using FFIV_ScreenReader.Utils;
using static FFIV_ScreenReader.Utils.ModTextTranslator;
using Il2CppLast.Map;

namespace FFIV_ScreenReader.Core
{
    /// <summary>
    /// Manages all audio feedback subsystems: wall tones, footsteps, and audio beacons.
    /// Owns the audio state, preferences, coroutine loops, toggles, volume, and suppression logic.
    /// </summary>
    public class AudioLoopManager
    {
        private readonly EntityNavigator entityNavigator;
        private readonly EntityCache entityCache;
        private readonly WaypointNavigator waypointNavigator;

        // Transient battle/dialogue suppression gate (NOT persisted). When true the loops keep
        // running but play nothing; the enabled state itself lives in PreferencesManager
        // (single source of truth). Set by SuppressAudio, cleared by RestoreAudio.
        private bool suppressed = false;

        // Coroutine-based wall tone loop
        private IEnumerator wallToneCoroutine = null;
        private const float WALL_TONE_LOOP_INTERVAL = 0.1f;

        // Coroutine-based audio beacon loop
        private IEnumerator beaconCoroutine = null;

        // Beacon navigation constants — proximity-based interval modulation.
        // Mode A (valid path): 1.0s at 31.5 tiles (pathfinding limit) → 0.2s at 2 tiles; silent at ≤1 tile.
        // Mode B (no valid path / out of range): 1.0s at ≥100 tiles → 0.5s at 32 tiles; halved pitch.
        private const float MODE_A_INTERVAL_FAR  = 1.0f;
        private const float MODE_A_INTERVAL_NEAR = 0.2f;
        private const float MODE_A_FAR_TILES     = 31.5f;
        private const float MODE_A_NEAR_TILES    = 2.0f;
        private const float BEACON_STOP_TILES    = 1.0f;
        private const float MODE_B_INTERVAL_FAR  = 1.0f;
        private const float MODE_B_INTERVAL_NEAR = 0.5f;
        private const float MODE_B_FAR_TILES     = 100f;
        private const float MODE_B_NEAR_TILES    = 32f;
        private const float TILE_SIZE            = 16f;

        // Beacon state
        private bool beaconSilenced = false;
        private object lastBeaconTarget = null;
        private float nextBeaconTime = 0f;

        // Map transition suppression for wall tones
        private int wallToneMapId = -1;
        internal float wallToneSuppressedUntil = 0f;

        // Map transition suppression for beacons
        internal float beaconSuppressedUntil = 0f;

        // Reusable direction list buffer to avoid per-cycle allocations
        private static readonly List<SoundPlayer.Direction> wallDirectionsBuffer = new List<SoundPlayer.Direction>(4);

        // Pre-cached direction vectors for map exit checks (avoids per-cycle Vector3 allocations)
        private static readonly Vector3 DirNorth = new Vector3(0, 16, 0);
        private static readonly Vector3 DirSouth = new Vector3(0, -16, 0);
        private static readonly Vector3 DirEast = new Vector3(16, 0, 0);
        private static readonly Vector3 DirWest = new Vector3(-16, 0, 0);

        // Beacon debouncing
        private float lastBeaconPlayedAt = 0f;

        public AudioLoopManager(EntityNavigator entityNavigator, EntityCache entityCache, WaypointNavigator waypointNavigator)
        {
            this.entityNavigator = entityNavigator;
            this.entityCache = entityCache;
            this.waypointNavigator = waypointNavigator;
        }

        /// <summary>
        /// Stops all audio loops. Call during mod shutdown.
        /// </summary>
        public void Shutdown()
        {
            StopWallToneLoop();
            StopBeaconLoop();
        }

        /// <summary>
        /// Handles scene transition: stops audio loops and suppresses briefly.
        /// </summary>
        public void OnSceneTransition()
        {
            StopWallToneLoop();
            StopBeaconLoop();
            wallToneSuppressedUntil = Time.time + 1.0f;
            beaconSuppressedUntil = Time.time + 1.0f;
        }

        /// <summary>
        /// Whether any audio loop needs restarting after a scene load.
        /// </summary>
        public bool NeedsAudioRestart => PreferencesManager.WallTonesEnabled || PreferencesManager.AudioBeaconsEnabled;

        #region Audio Loop Management

        /// <summary>
        /// Starts the wall tone coroutine loop. Safe to call if already running (no-op).
        /// </summary>
        private void StartWallToneLoop()
        {
            if (!PreferencesManager.WallTonesEnabled) return;  // Don't start if disabled
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
        /// (Re)starts the enabled audio loops when the field player exists and no battle is running.
        /// Event-driven (2026-09-24, replaces a 0.5 s WaitForSeconds after every scene load): called
        /// from OnSceneLoaded (an additive load on the field, where the player already exists) and
        /// from MainGame.set_FieldReady(true) (a map load, where the player appears later). Start*
        /// are no-ops for a loop that is already running, and the loops stay silent for the first
        /// second after OnSceneTransition (wallToneSuppressedUntil / beaconSuppressedUntil).
        /// </summary>
        internal void RestartLoopsIfOnField()
        {
            if (!NeedsAudioRestart || BattleState.IsInBattle)
                return;

            // Only start loops on a valid field (FieldPlayerController cached by OnSceneLoaded / FieldReady)
            if (GameObjectCache.Get<FieldPlayerController>() == null)
                return;

            if (PreferencesManager.WallTonesEnabled) StartWallToneLoop();
            if (PreferencesManager.AudioBeaconsEnabled) StartBeaconLoop();
        }

        /// <summary>
        /// Starts the audio beacon coroutine loop. Safe to call if already running (no-op).
        /// </summary>
        private void StartBeaconLoop()
        {
            if (!PreferencesManager.AudioBeaconsEnabled) return;  // Don't start if disabled
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
            beaconSilenced = false;
            lastBeaconTarget = null;
        }

        /// <summary>
        /// Forces the beacon to ping on the next loop iteration and clears any silence latch.
        /// Called by the pathfinding commands when beacon navigation mode is on.
        /// </summary>
        public void RestartBeacon()
        {
            beaconSilenced = false;
            nextBeaconTime = 0f;
        }

        /// <summary>
        /// Coroutine loop that plays proximity-based audio beacon pings.
        /// Interval shortens as the player nears the selected entity.
        /// Mode A (valid path): normal pitch, 1.0s→0.2s over 31.5→2 tiles, silent at ≤1 tile.
        /// Mode B (no valid path): halved pitch, 1.0s→0.5s over 100→32 tiles, no silence latch.
        /// Uses manual time-based waiting because WaitForSeconds doesn't work through IL2CPP wrapper.
        /// </summary>
        private IEnumerator BeaconLoop()
        {
            nextBeaconTime = Time.time + 0.3f;  // Delay first beacon by 300ms for scene stability

            while (PreferencesManager.AudioBeaconsEnabled)
            {
                // Silence during battle, NPC dialogue, transient suppression, any open game menu
                // (IsFieldActive), or a mod dialog (SuppressGameInput: mod menu / text input /
                // confirmation, where the per-tick work would also stutter keyboard polling).
                if (suppressed || BattleState.IsInBattle || DialogueTracker.IsInDialogue
                    || !ControllerRouter.IsFieldActive || ControllerRouter.SuppressGameInput)
                {
                    yield return null;
                    continue;
                }

                if (Time.time < nextBeaconTime)
                {
                    yield return null;
                    continue;
                }

                // Suppress beacons briefly after scene load
                if (Time.time < beaconSuppressedUntil)
                {
                    nextBeaconTime = Time.time + 0.1f;
                    continue;
                }

                try
                {
                    object targetRef = null;
                    Vector3 targetPos = Vector3.zero;
                    switch (NavigationTargetTracker.LastKind)
                    {
                        case NavigationTargetTracker.Kind.Entity:
                            var e = entityNavigator?.CurrentEntity;
                            if (e != null) { targetRef = e; targetPos = e.Position; }
                            break;
                        case NavigationTargetTracker.Kind.Waypoint:
                            var w = waypointNavigator?.SelectedWaypoint;
                            if (w != null) { targetRef = w; targetPos = w.Position; }
                            break;
                        default:
                            // No selection yet — fall back to current entity (preserves
                            // legacy behavior where beacon followed the entity by default).
                            var fallback = entityNavigator?.CurrentEntity;
                            if (fallback != null) { targetRef = fallback; targetPos = fallback.Position; }
                            break;
                    }

                    if (targetRef == null)
                    {
                        nextBeaconTime = Time.time + 0.2f;
                        continue;
                    }

                    // Selection change clears the silence latch so new targets always ping.
                    if (!ReferenceEquals(targetRef, lastBeaconTarget))
                    {
                        beaconSilenced = false;
                        lastBeaconTarget = targetRef;
                    }

                    var playerController = GameObjectCache.Get<FieldPlayerController>();
                    if (playerController?.fieldPlayer == null)
                    {
                        nextBeaconTime = Time.time + 0.2f;
                        continue;
                    }

                    Vector3 playerPos = playerController.fieldPlayer.transform.localPosition;

                    // Sanity check: skip if positions look invalid (garbage data during load)
                    if (float.IsNaN(playerPos.x) || float.IsNaN(targetPos.x) ||
                        Mathf.Abs(playerPos.x) > 10000f || Mathf.Abs(targetPos.x) > 10000f)
                    {
                        nextBeaconTime = Time.time + 0.2f;
                        continue;
                    }

                    float distTiles = Vector3.Distance(playerPos, targetPos) / TILE_SIZE;

                    // Mode selection — expensive (A* per beacon tick) but only 1–5 Hz.
                    bool pathValid;
                    try
                    {
                        var pathInfo = FieldNavigationHelper.FindPathTo(
                            playerPos, targetPos,
                            playerController.mapHandle,
                            playerController.fieldPlayer);
                        pathValid = pathInfo.Success;
                    }
                    catch
                    {
                        pathValid = false;
                    }

                    float interval;
                    bool lowPitch;
                    if (pathValid)
                    {
                        // Mode A: valid path
                        if (distTiles <= BEACON_STOP_TILES)
                        {
                            beaconSilenced = true;
                            nextBeaconTime = Time.time + 0.2f;
                            continue;
                        }
                        // Player moved back out of the stop radius — release the arrival-silence
                        // latch so the beacon resumes pinging when they walk away from a reached,
                        // still-selected target (previously it stayed silent permanently).
                        beaconSilenced = false;
                        float t = Mathf.Clamp01((distTiles - MODE_A_NEAR_TILES) /
                                                (MODE_A_FAR_TILES - MODE_A_NEAR_TILES));
                        interval = Mathf.Lerp(MODE_A_INTERVAL_NEAR, MODE_A_INTERVAL_FAR, t);
                        lowPitch = false;
                    }
                    else
                    {
                        // Mode B: out of range or blocked — halved pitch, no silence latch
                        float t = Mathf.Clamp01((distTiles - MODE_B_NEAR_TILES) /
                                                (MODE_B_FAR_TILES - MODE_B_NEAR_TILES));
                        interval = Mathf.Lerp(MODE_B_INTERVAL_NEAR, MODE_B_INTERVAL_FAR, t);
                        lowPitch = true;
                    }

                    // Silence latch only holds while the path is valid (Mode A).
                    if (beaconSilenced && pathValid)
                    {
                        nextBeaconTime = Time.time + 0.2f;
                        continue;
                    }

                    nextBeaconTime = Time.time + interval;

                    float maxDist = 500f;
                    float volumeScale = Mathf.Clamp(1f - (distTiles * TILE_SIZE / maxDist), 0.15f, 0.60f);

                    float deltaX = targetPos.x - playerPos.x;
                    float pan = Mathf.Clamp(deltaX / 100f, -1f, 1f) * 0.5f + 0.5f;

                    bool isSouth = targetPos.y < playerPos.y - 8f;

                    // Debounce: ensure at least 80% of the current interval has elapsed
                    float timeSinceLast = Time.time - lastBeaconPlayedAt;
                    if (timeSinceLast < interval * 0.8f)
                        continue;

                    SoundPlayer.PlayBeacon(isSouth, pan, volumeScale, lowPitch);
                    lastBeaconPlayedAt = Time.time;
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[Beacon] Error: {ex.Message}");
                    nextBeaconTime = Time.time + 0.5f;
                }
            }

            // Clean up when exiting
            beaconCoroutine = null;
            beaconSilenced = false;
            lastBeaconTarget = null;
        }

        /// <summary>
        /// Coroutine loop that checks for adjacent walls every 100ms and plays looping tones.
        /// Uses manual time-based waiting for IL2CPP compatibility.
        /// Exits when the wall-tone preference is disabled.
        /// </summary>
        private IEnumerator WallToneLoop()
        {
            float nextCheckTime = Time.time + 0.3f;  // Delay first check by 300ms for scene stability

            while (PreferencesManager.WallTonesEnabled)  // Exit when disabled
            {
                // Silence during battle, NPC dialogue, transient suppression, any open game menu
                // (IsFieldActive), or a mod dialog (SuppressGameInput: mod menu / text input /
                // confirmation, where the per-tick work would also stutter keyboard polling).
                if (suppressed || BattleState.IsInBattle || DialogueTracker.IsInDialogue
                    || !ControllerRouter.IsFieldActive || ControllerRouter.SuppressGameInput)
                {
                    if (SoundPlayer.IsWallTonePlaying())
                        SoundPlayer.StopWallTone();
                    yield return null;
                    continue;
                }

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
                var playerController = GameObjectCache.Get<FieldPlayerController>();
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
            // Single source of truth: save the pref FIRST, then start/stop the loop
            // (which reads the pref back live).
            bool newVal = !PreferencesManager.WallTonesEnabled;
            PreferencesManager.SaveWallTones(newVal);

            if (newVal)
                StartWallToneLoop();
            else
                StopWallToneLoop();

            FFIV_ScreenReaderMod.SpeakText(string.Format(T("Wall tones {0}"), newVal ? T("on") : T("off")));
        }

        internal void ToggleFootsteps()
        {
            bool newVal = !PreferencesManager.FootstepsEnabled;
            PreferencesManager.SaveFootsteps(newVal);

            FFIV_ScreenReaderMod.SpeakText(string.Format(T("Footsteps {0}"), newVal ? T("on") : T("off")));
        }

        internal void ToggleAudioBeacons()
        {
            bool newVal = !PreferencesManager.AudioBeaconsEnabled;
            PreferencesManager.SaveAudioBeacons(newVal);

            if (newVal)
                StartBeaconLoop();
            else
                StopBeaconLoop();

            FFIV_ScreenReaderMod.SpeakText(string.Format(T("Beacon navigation {0}"), newVal ? T("on") : T("off")));
        }

        // Public static accessors for enabled state — single source of truth is PreferencesManager.
        // Used by ModMenu, BattleState, NavigationStateManager, FootstepPatches, ControllerRouter.
        public static bool WallTonesEnabled => PreferencesManager.WallTonesEnabled;
        public static bool FootstepsEnabled => PreferencesManager.FootstepsEnabled;
        public static bool AudioBeaconsEnabled => PreferencesManager.AudioBeaconsEnabled;

        #endregion

        #region Audio Suppression

        /// <summary>
        /// Suppresses all audio feedback (battle/dialogue) via the transient suppressed gate.
        /// The persisted enabled prefs are NOT touched; the loops stay running but go silent and
        /// resume automatically once suppression clears.
        /// </summary>
        internal void SuppressAudio()
        {
            suppressed = true;
            if (SoundPlayer.IsWallTonePlaying())
                SoundPlayer.StopWallTone();
        }

        /// <summary>
        /// Clears the suppression gate and ensures the loops are running for whatever is enabled.
        /// The bool parameters are the pre-suppression enabled snapshot; with a single source of
        /// truth (PreferencesManager) they equal the live prefs, so we restart from those.
        /// </summary>
        internal void RestoreAudio(bool wallTones, bool footsteps, bool audioBeacons)
        {
            suppressed = false;
            if (PreferencesManager.WallTonesEnabled) StartWallToneLoop();
            if (PreferencesManager.AudioBeaconsEnabled) StartBeaconLoop();
        }

        #endregion
    }
}
