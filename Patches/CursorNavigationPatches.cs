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
    /// </summary>
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

                // Skip if in save/load menu (handled by SaveListController.SelectContent patch)
                if (SaveLoadMenuState.IsActive)
                {
                    return;
                }

                // Skip if this is item target selection (handled by ItemUseController.SelectContent patch)
                var parent = __instance.transform.parent;
                while (parent != null)
                {
                    if (parent.name.Contains("item_target_select"))
                    {
                        return;
                    }
                    parent = parent.parent;
                }

                // Skip ALL battle navigation (battle controller patches handle everything in battle)
                var enemyEntities = UnityEngine.Object.FindObjectsOfType<Il2CppLast.Battle.BattleEnemyEntity>();
                if (enemyEntities != null && enemyEntities.Length > 0)
                {
                    return; // We're in battle - let controller patches handle it
                }

                // Skip if this is battle target selection (handled by BattleTargetSelectController.SelectContent patch)
                parent = __instance.transform.parent;
                while (parent != null)
                {
                    string parentName = parent.name.ToLower();
                    if (parentName.Contains("battle_target") ||
                        parentName.Contains("battletarget") ||
                        parentName.Contains("battle_command") ||
                        parentName.Contains("battlecommand") ||
                        parentName.Contains("battle_item") ||
                        parentName.Contains("battleitem") ||
                        parentName.Contains("battle_ability") ||
                        parentName.Contains("battleability") ||
                        parentName.Contains("battle_infomation") ||
                        parentName.Contains("battleinfomation") ||
                        parentName.Contains("battle_menu") ||
                        parentName.Contains("battlemenu"))
                    {
                        return;
                    }
                    parent = parent.parent;
                }

                // Skip if this is title menu navigation (handled by TitleMenuCommandController.SetCursor patch)
                parent = __instance.transform.parent;
                while (parent != null)
                {
                    if (parent.name.Contains("title_command") || parent.name.Contains("TitleMenu"))
                    {
                        return;
                    }
                    parent = parent.parent;
                }

                // Skip if this is config menu navigation (handled by ConfigCommandController.SetFocus patch)
                parent = __instance.transform.parent;
                while (parent != null)
                {
                    if (parent.name.Contains("config") || parent.name.Contains("Config"))
                    {
                        return;
                    }
                    parent = parent.parent;
                }

                // Skip if this is item menu navigation (handled by ItemListController.SelectContent patch)
                parent = __instance.transform.parent;
                while (parent != null)
                {
                    if (parent.name.Contains("list_window"))
                    {
                        return;
                    }
                    parent = parent.parent;
                }

                // Skip if this is equipment menu navigation (handled by EquipmentSelectWindowController.SetCursor patch)
                parent = __instance.transform.parent;
                while (parent != null)
                {
                    if (parent.name.Contains("equip_select"))
                    {
                        return;
                    }
                    parent = parent.parent;
                }

                // Skip if this is equipment slot navigation (handled by EquipmentInfoWindowController.SelectContent patch)
                parent = __instance.transform.parent;
                while (parent != null)
                {
                    if (parent.name.Contains("equip_info_content") || parent.name.Contains("EquipmentInfo"))
                    {
                        return;
                    }
                    parent = parent.parent;
                }

                // Skip if this is shop navigation (handled by ShopPatches)
                parent = __instance.transform.parent;
                while (parent != null)
                {
                    if (parent.name.Contains("shop") || parent.name.Contains("Shop"))
                    {
                        return;
                    }
                    parent = parent.parent;
                }

                // Skip if this is party setting menu (handled by PartySettingMenuBaseController.SelectContent patch)
                parent = __instance.transform.parent;
                while (parent != null)
                {
                    if (parent.name.Contains("party") || parent.name.Contains("Party"))
                    {
                        return;
                    }
                    parent = parent.parent;
                }

                // Skip if this is status details screen (handled by StatusDetailsController patches)
                parent = __instance.transform.parent;
                while (parent != null)
                {
                    if (parent.name.Contains("status") || parent.name.Contains("Status"))
                    {
                        return;
                    }
                    parent = parent.parent;
                }

                // Skip if this is ability menu navigation (handled by AbilityMenuPatches)
                parent = __instance.transform.parent;
                while (parent != null)
                {
                    string parentName = parent.name.ToLower();
                    if (parentName.Contains("ability"))
                    {
                        return;
                    }
                    parent = parent.parent;
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

                // Skip if in save/load menu (handled by SaveListController.SelectContent patch)
                if (SaveLoadMenuState.IsActive)
                {
                    return;
                }

                // Skip if this is item target selection (handled by ItemUseController.SelectContent patch)
                var parent = __instance.transform.parent;
                while (parent != null)
                {
                    if (parent.name.Contains("item_target_select"))
                    {
                        return;
                    }
                    parent = parent.parent;
                }

                // Skip ALL battle navigation (battle controller patches handle everything in battle)
                var enemyEntities = UnityEngine.Object.FindObjectsOfType<Il2CppLast.Battle.BattleEnemyEntity>();
                if (enemyEntities != null && enemyEntities.Length > 0)
                {
                    return; // We're in battle - let controller patches handle it
                }

                // Skip if this is battle target selection
                parent = __instance.transform.parent;
                while (parent != null)
                {
                    string parentName = parent.name.ToLower();
                    if (parentName.Contains("battle_target") ||
                        parentName.Contains("battletarget") ||
                        parentName.Contains("battle_command") ||
                        parentName.Contains("battlecommand") ||
                        parentName.Contains("battle_item") ||
                        parentName.Contains("battleitem") ||
                        parentName.Contains("battle_ability") ||
                        parentName.Contains("battleability") ||
                        parentName.Contains("battle_infomation") ||
                        parentName.Contains("battleinfomation") ||
                        parentName.Contains("battle_menu") ||
                        parentName.Contains("battlemenu"))
                    {
                        return;
                    }
                    parent = parent.parent;
                }

                // Skip if this is title menu navigation
                parent = __instance.transform.parent;
                while (parent != null)
                {
                    if (parent.name.Contains("title_command") || parent.name.Contains("TitleMenu"))
                    {
                        return;
                    }
                    parent = parent.parent;
                }

                // Skip if this is config menu navigation
                parent = __instance.transform.parent;
                while (parent != null)
                {
                    if (parent.name.Contains("config") || parent.name.Contains("Config"))
                    {
                        return;
                    }
                    parent = parent.parent;
                }

                // Skip if this is item menu navigation
                parent = __instance.transform.parent;
                while (parent != null)
                {
                    if (parent.name.Contains("list_window"))
                    {
                        return;
                    }
                    parent = parent.parent;
                }

                // Skip if this is equipment menu navigation
                parent = __instance.transform.parent;
                while (parent != null)
                {
                    if (parent.name.Contains("equip_select"))
                    {
                        return;
                    }
                    parent = parent.parent;
                }

                // Skip if this is equipment slot navigation
                parent = __instance.transform.parent;
                while (parent != null)
                {
                    if (parent.name.Contains("equip_info_content") || parent.name.Contains("EquipmentInfo"))
                    {
                        return;
                    }
                    parent = parent.parent;
                }

                // Skip if this is shop navigation
                parent = __instance.transform.parent;
                while (parent != null)
                {
                    if (parent.name.Contains("shop") || parent.name.Contains("Shop"))
                    {
                        return;
                    }
                    parent = parent.parent;
                }

                // Skip if this is party setting menu
                parent = __instance.transform.parent;
                while (parent != null)
                {
                    if (parent.name.Contains("party") || parent.name.Contains("Party"))
                    {
                        return;
                    }
                    parent = parent.parent;
                }

                // Skip if this is status details screen
                parent = __instance.transform.parent;
                while (parent != null)
                {
                    if (parent.name.Contains("status") || parent.name.Contains("Status"))
                    {
                        return;
                    }
                    parent = parent.parent;
                }

                // Skip if this is ability menu navigation
                parent = __instance.transform.parent;
                while (parent != null)
                {
                    string parentName = parent.name.ToLower();
                    if (parentName.Contains("ability"))
                    {
                        return;
                    }
                    parent = parent.parent;
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

                // Skip if in save/load menu (handled by SaveListController.SelectContent patch)
                if (SaveLoadMenuState.IsActive)
                {
                    return;
                }

                // Skip if this is item target selection (handled by ItemUseController.SelectContent patch)
                var parent = __instance.transform.parent;
                while (parent != null)
                {
                    if (parent.name.Contains("item_target_select"))
                    {
                        return;
                    }
                    parent = parent.parent;
                }

                // Skip ALL battle navigation (battle controller patches handle everything in battle)
                var enemyEntities = UnityEngine.Object.FindObjectsOfType<Il2CppLast.Battle.BattleEnemyEntity>();
                if (enemyEntities != null && enemyEntities.Length > 0)
                {
                    return; // We're in battle - let controller patches handle it
                }

                // Skip if this is battle target selection
                parent = __instance.transform.parent;
                while (parent != null)
                {
                    string parentName = parent.name.ToLower();
                    if (parentName.Contains("battle_target") ||
                        parentName.Contains("battletarget") ||
                        parentName.Contains("battle_command") ||
                        parentName.Contains("battlecommand") ||
                        parentName.Contains("battle_item") ||
                        parentName.Contains("battleitem") ||
                        parentName.Contains("battle_ability") ||
                        parentName.Contains("battleability") ||
                        parentName.Contains("battle_infomation") ||
                        parentName.Contains("battleinfomation") ||
                        parentName.Contains("battle_menu") ||
                        parentName.Contains("battlemenu"))
                    {
                        return;
                    }
                    parent = parent.parent;
                }

                // Skip if this is title menu navigation
                parent = __instance.transform.parent;
                while (parent != null)
                {
                    if (parent.name.Contains("title_command") || parent.name.Contains("TitleMenu"))
                    {
                        return;
                    }
                    parent = parent.parent;
                }

                // Skip if this is config menu navigation
                parent = __instance.transform.parent;
                while (parent != null)
                {
                    if (parent.name.Contains("config") || parent.name.Contains("Config"))
                    {
                        return;
                    }
                    parent = parent.parent;
                }

                // Skip if this is item menu navigation (handled by ItemListController.SelectContent patch)
                parent = __instance.transform.parent;
                while (parent != null)
                {
                    if (parent.name.Contains("list_window"))
                    {
                        return;
                    }
                    parent = parent.parent;
                }

                // Skip if this is equipment menu navigation
                parent = __instance.transform.parent;
                while (parent != null)
                {
                    if (parent.name.Contains("equip_select"))
                    {
                        return;
                    }
                    parent = parent.parent;
                }

                // Skip if this is equipment slot navigation
                parent = __instance.transform.parent;
                while (parent != null)
                {
                    if (parent.name.Contains("equip_info_content") || parent.name.Contains("EquipmentInfo"))
                    {
                        return;
                    }
                    parent = parent.parent;
                }

                // Skip if this is shop navigation
                parent = __instance.transform.parent;
                while (parent != null)
                {
                    if (parent.name.Contains("shop") || parent.name.Contains("Shop"))
                    {
                        return;
                    }
                    parent = parent.parent;
                }

                // Skip if this is party setting menu
                parent = __instance.transform.parent;
                while (parent != null)
                {
                    if (parent.name.Contains("party") || parent.name.Contains("Party"))
                    {
                        return;
                    }
                    parent = parent.parent;
                }

                // Skip if this is status details screen
                parent = __instance.transform.parent;
                while (parent != null)
                {
                    if (parent.name.Contains("status") || parent.name.Contains("Status"))
                    {
                        return;
                    }
                    parent = parent.parent;
                }

                // Skip if this is ability menu navigation
                parent = __instance.transform.parent;
                while (parent != null)
                {
                    string parentName = parent.name.ToLower();
                    if (parentName.Contains("ability"))
                    {
                        return;
                    }
                    parent = parent.parent;
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

                // Skip if in save/load menu (handled by SaveListController.SelectContent patch)
                if (SaveLoadMenuState.IsActive)
                {
                    return;
                }

                // Skip if this is item target selection (handled by ItemUseController.SelectContent patch)
                var parent = __instance.transform.parent;
                while (parent != null)
                {
                    if (parent.name.Contains("item_target_select"))
                    {
                        return;
                    }
                    parent = parent.parent;
                }

                // Skip ALL battle navigation (battle controller patches handle everything in battle)
                var enemyEntities = UnityEngine.Object.FindObjectsOfType<Il2CppLast.Battle.BattleEnemyEntity>();
                if (enemyEntities != null && enemyEntities.Length > 0)
                {
                    return; // We're in battle - let controller patches handle it
                }

                // Skip if this is battle target selection
                parent = __instance.transform.parent;
                while (parent != null)
                {
                    string parentName = parent.name.ToLower();
                    if (parentName.Contains("battle_target") ||
                        parentName.Contains("battletarget") ||
                        parentName.Contains("battle_command") ||
                        parentName.Contains("battlecommand") ||
                        parentName.Contains("battle_item") ||
                        parentName.Contains("battleitem") ||
                        parentName.Contains("battle_ability") ||
                        parentName.Contains("battleability") ||
                        parentName.Contains("battle_infomation") ||
                        parentName.Contains("battleinfomation") ||
                        parentName.Contains("battle_menu") ||
                        parentName.Contains("battlemenu"))
                    {
                        return;
                    }
                    parent = parent.parent;
                }

                // Skip if this is title menu navigation
                parent = __instance.transform.parent;
                while (parent != null)
                {
                    if (parent.name.Contains("title_command") || parent.name.Contains("TitleMenu"))
                    {
                        return;
                    }
                    parent = parent.parent;
                }

                // Skip if this is config menu navigation
                parent = __instance.transform.parent;
                while (parent != null)
                {
                    if (parent.name.Contains("config") || parent.name.Contains("Config"))
                    {
                        return;
                    }
                    parent = parent.parent;
                }

                // Skip if this is item menu navigation (handled by ItemListController.SelectContent patch)
                parent = __instance.transform.parent;
                while (parent != null)
                {
                    if (parent.name.Contains("list_window"))
                    {
                        return;
                    }
                    parent = parent.parent;
                }

                // Skip if this is equipment menu navigation
                parent = __instance.transform.parent;
                while (parent != null)
                {
                    if (parent.name.Contains("equip_select"))
                    {
                        return;
                    }
                    parent = parent.parent;
                }

                // Skip if this is equipment slot navigation
                parent = __instance.transform.parent;
                while (parent != null)
                {
                    if (parent.name.Contains("equip_info_content") || parent.name.Contains("EquipmentInfo"))
                    {
                        return;
                    }
                    parent = parent.parent;
                }

                // Skip if this is shop navigation
                parent = __instance.transform.parent;
                while (parent != null)
                {
                    if (parent.name.Contains("shop") || parent.name.Contains("Shop"))
                    {
                        return;
                    }
                    parent = parent.parent;
                }

                // Skip if this is party setting menu
                parent = __instance.transform.parent;
                while (parent != null)
                {
                    if (parent.name.Contains("party") || parent.name.Contains("Party"))
                    {
                        return;
                    }
                    parent = parent.parent;
                }

                // Skip if this is status details screen
                parent = __instance.transform.parent;
                while (parent != null)
                {
                    if (parent.name.Contains("status") || parent.name.Contains("Status"))
                    {
                        return;
                    }
                    parent = parent.parent;
                }

                // Skip if this is ability menu navigation
                parent = __instance.transform.parent;
                while (parent != null)
                {
                    string parentName = parent.name.ToLower();
                    if (parentName.Contains("ability"))
                    {
                        return;
                    }
                    parent = parent.parent;
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
