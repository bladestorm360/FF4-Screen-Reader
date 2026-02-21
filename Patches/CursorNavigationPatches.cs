using System;
using HarmonyLib;
using MelonLoader;
using FFIV_ScreenReader.Core;
using FFIV_ScreenReader.Menus;
using FFIV_ScreenReader.Utils;
using GameCursor = Il2CppLast.UI.Cursor;

namespace FFIV_ScreenReader.Patches
{
    /// <summary>
    /// Harmony patches for cursor navigation.
    /// Hooks NextIndex, PrevIndex, SkipNextIndex, SkipPrevIndex to announce menu items.
    ///
    /// All 4 postfixes delegate to HandleCursorNavigation which performs O(1) state flag
    /// checks instead of expensive hierarchy walks.
    /// </summary>

    /// <summary>
    /// Centralized suppression check - determines if specialized patches are handling navigation.
    /// All checks are O(1) boolean comparisons instead of O(n) hierarchy traversals.
    /// </summary>
    internal static class CursorSuppressionCheck
    {
        /// <summary>
        /// Returns true if a specialized patch is handling cursor navigation,
        /// meaning the generic cursor patch should skip processing.
        /// </summary>
        public static bool ShouldSuppress()
        {
            // All registered menu states (Battle, Shop, Item, Equipment, Ability, Config, Status, Party, Title)
            if (MenuStateRegistry.IsAnyActive()) return true;

            // External states (not managed by MenuStateRegistry)
            if (SaveLoadMenuState.IsActive) return true;
            if (NamingPatches.ShouldSuppress()) return true;

            return false;
        }
    }

    /// <summary>
    /// Shared handler for all cursor navigation postfixes.
    /// </summary>
    internal static class CursorNavigationHandler
    {
        public static void HandleCursorNavigation(GameCursor instance, string direction, int count, bool isLoop)
        {
            try
            {
                if (instance == null || instance.gameObject == null || instance.transform == null)
                    return;

                if (SaveLoadMenuState.IsActive)
                    return;

                if (PopupState.ShouldSuppress())
                {
                    PopupPatches.ReadCurrentButton(instance);
                    return;
                }

                if (CursorSuppressionCheck.ShouldSuppress())
                    return;

                CoroutineManager.StartManaged(
                    MenuTextDiscovery.WaitAndReadCursor(instance, direction, count, isLoop));
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in cursor {direction}: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(GameCursor), nameof(GameCursor.NextIndex))]
    public static class Cursor_NextIndex_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(GameCursor __instance, Il2CppSystem.Action<int> action, int count, bool isLoop) =>
            CursorNavigationHandler.HandleCursorNavigation(__instance, "NextIndex", count, isLoop);
    }

    [HarmonyPatch(typeof(GameCursor), nameof(GameCursor.PrevIndex))]
    public static class Cursor_PrevIndex_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(GameCursor __instance, Il2CppSystem.Action<int> action, int count, bool isLoop = false) =>
            CursorNavigationHandler.HandleCursorNavigation(__instance, "PrevIndex", count, isLoop);
    }

    [HarmonyPatch(typeof(GameCursor), nameof(GameCursor.SkipNextIndex))]
    public static class Cursor_SkipNextIndex_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(GameCursor __instance, Il2CppSystem.Action<int> action, int count, int skipCount, bool isEndPoint = false, bool isLoop = false) =>
            CursorNavigationHandler.HandleCursorNavigation(__instance, "SkipNextIndex", count, isLoop);
    }

    [HarmonyPatch(typeof(GameCursor), nameof(GameCursor.SkipPrevIndex))]
    public static class Cursor_SkipPrevIndex_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(GameCursor __instance, Il2CppSystem.Action<int> action, int count, int skipCount, bool isEndPoint = false, bool isLoop = false) =>
            CursorNavigationHandler.HandleCursorNavigation(__instance, "SkipPrevIndex", count, isLoop);
    }
}
