using System;
using HarmonyLib;
using MelonLoader;
using FFIV_ScreenReader.Core;
using Il2CppLast.Management;
using static FFIV_ScreenReader.Utils.ModTextTranslator;

namespace FFIV_ScreenReader.Patches
{
    /// <summary>
    /// Narrates the game's own walk/run (F1) and encounter (F3) field toggles whatever the input
    /// source (keyboard, a stick click passed through to the game, anything that drives the field
    /// toggle) by hooking the game's setting setters instead of watching keys.
    ///
    /// Direct-call xrefs in FF4's GameAssembly.dll (tools/hitscan.py, 2026-09-23):
    ///   CheatSettingsClient.SetIsEnableEncount (RVA 0x9179E0, unique) ← FieldMap.UpdatePlayerStatePlay
    ///     (the field toggle), ConfigActualDetailsControllerBase.SetEnableEncount ×2 (config menu),
    ///     SaveSlotManager.&lt;GotoLoadSaveData&gt;d__50.MoveNext (loading a save).
    ///   ConfigClient.SetIsAutoDash (RVA 0x91ABC0, unique) ← FieldMap.UpdatePlayerStatePlay,
    ///     ConfigActualDetailsControllerBase.SetIsAutoDash and SwitchArrowSelectTypeProcess (config menu).
    ///
    /// Only the field toggle should speak. The config menu announces its own row, and a load is not
    /// a toggle (the load re-applies the value it just deserialized, so it is never a real change).
    /// The field caller runs only while SubSceneManagerMainGame is in its Player state, the config
    /// menu runs in the Menu state, and the title-screen load has no MainGame sub-scene at all, so
    /// the gate reads the game's own current state at the moment of the call (no polling).
    ///
    /// Prefixes, so the old value is still readable: "real change" is exact with no seeding.
    /// Do NOT hook CheatSettingsData.set_IsEnableEncount instead: its body is folded with 21 other
    /// setters, so a detour there would fire for all of them.
    /// </summary>
    public static class GameTogglePatches
    {
        // SubSceneManagerMainGame.State.Player — the only state whose update runs
        // FieldMap.UpdatePlayerStatePlay (the field toggle's caller).
        private const int MAIN_GAME_STATE_PLAYER = 3;

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            Patch(harmony, typeof(CheatSettingsClient), "SetIsEnableEncount", nameof(SetIsEnableEncount_Prefix));
            Patch(harmony, typeof(ConfigClient), "SetIsAutoDash", nameof(SetIsAutoDash_Prefix));
        }

        private static void Patch(HarmonyLib.Harmony harmony, Type type, string method, string prefixName)
        {
            try
            {
                var target = AccessTools.Method(type, method);
                if (target == null)
                {
                    MelonLogger.Warning($"[GameToggle] {type.Name}.{method} not found - that toggle will be silent");
                    return;
                }
                harmony.Patch(target, prefix: new HarmonyMethod(AccessTools.Method(typeof(GameTogglePatches), prefixName)));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[GameToggle] Failed to patch {type.Name}.{method}: {ex.Message}");
            }
        }

        /// <summary>The field toggle, as opposed to the config menu or a save load.</summary>
        private static bool IsFieldToggle()
        {
            try
            {
                var sub = Il2CppLast.Management.SceneManager.Instance?.GetCurrentSubSceneManager();
                if (sub == null) return false;
                var mainGame = sub.TryCast<SubSceneManagerMainGame>();
                if (mainGame == null) return false; // title / splash / extras: never the field toggle
                return (int)mainGame.GetCurrentState() == MAIN_GAME_STATE_PLAYER;
            }
            catch (Exception ex)
            {
                // Could not read the game state: fall back to the mod's own field gate.
                MelonLogger.Warning($"[GameToggle] Game state unreadable, using field gate: {ex.Message}");
                return InputManager.IsOnValidMap() && !MenuStateRegistry.AnyActive();
            }
        }

        // __0 = isEnable, the value about to be written.
        public static void SetIsEnableEncount_Prefix(bool __0)
        {
            try
            {
                var cheat = UserDataManager.Instance()?.CheatSettingsData;
                if (cheat == null || cheat.IsEnableEncount == __0) return;
                if (!IsFieldToggle()) return;
                FFIV_ScreenReaderMod.SpeakText(__0 ? T("Encounters on") : T("Encounters off"), interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[GameToggle] Error announcing encounter toggle: {ex.Message}");
            }
        }

        // __0 = the new auto-dash value (0 = walk by default, non-zero = run by default).
        public static void SetIsAutoDash_Prefix(int __0)
        {
            try
            {
                var config = UserDataManager.Instance()?.Config;
                if (config == null || (config.IsAutoDash != 0) == (__0 != 0)) return;
                if (!IsFieldToggle()) return;
                FFIV_ScreenReaderMod.SpeakText(__0 != 0 ? T("Run") : T("Walk"), interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[GameToggle] Error announcing walk/run toggle: {ex.Message}");
            }
        }
    }
}
