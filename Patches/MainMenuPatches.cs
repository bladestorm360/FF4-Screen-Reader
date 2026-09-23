using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using FFIV_ScreenReader.Core;
using FFIV_ScreenReader.Menus;
using FFIV_ScreenReader.Utils;

// Type alias for IL2CPP type
using MainMenuController = Il2CppLast.UI.KeyInput.MainMenuController;
using MenuManager = Il2CppLast.UI.MenuManager;
using GameCursor = Il2CppLast.UI.Cursor;

namespace FFIV_ScreenReader.Patches
{
    /// <summary>
    /// Patches for the main menu controller.
    /// Ensures all menu state flags are cleared when entering the main menu,
    /// preventing stuck states from suppressing speech in other menus, and announces the
    /// focused field-menu command on open and on back-out from any sub-menu.
    /// </summary>
    public static class MainMenuPatches
    {
        private static bool isPatched = false;

        /// <summary>
        /// Applies patches to MainMenuController.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched)
                return;

            try
            {
                Type controllerType = typeof(MainMenuController);

                // Patch Show(bool) - called when opening the main menu
                var showMethod = AccessTools.Method(controllerType, "Show", new Type[] { typeof(bool) });
                if (showMethod != null)
                {
                    var postfix = typeof(MainMenuPatches).GetMethod(nameof(Show_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(showMethod, postfix: new HarmonyMethod(postfix));
                }

                // Patch InitNone - the command-select state entered on open and on every
                // sub-menu (Item/Magic/Equip/Status/Config/...) back-out
                var initNoneMethod = AccessTools.Method(controllerType, "InitNone");
                if (initNoneMethod != null)
                {
                    var postfix = typeof(MainMenuPatches).GetMethod(nameof(InitNone_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(initNoneMethod, postfix: new HarmonyMethod(postfix));
                }

                isPatched = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[MainMenu] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for MainMenuController.Show - clears all menu states.
        /// This ensures any stuck state flags are reset when entering the main menu.
        /// </summary>
        public static void Show_Postfix(MainMenuController __instance)
        {
            MenuState.ClearAllMenuStates();
            FieldMenuReader.AnnounceFocus(__instance);
        }

        /// <summary>
        /// Postfix for MainMenuController.InitNone - re-announces the focused command when the
        /// menu returns to command select from a sub-menu.
        /// </summary>
        public static void InitNone_Postfix(MainMenuController __instance)
        {
            FieldMenuReader.AnnounceFocus(__instance);
        }
    }

    /// <summary>
    /// Announces the initially-focused command of the field menu (Item / Magic / Equip / Status /
    /// ...). Navigation already flows through the generic cursor reader, but the initial cursor
    /// placement never fires Cursor.NextIndex, so nothing is announced on open or on back-out.
    /// Reuses the same reader navigation uses, on the command bar's selectCursor.
    /// Gated on MenuManager.IsOpen so it never reads during the scene-construction flurry of a
    /// map load. (Do NOT read MainMenuController.focusId — that is the SELECTED command.)
    /// </summary>
    internal static class FieldMenuReader
    {
        // Bumped on every AnnounceFocus call; the delayed read aborts if a newer call superseded it.
        // On open both Show and InitNone fire within a frame, so the latch collapses them to one read.
        private static int _gen;

        internal static void AnnounceFocus(MainMenuController inst)
        {
            if (inst == null) return;
            int gen = ++_gen;
            CoroutineManager.StartManaged(DelayedAnnounce(inst, gen));
        }

        private static IEnumerator DelayedAnnounce(MainMenuController inst, int gen)
        {
            yield return null; // let the cursor settle AND MenuManager.IsOpen flip true

            if (gen != _gen) yield break; // superseded by a newer call

            GameCursor cursor = null;
            int count = 0;
            try
            {
                if (inst != null && inst.gameObject != null && inst.gameObject.activeInHierarchy && IsMenuOpen())
                {
                    var cmd = inst.commandMenuController; // Il2CppLast.UI.CommandMenuController
                    if (cmd != null)
                    {
                        cursor = cmd.selectCursor;
                        count = cmd.contents?.Count ?? 0;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MainMenu] Error reading field-menu focus: {ex.Message}");
            }

            if (cursor != null)
                CoroutineManager.StartManaged(MenuTextDiscovery.WaitAndReadCursor(cursor, "Navigate", count, false));
        }

        // MenuManager.IsOpen is reliably false during a map/asset load.
        private static bool IsMenuOpen()
        {
            try
            {
                var mm = MenuManager.Instance;
                return mm != null && mm.IsOpen;
            }
            catch { return false; } // MenuManager not constructed yet during early load
        }
    }
}
