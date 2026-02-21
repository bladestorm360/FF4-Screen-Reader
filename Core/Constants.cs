namespace FFIV_ScreenReader.Core
{
    /// <summary>
    /// FF4-specific game constants (non-audio).
    /// Audio constants are in Utils.SoundConstants.
    /// </summary>
    public static class Constants
    {
        /// <summary>
        /// Size of one map cell in world units. One step = one cell = 16 world units.
        /// </summary>
        public const float CellSize = 16f;

        // SampleRate and WavHeaderSize moved to Utils.SoundConstants
        // Kept here as aliases for backward compatibility with existing references
        public const int SampleRate = Utils.SoundConstants.SAMPLE_RATE;
        public const int WavHeaderSize = Utils.SoundConstants.WAV_HEADER_SIZE;

        /// <summary>
        /// Item content type values from ContentType enum.
        /// Used by ItemDetailsAnnouncer to distinguish weapons from armor.
        /// </summary>
        public static class ItemContentTypes
        {
            public const int WEAPON = 2;
            public const int ARMOR = 3;
        }
    }
}
