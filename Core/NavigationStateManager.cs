namespace FFIV_ScreenReader.Core
{
    /// <summary>
    /// Manages battle/dialogue navigation suppression.
    /// Stores pre-suppression state and restores it when the context ends.
    /// </summary>
    public class NavigationStateManager
    {
        private readonly AudioLoopManager audioManager;
        private readonly EntityNavigator entityNavigator;

        // Dialogue navigation suppression state storage
        private bool _preDialogueWallTones;
        private bool _preDialogueFootsteps;
        private bool _preDialogueAudioBeacons;
        private bool _preDialoguePathfindingFilter;
        private bool _hasStoredDialogueState;

        // Filter state (owned here, exposed for BattleState and ModMenu)
        internal bool FilterByPathfinding { get; set; }

        public NavigationStateManager(AudioLoopManager audioManager, EntityNavigator entityNavigator)
        {
            this.audioManager = audioManager;
            this.entityNavigator = entityNavigator;
        }

        /// <summary>
        /// Suppresses all navigation features for battle. Called when battle starts.
        /// Does not store state - that's handled by BattleState.SetActive().
        /// </summary>
        public void SuppressNavigationForBattle()
        {
            audioManager.SuppressAudio();
            FilterByPathfinding = false;
            if (entityNavigator != null)
                entityNavigator.FilterByPathfinding = false;
        }

        /// <summary>
        /// Restores navigation features after battle ends. Called by BattleState.Reset().
        /// </summary>
        public void RestoreNavigationAfterBattle(bool wallTones, bool footsteps, bool audioBeacons, bool pathfindingFilter)
        {
            audioManager.RestoreAudio(wallTones, footsteps, audioBeacons);
            FilterByPathfinding = pathfindingFilter;
            if (entityNavigator != null)
                entityNavigator.FilterByPathfinding = pathfindingFilter;
        }

        /// <summary>
        /// Suppresses all navigation features for dialogue. Stores state for restoration.
        /// </summary>
        public void SuppressNavigationForDialogue()
        {
            if (_hasStoredDialogueState) return; // Already suppressed

            _preDialogueWallTones = AudioLoopManager.WallTonesEnabled;
            _preDialogueFootsteps = AudioLoopManager.FootstepsEnabled;
            _preDialogueAudioBeacons = AudioLoopManager.AudioBeaconsEnabled;
            _preDialoguePathfindingFilter = FilterByPathfinding;
            _hasStoredDialogueState = true;

            SuppressNavigationForBattle(); // Reuse same suppression logic
        }

        /// <summary>
        /// Restores navigation features after dialogue ends.
        /// </summary>
        public void RestoreNavigationAfterDialogue()
        {
            if (!_hasStoredDialogueState) return;

            RestoreNavigationAfterBattle(
                _preDialogueWallTones, _preDialogueFootsteps,
                _preDialogueAudioBeacons, _preDialoguePathfindingFilter);
            _hasStoredDialogueState = false;
        }
    }
}
