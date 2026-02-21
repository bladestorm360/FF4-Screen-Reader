using FFIV_ScreenReader.Core;

namespace FFIV_ScreenReader.Patches
{
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
            FFIV_ScreenReaderMod.Instance?.ScheduleEntityRefresh();
        }

        /// <summary>
        /// Postfix for MessageWindowManager.Close - triggers entity refresh when dialogue ends.
        /// Also resets dialogue tracker state for clean next conversation.
        /// </summary>
        public static void MessageWindow_Close_Postfix()
        {
            // Reset dialogue tracker for next conversation
            DialogueTracker.Reset();

            // Trigger entity refresh after dialogue ends (NPC interaction complete)
            FFIV_ScreenReaderMod.Instance?.ScheduleEntityRefresh();
        }
    }
}
