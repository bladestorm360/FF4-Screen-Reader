using System;
using System.Collections;
using HarmonyLib;
using MelonLoader;
using FFIV_ScreenReader.Core;
using FFIV_ScreenReader.Utils;

using BattlePauseController = Il2CppLast.UI.KeyInput.BattlePauseController;
using GameCursor = Il2CppLast.UI.Cursor;

namespace FFIV_ScreenReader.Patches
{
    /// <summary>
    /// Battle pause menu (opened with the pause button during battle). The battle menu state
    /// suppresses the generic cursor reader, so the pause menu was silent. Opening is announced
    /// from SetEnablePauseMenu(true); navigation is routed here by CursorNavigationHandler when
    /// the moved cursor is the pause menu's own selectCommandCursor. Command names come from the
    /// controller's command message ids.
    /// </summary>
    public static class BattlePausePatches
    {
        // The open pause menu's controller: set by SetEnablePauseMenu(true), cleared by
        // SetEnablePauseMenu(false) and on scene change, so a cursor move never searches the scene.
        private static BattlePauseController activePause;

        internal static void SetActivePause(BattlePauseController pause) => activePause = pause;

        internal static void Reset() => activePause = null;

        /// <summary>
        /// Handles a cursor move if it belongs to the open battle pause menu.
        /// Returns true when handled (the caller skips its own processing).
        /// </summary>
        internal static bool TryHandleCursor(GameCursor cursor)
        {
            var pause = activePause;
            if (pause == null || !pause.isActivePauseMenu || pause.selectCommandCursor?.Pointer != cursor.Pointer)
                return false;

            CoroutineManager.StartManaged(AnnounceFocusAfterFrame(pause));
            return true;
        }

        internal static IEnumerator AnnounceFocusAfterFrame(BattlePauseController pause)
        {
            yield return null; // the cursor index updates after the move callback

            try
            {
                if (pause == null || !pause.isActivePauseMenu)
                    yield break;

                var ids = pause.isArBattle ? pause.arBattleCommandMessageIdList : pause.commandMessageIdList;
                var cursor = pause.selectCommandCursor;
                if (ids == null || cursor == null || cursor.Index < 0 || cursor.Index >= ids.Count)
                    yield break;

                string name = TextUtils.StripIconMarkup(MessageHelper.GetLocalizedMessage(ids[cursor.Index]));
                if (!string.IsNullOrEmpty(name))
                    FFIV_ScreenReaderMod.SpeakText(MenuPosition.Format(name, cursor.Index, ids.Count), interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[BattlePause] Error reading pause menu focus: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(BattlePauseController), "SetEnablePauseMenu")]
    public static class BattlePauseController_SetEnablePauseMenu_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BattlePauseController __instance, bool isEnable)
        {
            try
            {
                BattlePausePatches.SetActivePause(isEnable ? __instance : null);
                if (isEnable && __instance != null)
                    CoroutineManager.StartManaged(BattlePausePatches.AnnounceFocusAfterFrame(__instance));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[BattlePause] Error in SetEnablePauseMenu patch: {ex.Message}");
            }
        }
    }
}
