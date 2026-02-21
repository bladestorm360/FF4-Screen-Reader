namespace FFIV_ScreenReader.Utils
{
    /// <summary>
    /// Helper for retrieving localized game messages via MessageManager.
    /// Centralizes the common pattern of instance-check + GetMessage + null-check.
    /// </summary>
    public static class MessageHelper
    {
        /// <summary>
        /// Gets a localized message by its ID. Returns null if the manager
        /// is unavailable or the message is empty/whitespace.
        /// </summary>
        public static string GetLocalizedMessage(string mesId)
        {
            if (string.IsNullOrWhiteSpace(mesId))
                return null;

            var mgr = Il2CppLast.Management.MessageManager.Instance;
            if (mgr == null) return null;

            string msg = mgr.GetMessage(mesId);
            return string.IsNullOrWhiteSpace(msg) ? null : msg;
        }
    }
}
