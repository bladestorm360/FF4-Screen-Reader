using System;
using HarmonyLib;
using MelonLoader;
using FFIV_ScreenReader.Menus;
using FFIV_ScreenReader.Utils;

using ItemWindowController = Il2CppLast.UI.KeyInput.ItemWindowController;
using EquipmentWindowController = Il2CppLast.UI.KeyInput.EquipmentWindowController;
using GameCursor = Il2CppLast.UI.Cursor;

namespace FFIV_ScreenReader.Patches
{
    /// <summary>
    /// Announces the focused command of the item (Use / Key Items / Sort) and equipment
    /// (Equip / Optimal / Remove All) command bars whenever the bar gains focus — on entry from the
    /// main menu and on back-out from a list. Navigation already flows through the generic cursor
    /// reader, but placing the cursor on entry never fires Cursor.NextIndex, so the focused command
    /// was silent. The command-state Init methods fire exactly on (re)entry, so they read the bar's
    /// selectCursor through the same reader navigation uses.
    /// </summary>
    internal static class CommandBarReader
    {
        internal static void AnnounceFocus(GameCursor cursor, int count)
        {
            if (cursor != null)
                CoroutineManager.StartManaged(MenuTextDiscovery.WaitAndReadCursor(cursor, "Navigate", count, false));
        }
    }

    [HarmonyPatch(typeof(ItemWindowController), "CommandSelectInit")]
    public static class ItemWindowController_CommandSelectInit_Announce_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemWindowController __instance)
        {
            try
            {
                var command = __instance?.commandController;
                CommandBarReader.AnnounceFocus(command?.selectCursor, command?.contentList?.Count ?? 0);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[CommandBar] Error announcing item command focus: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(EquipmentWindowController), "CommandInit")]
    public static class EquipmentWindowController_CommandInit_Announce_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(EquipmentWindowController __instance)
        {
            try
            {
                var command = __instance?.commandController;
                CommandBarReader.AnnounceFocus(command?.selectCursor, command?.contents?.Count ?? 0);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[CommandBar] Error announcing equipment command focus: {ex.Message}");
            }
        }
    }
}
