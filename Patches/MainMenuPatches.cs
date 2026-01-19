using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using FFIV_ScreenReader.Core;

// Type alias for IL2CPP type
using MainMenuController = Il2CppLast.UI.KeyInput.MainMenuController;

namespace FFIV_ScreenReader.Patches
{
    /// <summary>
    /// Patches for the main menu controller.
    /// Ensures all menu state flags are cleared when entering the main menu,
    /// preventing stuck states from suppressing speech in other menus.
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
        public static void Show_Postfix(bool isSavePoint)
        {
            MenuState.ClearAllMenuStates();
        }
    }
}
