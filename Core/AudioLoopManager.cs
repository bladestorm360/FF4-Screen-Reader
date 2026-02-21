using System;
using System.Collections;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using FFIV_ScreenReader.Field;
using FFIV_ScreenReader.Patches;
using FFIV_ScreenReader.Utils;
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

        public AudioLoopManager(EntityNavigator entityNavigator, EntityCache entityCache)
        {
            this.entityNavigator = entityNavigator;
            this.entityCache = entityCache;
        }

        /// <summary>
        /// Loads default audio preferences into runtime state.
        /// Call once during mod initialization.
        /// </summary>
        public void LoadPreferences()
        {
            enableWallTones = PreferencesManager.WallTonesDefault;
            enableFootsteps = PreferencesManager.FootstepsDefault;
            enableAudioBeacons = PreferencesManager.AudioBeaconsDefault;
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
        public bool NeedsAudioRestart => enableWallTones || enableAudioBeacons;

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
        internal IEnumerator DelayedAudioRestart()
        {
            yield return new WaitForSeconds(0.5f);

            // Don't restart audio loops if in battle
            if (BattleState.IsInBattle)
                yield break;

            // Only start loops if on valid field (FieldPlayerController exists)
            var playerController = GameObjectCache.Get<FieldPlayerController>();
            if (playerController == null)
                playerController = GameObjectCache.Refresh<FieldPlayerController>();

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
                // Suppress during battle (belt-and-suspenders with SuppressAudio)
                if (BattleState.IsInBattle)
                {
                    yield return null;
                    continue;
                }

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

                    var playerController = GameObjectCache.Get<FieldPlayerController>();
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
                // Suppress during battle (belt-and-suspenders with SuppressAudio)
                if (BattleState.IsInBattle)
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
            enableWallTones = !enableWallTones;

            if (enableWallTones)
                StartWallToneLoop();
            else
                StopWallToneLoop();

            // Save to preferences
            PreferencesManager.SaveWallTones(enableWallTones);

            string status = enableWallTones ? "on" : "off";
            FFIV_ScreenReaderMod.SpeakText($"Wall tones {status}");
        }

        internal void ToggleFootsteps()
        {
            enableFootsteps = !enableFootsteps;

            // Save to preferences
            PreferencesManager.SaveFootsteps(enableFootsteps);

            string status = enableFootsteps ? "on" : "off";
            FFIV_ScreenReaderMod.SpeakText($"Footsteps {status}");
        }

        internal void ToggleAudioBeacons()
        {
            enableAudioBeacons = !enableAudioBeacons;

            if (enableAudioBeacons)
                StartBeaconLoop();
            else
                StopBeaconLoop();

            // Save to preferences
            PreferencesManager.SaveAudioBeacons(enableAudioBeacons);

            string status = enableAudioBeacons ? "on" : "off";
            FFIV_ScreenReaderMod.SpeakText($"Audio beacons {status}");
        }

        // Accessors for audio feedback state (used by FootstepPatches, BattleState, mod)
        internal bool IsWallTonesEnabled() => enableWallTones;
        internal bool IsFootstepsEnabled() => enableFootsteps;
        internal bool IsAudioBeaconsEnabled() => enableAudioBeacons;

        // Public static accessors for enabled state (used by ModMenu via pass-through on mod)
        public static bool WallTonesEnabled => FFIV_ScreenReaderMod.Instance?.audioManager?.enableWallTones ?? false;
        public static bool FootstepsEnabled => FFIV_ScreenReaderMod.Instance?.audioManager?.enableFootsteps ?? false;
        public static bool AudioBeaconsEnabled => FFIV_ScreenReaderMod.Instance?.audioManager?.enableAudioBeacons ?? false;

        #endregion

        #region Audio Suppression

        /// <summary>
        /// Suppresses all audio feedback. Stops loops and disables all toggles.
        /// Does not store state - callers are responsible for state management.
        /// </summary>
        internal void SuppressAudio()
        {
            StopWallToneLoop();
            StopBeaconLoop();
            enableWallTones = false;
            enableFootsteps = false;
            enableAudioBeacons = false;
        }

        /// <summary>
        /// Restores audio feedback to the given state. Restarts loops as needed.
        /// </summary>
        internal void RestoreAudio(bool wallTones, bool footsteps, bool audioBeacons)
        {
            enableWallTones = wallTones;
            enableFootsteps = footsteps;
            enableAudioBeacons = audioBeacons;
            if (enableWallTones) StartWallToneLoop();
            if (enableAudioBeacons) StartBeaconLoop();
        }

        #endregion
    }
}
