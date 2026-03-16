using System;
using FFIV_ScreenReader.Core;
using static FFIV_ScreenReader.Utils.ModTextTranslator;
using HarmonyLib;
using Il2CppLast.Data.User;
using Il2CppLast.Defaine.User;
using Il2CppLast.Management;
using Il2CppLast.UI.KeyInput;
using MelonLoader;

namespace FFIV_ScreenReader.Patches
{
    /// <summary>
    /// Patches for formation screen row switching (front row vs back row).
    /// Announces when a character switches between front and back row.
    /// </summary>
    [HarmonyPatch(typeof(StatusWindowController), nameof(StatusWindowController.SwitchCorps))]
    public static class StatusWindowController_SwitchCorps_Patch
    {
        public static void Prefix(StatusWindowController __instance)
        {
            try
            {
                // Get the current character index from the cursor
                var cursor = __instance.selectCursor;
                if (cursor == null)
                    return;

                int index = cursor.Index;

                // Get the current corps list to determine current state
                var userDataManager = UserDataManager.Instance();
                if (userDataManager == null)
                    return;

                var corpsList = userDataManager.GetCorpsListClone();
                if (corpsList == null || index < 0 || index >= corpsList.Count)
                    return;

                var corps = corpsList[index];
                if (corps == null)
                    return;

                // Get current corps ID (Front or Back)
                CorpsId currentId = corps.Id;

                // Determine what it's switching TO (opposite of current)
                // CorpsId.Front = 1, CorpsId.Back = 2
                string newRow;
                if (currentId == CorpsId.Front)
                {
                    newRow = T("Back Row");
                }
                else if (currentId == CorpsId.Back)
                {
                    newRow = T("Front Row");
                }
                else
                {
                    newRow = string.Format(T("Unknown Row Type {0}"), currentId);
                }

                // Announce the new row state
                FFIV_ScreenReaderMod.SpeakText(newRow);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in SwitchCorps patch: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
