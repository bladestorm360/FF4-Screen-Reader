using System;
using MelonLoader;

namespace FFIV_ScreenReader.Core
{
    /// <summary>
    /// Centralized preferences management for the FFIV Screen Reader mod.
    /// All MelonPreferences entries live here with public getters and setter methods.
    /// </summary>
    public static class PreferencesManager
    {
        private static MelonPreferences_Category prefsCategory;

        // Toggle preferences
        private static MelonPreferences_Entry<bool> prefPathfindingFilter;
        private static MelonPreferences_Entry<bool> prefMapExitFilter;
        private static MelonPreferences_Entry<bool> prefToLayerFilter;
        private static MelonPreferences_Entry<bool> prefWallTones;
        private static MelonPreferences_Entry<bool> prefFootsteps;
        private static MelonPreferences_Entry<bool> prefAudioBeacons;
        private static MelonPreferences_Entry<bool> prefStickClickNormalization;
        private static MelonPreferences_Entry<bool> prefAnnounceOnBeaconRestart;
        private static MelonPreferences_Entry<bool> prefMenuPositionAnnouncements;
        private static MelonPreferences_Entry<bool> prefAutoDetail;
        private static MelonPreferences_Entry<bool> prefExpCounter;

        // Volume preferences (0-100, default 50)
        private static MelonPreferences_Entry<int> prefWallBumpVolume;
        private static MelonPreferences_Entry<int> prefFootstepVolume;
        private static MelonPreferences_Entry<int> prefWallToneVolume;
        private static MelonPreferences_Entry<int> prefBeaconVolume;
        private static MelonPreferences_Entry<int> prefExpCounterVolume;

        // Battle preferences
        private static MelonPreferences_Entry<int> prefEnemyHPDisplay;

        // Multi-hit damage display (0=Total only, 1=With hit count "14x1552 damage")
        private static MelonPreferences_Entry<int> prefDamageDisplay;

        /// <summary>
        /// Initialize all preferences. Call once during OnInitializeMelon.
        /// </summary>
        public static void Initialize()
        {
            prefsCategory = MelonPreferences.CreateCategory("FFIV_ScreenReader");

            prefPathfindingFilter = prefsCategory.CreateEntry<bool>("PathfindingFilter", false, "Pathfinding Filter", "Only show entities with valid paths when cycling");
            prefMapExitFilter = prefsCategory.CreateEntry<bool>("MapExitFilter", false, "Map Exit Filter", "Filter multiple map exits to the same destination, showing only the closest one");
            prefToLayerFilter = prefsCategory.CreateEntry<bool>("ToLayerFilter", false, "Layer Transition Filter", "Hide layer transition entities from navigation list");
            prefWallTones = prefsCategory.CreateEntry<bool>("WallTones", false, "Wall Tones", "Play directional tones when approaching walls");
            prefFootsteps = prefsCategory.CreateEntry<bool>("Footsteps", false, "Footsteps", "Play click sound on each tile movement");
            prefAudioBeacons = prefsCategory.CreateEntry<bool>("AudioBeacons", false, "Audio Beacons", "Play directional pings toward selected entity");
            prefStickClickNormalization = prefsCategory.CreateEntry<bool>("StickClickNormalization", false, "Stick Click Normalization", "Pass L3/R3 through to game (auto-dash / encounter toggle); mod functions require mod mode");
            prefAnnounceOnBeaconRestart = prefsCategory.CreateEntry<bool>("AnnounceOnBeaconRestart", false, "Beacon Destination Announcement", "Re-speak the current destination when the beacon is restarted");
            prefMenuPositionAnnouncements = prefsCategory.CreateEntry<bool>("MenuPositionAnnouncements", true, "Menu Position Announcements", "Append the cursor's position in a list when navigating menus, e.g. (3 of 12)");
            prefAutoDetail = prefsCategory.CreateEntry<bool>("AutoDetail", false, "Auto Detail", "Automatically read the details normally behind the details key (equip compatibility in the item menu, descriptions and MP cost in shops) when focusing an entry");
            prefExpCounter = prefsCategory.CreateEntry<bool>("ExpCounter", true, "EXP Counter Sound", "Play rapid beeping while the EXP bar animates on battle results");

            prefWallBumpVolume = prefsCategory.CreateEntry<int>("WallBumpVolume", 50, "Wall Bump Volume", "Volume for wall bump sounds (0-100)");
            prefFootstepVolume = prefsCategory.CreateEntry<int>("FootstepVolume", 50, "Footstep Volume", "Volume for footstep sounds (0-100)");
            prefWallToneVolume = prefsCategory.CreateEntry<int>("WallToneVolume", 50, "Wall Tone Volume", "Volume for wall proximity tones (0-100)");
            prefBeaconVolume = prefsCategory.CreateEntry<int>("BeaconVolume", 50, "Beacon Volume", "Volume for audio beacon pings (0-100)");
            prefExpCounterVolume = prefsCategory.CreateEntry<int>("ExpCounterVolume", 50, "EXP Counter Volume", "Volume for EXP counter beep (0-100)");

            prefEnemyHPDisplay = prefsCategory.CreateEntry<int>("EnemyHPDisplay", 0, "Enemy HP Display", "0=Numbers, 1=Percentage, 2=Hidden");
            prefDamageDisplay = prefsCategory.CreateEntry<int>("DamageDisplay", 0, "Multi-hit Damage", "0=Total only, 1=With hit count (e.g. 14x1552 damage)");
        }

        #region Toggle Getters (live saved preference values)

        public static bool PathfindingFilterEnabled => prefPathfindingFilter?.Value ?? false;
        public static bool MapExitFilterEnabled => prefMapExitFilter?.Value ?? false;
        public static bool ToLayerFilterEnabled => prefToLayerFilter?.Value ?? false;
        public static bool WallTonesEnabled => prefWallTones?.Value ?? false;
        public static bool FootstepsEnabled => prefFootsteps?.Value ?? false;
        public static bool AudioBeaconsEnabled => prefAudioBeacons?.Value ?? false;
        public static bool StickClickNormalizationEnabled => prefStickClickNormalization?.Value ?? false;
        public static bool AnnounceOnBeaconRestartEnabled => prefAnnounceOnBeaconRestart?.Value ?? false;
        public static bool MenuPositionAnnouncementsEnabled => prefMenuPositionAnnouncements?.Value ?? true;
        public static bool AutoDetailEnabled => prefAutoDetail?.Value ?? false;
        public static bool ExpCounterEnabled => prefExpCounter?.Value ?? true;

        #endregion

        #region Volume Getters

        public static int WallBumpVolume => prefWallBumpVolume?.Value ?? 50;
        public static int FootstepVolume => prefFootstepVolume?.Value ?? 50;
        public static int WallToneVolume => prefWallToneVolume?.Value ?? 50;
        public static int BeaconVolume => prefBeaconVolume?.Value ?? 50;
        public static int ExpCounterVolume => prefExpCounterVolume?.Value ?? 50;

        #endregion

        #region Battle Getters

        public static int EnemyHPDisplay => prefEnemyHPDisplay?.Value ?? 0;
        public static int DamageDisplay => prefDamageDisplay?.Value ?? 0;

        #endregion

        #region Shared persistence helpers

        private static void SetIntPreference(MelonPreferences_Entry<int> pref, int value, int min, int max)
        {
            if (pref != null)
            {
                pref.Value = Math.Clamp(value, min, max);
                prefsCategory?.SaveToFile(false);
            }
        }

        private static void SetBoolPreference(MelonPreferences_Entry<bool> pref, bool value)
        {
            if (pref != null)
            {
                pref.Value = value;
                prefsCategory?.SaveToFile(false);
            }
        }

        #endregion

        #region Volume / Enum Setters (with clamping + auto-save)

        public static void SetWallBumpVolume(int value) => SetIntPreference(prefWallBumpVolume, value, 0, 100);
        public static void SetFootstepVolume(int value) => SetIntPreference(prefFootstepVolume, value, 0, 100);
        public static void SetWallToneVolume(int value) => SetIntPreference(prefWallToneVolume, value, 0, 100);
        public static void SetBeaconVolume(int value) => SetIntPreference(prefBeaconVolume, value, 0, 100);
        public static void SetExpCounterVolume(int value) => SetIntPreference(prefExpCounterVolume, value, 0, 100);
        public static void SetEnemyHPDisplay(int value) => SetIntPreference(prefEnemyHPDisplay, value, 0, 2);
        public static void SetDamageDisplay(int value) => SetIntPreference(prefDamageDisplay, value, 0, 1);

        #endregion

        #region Toggle Persistence Helpers

        public static void SavePathfindingFilter(bool value) => SetBoolPreference(prefPathfindingFilter, value);
        public static void SaveMapExitFilter(bool value) => SetBoolPreference(prefMapExitFilter, value);
        public static void SaveToLayerFilter(bool value) => SetBoolPreference(prefToLayerFilter, value);
        public static void SaveWallTones(bool value) => SetBoolPreference(prefWallTones, value);
        public static void SaveFootsteps(bool value) => SetBoolPreference(prefFootsteps, value);
        public static void SaveAudioBeacons(bool value) => SetBoolPreference(prefAudioBeacons, value);
        public static void SaveStickClickNormalization(bool value) => SetBoolPreference(prefStickClickNormalization, value);
        public static void SaveAnnounceOnBeaconRestart(bool value) => SetBoolPreference(prefAnnounceOnBeaconRestart, value);
        public static void SaveMenuPositionAnnouncements(bool value) => SetBoolPreference(prefMenuPositionAnnouncements, value);
        public static void SaveAutoDetail(bool value) => SetBoolPreference(prefAutoDetail, value);
        public static void SaveExpCounter(bool value) => SetBoolPreference(prefExpCounter, value);

        #endregion
    }
}
