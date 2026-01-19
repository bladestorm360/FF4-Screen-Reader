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
    /// Hooks NextIndex and PrevIndex to announce menu items as players navigate.
    ///
    /// PERFORMANCE OPTIMIZATION (January 2026):
    /// Replaced expensive hierarchy walks and FindObjectsOfType with O(1) state flag checks.
    /// Each specialized menu sets its state flag when active, and this generic handler
    /// simply checks those flags instead of traversing the transform hierarchy.
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
            // Battle state - replaces expensive FindObjectsOfType<BattleEnemyEntity>
            if (BattleState.ShouldSuppress()) return true;

            // Menu states - replaces hierarchy walks
            if (ShopState.ShouldSuppress()) return true;
            if (ItemMenuState.ShouldSuppress()) return true;
            if (EquipmentMenuState.ShouldSuppress()) return true;
            if (AbilityMenuState.ShouldSuppress()) return true;
            if (ConfigMenuState.ShouldSuppress()) return true;
            if (StatusMenuState.ShouldSuppress()) return true;
            if (PartyMenuState.ShouldSuppress()) return true;
            if (TitleMenuState.ShouldSuppress()) return true;

            // Existing state flags
            if (SaveLoadMenuState.IsActive) return true;
            if (NamingPatches.ShouldSuppress()) return true;

            return false;
        }
    }

    [HarmonyPatch(typeof(GameCursor), nameof(GameCursor.NextIndex))]
    public static class Cursor_NextIndex_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(GameCursor __instance, Il2CppSystem.Action<int> action, int count, bool isLoop)
        {
            try
            {
                // Safety checks before starting coroutine
                if (__instance == null || __instance.gameObject == null || __instance.transform == null)
                {
                    return;
                }

                // Check if popup is active - if so, read button instead of menu text
                if (PopupState.ShouldSuppress() || SaveLoadMenuState.ShouldSuppress())
                {
                    PopupPatches.ReadCurrentButton(__instance);
                    return;
                }

                // Check all state flags - O(1) operation instead of hierarchy walks
                if (CursorSuppressionCheck.ShouldSuppress())
                {
                    return;
                }

                // Use managed coroutine system
                var coroutine = MenuTextDiscovery.WaitAndReadCursor(
                    __instance,
                    "NextIndex",
                    count,
                    isLoop
                );
                CoroutineManager.StartManaged(coroutine);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in NextIndex patch: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(GameCursor), nameof(GameCursor.PrevIndex))]
    public static class Cursor_PrevIndex_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(GameCursor __instance, Il2CppSystem.Action<int> action, int count, bool isLoop = false)
        {
            try
            {
                // Safety checks before starting coroutine
                if (__instance == null || __instance.gameObject == null || __instance.transform == null)
                {
                    return;
                }

                // Check if popup is active - if so, read button instead of menu text
                if (PopupState.ShouldSuppress() || SaveLoadMenuState.ShouldSuppress())
                {
                    PopupPatches.ReadCurrentButton(__instance);
                    return;
                }

                // Check all state flags - O(1) operation instead of hierarchy walks
                if (CursorSuppressionCheck.ShouldSuppress())
                {
                    return;
                }

                // Use managed coroutine system
                var coroutine = MenuTextDiscovery.WaitAndReadCursor(
                    __instance,
                    "PrevIndex",
                    count,
                    isLoop
                );
                CoroutineManager.StartManaged(coroutine);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in PrevIndex patch: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(GameCursor), nameof(GameCursor.SkipNextIndex))]
    public static class Cursor_SkipNextIndex_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(GameCursor __instance, Il2CppSystem.Action<int> action, int count, int skipCount, bool isEndPoint = false, bool isLoop = false)
        {
            try
            {
                // Safety checks before starting coroutine
                if (__instance == null || __instance.gameObject == null || __instance.transform == null)
                {
                    return;
                }

                // Check if popup is active - if so, read button instead of menu text
                if (PopupState.ShouldSuppress() || SaveLoadMenuState.ShouldSuppress())
                {
                    PopupPatches.ReadCurrentButton(__instance);
                    return;
                }

                // Check all state flags - O(1) operation instead of hierarchy walks
                if (CursorSuppressionCheck.ShouldSuppress())
                {
                    return;
                }

                // Use managed coroutine system
                var coroutine = MenuTextDiscovery.WaitAndReadCursor(
                    __instance,
                    "SkipNextIndex",
                    count,
                    isLoop
                );
                CoroutineManager.StartManaged(coroutine);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in SkipNextIndex patch: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(GameCursor), nameof(GameCursor.SkipPrevIndex))]
    public static class Cursor_SkipPrevIndex_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(GameCursor __instance, Il2CppSystem.Action<int> action, int count, int skipCount, bool isEndPoint = false, bool isLoop = false)
        {
            try
            {
                // Safety checks before starting coroutine
                if (__instance == null || __instance.gameObject == null || __instance.transform == null)
                {
                    return;
                }

                // Check if popup is active - if so, read button instead of menu text
                if (PopupState.ShouldSuppress() || SaveLoadMenuState.ShouldSuppress())
                {
                    PopupPatches.ReadCurrentButton(__instance);
                    return;
                }

                // Check all state flags - O(1) operation instead of hierarchy walks
                if (CursorSuppressionCheck.ShouldSuppress())
                {
                    return;
                }

                // Use managed coroutine system
                var coroutine = MenuTextDiscovery.WaitAndReadCursor(
                    __instance,
                    "SkipPrevIndex",
                    count,
                    isLoop
                );
                CoroutineManager.StartManaged(coroutine);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in SkipPrevIndex patch: {ex.Message}");
            }
        }
    }
}
