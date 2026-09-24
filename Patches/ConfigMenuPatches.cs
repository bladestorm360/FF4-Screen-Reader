using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MelonLoader;
using Il2CppLast.UI.KeyInput;
using Il2CppLast.Management;
using FFIV_ScreenReader.Core;
using FFIV_ScreenReader.Menus;
using FFIV_ScreenReader.Utils;
using ConfigKeysSettingController = Il2CppLast.UI.KeyInput.ConfigKeysSettingController;
using ConfigControllCommandController = Il2CppLast.UI.KeyInput.ConfigControllCommandController;
using ConfigKeyIconController = Il2CppLast.UI.KeyInput.ConfigKeyIconController;
using OptionController = Il2CppLast.UI.KeyInput.OptionController;


// ConfigController is in base namespace
using FF4ConfigController = Il2CppLast.UI.KeyInput.ConfigController;

// Touch mode controllers
using ConfigActualDetailsControllerBase_Touch = Il2CppLast.UI.Touch.ConfigActualDetailsControllerBase;
using ConfigCommandController_Touch = Il2CppLast.UI.Touch.ConfigCommandController;

namespace FFIV_ScreenReader.Patches
{
    /// <summary>
    /// Manual patches for config menu state transitions.
    /// Hooks ConfigController.SetActive to clear state when config menu closes.
    /// </summary>
    public static class ConfigMenuStatePatches
    {
        private static bool isPatched = false;

        /// <summary>
        /// Apply manual Harmony patches for config menu state management.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched)
                return;

            try
            {
                // Patch ConfigController.SetActive(false) to clear state when config menu closes
                Type controllerType = typeof(FF4ConfigController);
                var setActiveMethod = controllerType.GetMethod("SetActive", BindingFlags.Instance | BindingFlags.Public);
                if (setActiveMethod != null)
                {
                    var postfix = typeof(ConfigMenuStatePatches).GetMethod(nameof(ConfigController_SetActive_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(setActiveMethod, postfix: new HarmonyMethod(postfix));
                }

                // Title-screen options (OptionController) hosts the Language dropdown and the
                // gamepad/keyboard remap sub-screens. Drive Config state from its lifecycle so the
                // Language-dropdown announce can gate on the menu actually being open.
                var optionSetActive = AccessTools.Method(typeof(OptionController), "SetActive", new Type[] { typeof(bool) });
                if (optionSetActive != null)
                {
                    harmony.Patch(optionSetActive,
                        prefix: new HarmonyMethod(AccessTools.Method(typeof(ConfigMenuStatePatches), nameof(OptionController_SetActive_Prefix))),
                        postfix: new HarmonyMethod(AccessTools.Method(typeof(ConfigMenuStatePatches), nameof(OptionController_SetActive_Postfix))));
                }
                else
                {
                    MelonLogger.Warning("[ConfigMenu] OptionController.SetActive not found");
                }

                // Focused config row on every open and on return from a sub-screen (the row guard
                // is cleared when a list gains focus; see ConfigActualDetails_SelectCommand_Patch).
                //   In game: KeyInput ConfigController.InitializeSelect (RVA 0x45B090) is the Select
                //   state's entry, reached on open (SetActive(true) queues Select) and on return from
                //   Setting / GameBoosterSetting; its SetDefaultSelect → SelectCommand reads the row.
                //   InitializeGameBoosterSetting (0x45AF40) enters the booster sub-screen the same way.
                //   Title options: OptionController.InitConfig (0x50B6D0; ShowConfig changes to the
                //   Config state) and the page Inits InitSelectLanguage / InitSelectScreenSetting /
                //   InitSelectSoundSettings (0x50C6C0 / 0x50CE00 / 0x50D820) enable a list's cursor;
                //   a postfix reads that list's focused row one frame later.
                void PatchFocusEntry(Type type, string method, bool readAfter)
                {
                    try
                    {
                        var m = AccessTools.Method(type, method);
                        if (m == null)
                        {
                            MelonLogger.Warning($"[ConfigMenu] {type.Name}.{method} not found");
                            return;
                        }
                        harmony.Patch(m,
                            prefix: new HarmonyMethod(AccessTools.Method(typeof(ConfigMenuStatePatches), nameof(ConfigListFocus_Prefix))),
                            postfix: readAfter
                                ? new HarmonyMethod(AccessTools.Method(typeof(ConfigMenuStatePatches), nameof(OptionPageInit_Postfix)))
                                : null);
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"[ConfigMenu] Error patching {type.Name}.{method}: {ex.Message}");
                    }
                }

                PatchFocusEntry(controllerType, "InitializeSelect", readAfter: false);
                PatchFocusEntry(controllerType, "InitializeGameBoosterSetting", readAfter: false);
                PatchFocusEntry(typeof(OptionController), "InitConfig", readAfter: true);
                PatchFocusEntry(typeof(OptionController), "InitSelectLanguage", readAfter: true);
                PatchFocusEntry(typeof(OptionController), "InitSelectScreenSetting", readAfter: true);
                PatchFocusEntry(typeof(OptionController), "InitSelectSoundSettings", readAfter: true);

                // Title-screen Language dropdown (keyboard/gamepad uses a Unity Dropdown driven by the
                // KeyInput OptionController). SetDropDownItemFocus is the discrete, event-driven hook —
                // it announces the focused language, gated on the config menu being open. DO NOT hook
                // OptionController.UpdateSelectLanguage — it is an EMPTY method (shared stub body);
                // detouring it corrupts every method that shares that body → launch crash.
                var dropDownFocus = AccessTools.Method(typeof(OptionController), "SetDropDownItemFocus");
                if (dropDownFocus != null)
                {
                    harmony.Patch(dropDownFocus, postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(ConfigMenuStatePatches), nameof(SetDropDownItemFocus_Postfix))));
                }
                else
                {
                    MelonLogger.Warning("[ConfigMenu] OptionController.SetDropDownItemFocus not found");
                }

                // Remap assign-flow speaking (ConfigKeysSettingController, all real-bodied methods).
                // KeyboardSettingInit / GamePadSettingInit fire on entering assign mode → "press a
                // key/button" prompt. ChangeKeySetting (overloaded keyboard + gamepad) fires when the
                // binding is applied → announce the new mapping.
                void PatchKeysSetting(string method, string postfixName)
                {
                    try
                    {
                        var m = AccessTools.Method(typeof(ConfigKeysSettingController), method);
                        if (m != null)
                        {
                            harmony.Patch(m, postfix: new HarmonyMethod(
                                AccessTools.Method(typeof(ConfigMenuStatePatches), postfixName)));
                        }
                        else
                        {
                            MelonLogger.Warning($"[ConfigMenu] ConfigKeysSettingController.{method} not found");
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"[ConfigMenu] Error patching ConfigKeysSettingController.{method}: {ex.Message}");
                    }
                }

                PatchKeysSetting("KeyboardSettingInit", nameof(KeyboardSettingInit_Postfix));
                PatchKeysSetting("GamePadSettingInit", nameof(GamePadSettingInit_Postfix));

                // Gamepad/Keyboard "Controls" pop-up (read-only list of every control): entering the
                // Help state renders the list once for KeyHelpReader's arrow/WASD navigation; returning
                // to the select list or closing the screen tears it down.
                PatchKeysSetting("GamePadHelpInit", nameof(GamePadHelpInit_Postfix));
                PatchKeysSetting("KeyboardHelpInit", nameof(KeyboardHelpInit_Postfix));
                PatchKeysSetting("GamePadSelectInit", nameof(ControlsHelpClose_Postfix));
                PatchKeysSetting("KeyboardSelectInit", nameof(ControlsHelpClose_Postfix));
                PatchKeysSetting("Close", nameof(ControlsHelpClose_Postfix));
                // NoneInit (the idle state; unique RVA 0x461EC0, real body) is another way out of Help.
                PatchKeysSetting("NoneInit", nameof(ControlsHelpClose_Postfix));

                // ChangeKeySetting is overloaded — patch every overload with the same __instance-only
                // postfix (avoids AmbiguousMatchException without needing an exact Type[]).
                try
                {
                    var changePostfix = new HarmonyMethod(AccessTools.Method(typeof(ConfigMenuStatePatches), nameof(ChangeKeySetting_Postfix)));
                    foreach (var m in typeof(ConfigKeysSettingController).GetMethods(
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                    {
                        if (m.Name == "ChangeKeySetting") harmony.Patch(m, postfix: changePostfix);
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[ConfigMenu] Error patching ChangeKeySetting: {ex.Message}");
                }

                isPatched = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[ConfigMenu] Error applying state patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for ConfigController.SetActive - clears state when menu closes.
        /// </summary>
        public static void ConfigController_SetActive_Postfix(FF4ConfigController __instance, bool isActive)
        {
            if (!isActive && MenuStates.Config.IsActive)
            {
                MenuStates.Config.Reset();
            }
        }

        /// <summary>
        /// Mirror of the in-game ConfigController.SetActive driver for the title-screen options menu.
        /// Drives Config state so the Language-dropdown announce can gate on the menu being open.
        /// </summary>
        public static void OptionController_SetActive_Prefix()
        {
            ConfigActualDetails_SelectCommand_Patch.SuppressReads = true;
        }

        /// <summary>A config list gains focus (open, sub-screen entry or return): clear the row guard.</summary>
        public static void ConfigListFocus_Prefix()
        {
            ConfigActualDetails_SelectCommand_Patch.ResetRowGuard();
        }

        /// <summary>Title-options page Init: read the focused list's row next frame.</summary>
        public static void OptionPageInit_Postfix(OptionController __instance)
        {
            try
            {
                ConfigActualDetails_SelectCommand_Patch.ScheduleFocusedRowRead(__instance);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[ConfigMenu] Error scheduling config row read: {ex.Message}");
            }
        }

        public static void OptionController_SetActive_Postfix(bool isActive)
        {
            ConfigActualDetails_SelectCommand_Patch.SuppressReads = false;
            if (isActive)
            {
                MenuStates.Config.SetActive();
            }
            else if (MenuStates.Config.IsActive)
            {
                MenuStates.Config.Reset();
            }
        }

        // ── Title-screen Language dropdown ──────────────────────────────────────────────

        /// <summary>Focused language label: prefer the tracked dropdown item, else the dropdown value.</summary>
        private static string GetFocusedLanguageLabel(OptionController inst)
        {
            var item = inst.selectedItem;
            if (item == null) return null;

            if (item.view != null && item.view.LabelText != null)
            {
                string t = item.view.LabelText.text;
                if (!string.IsNullOrWhiteSpace(t)) return t;
            }
            var dd = inst.selectedDoropDown;
            if (dd != null && dd.options != null && dd.value >= 0 && dd.value < dd.options.Count)
            {
                var opt = dd.options[dd.value];
                if (opt != null && !string.IsNullOrWhiteSpace(opt.text)) return opt.text;
            }
            // The CURRENT language's item has an empty label (its native name is a sprite). A focused
            // item with no readable label is therefore the current language → name it via the game.
            return ConfigMenuReader.GetCurrentLanguageDisplayName();
        }

        /// <summary>
        /// EVENT-DRIVEN announce. OptionController.SetDropDownItemFocus is the discrete "focused
        /// dropdown item changed" hook — speaks the focused language directly, gated on the config
        /// menu being open (the title screen fires this during load before the menu is opened).
        /// </summary>
        public static void SetDropDownItemFocus_Postfix(OptionController __instance)
        {
            try
            {
                if (__instance == null) return;
                if (!MenuStates.Config.IsActive) return;
                string label = GetFocusedLanguageLabel(__instance);
                if (string.IsNullOrWhiteSpace(label)) return;
                label = label.Trim();
                var focusedDropdown = __instance.selectedDoropDown;
                if (focusedDropdown != null && focusedDropdown.options != null)
                    label = FFIV_ScreenReader.Utils.MenuPosition.Format(label, focusedDropdown.value, focusedDropdown.options.Count);
                FFIV_ScreenReaderMod.SpeakText(label, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in SetDropDownItemFocus patch: {ex.Message}");
            }
        }

        // ── Remap assign-flow (ConfigKeysSettingController) ──────────────────────────────
        // Entering assign mode → announce the "press a key/button" prompt. Init methods fire once on
        // state entry → event-driven, no dedup.
        public static void KeyboardSettingInit_Postfix(ConfigKeysSettingController __instance)
            => AnnounceAssignPrompt(gamepad: false);

        public static void GamePadSettingInit_Postfix(ConfigKeysSettingController __instance)
            => AnnounceAssignPrompt(gamepad: true);

        private static void AnnounceAssignPrompt(bool gamepad)
        {
            try
            {
                FFIV_ScreenReaderMod.SpeakText(gamepad ? ModTextTranslator.T("Press a button.") : ModTextTranslator.T("Press a key."), interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in assign-prompt patch: {ex.Message}");
            }
        }

        // ── Gamepad/Keyboard Controls pop-up → KeyHelpReader navigation ──
        public static void GamePadHelpInit_Postfix(ConfigKeysSettingController __instance)
            => CoroutineManager.StartManaged(DelayedOpenControlsHelp(__instance, gamepad: true));

        public static void KeyboardHelpInit_Postfix(ConfigKeysSettingController __instance)
            => CoroutineManager.StartManaged(DelayedOpenControlsHelp(__instance, gamepad: false));

        public static void ControlsHelpClose_Postfix() => KeyHelpReader.CloseControlsHelp();

        /// <summary>
        /// One frame after the Help state opens (so each row's binding text is populated), renders
        /// the help list through the same builder the remap list uses. The state machine can cycle
        /// its Help Init during scene construction, so only build while the screen is on-screen.
        /// </summary>
        private static System.Collections.IEnumerator DelayedOpenControlsHelp(ConfigKeysSettingController inst, bool gamepad)
        {
            yield return null;

            try
            {
                if (inst == null || inst.gameObject == null || !inst.gameObject.activeInHierarchy)
                {
                    KeyHelpReader.CloseControlsHelp();
                    yield break;
                }

                var list = gamepad ? inst.HelpContentList : inst.KeyboardHelpContentList;
                var entries = new System.Collections.Generic.List<string>();
                if (list != null)
                {
                    foreach (var command in list)
                    {
                        string entry = ConfigKeysSettingController_SelectContent_Patch.BuildCommandAnnouncement(inst, command);
                        if (!string.IsNullOrWhiteSpace(entry))
                            entries.Add(entry);
                    }
                }
                KeyHelpReader.OpenControlsHelp(inst, entries);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading controls help list: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for ConfigKeysSettingController.ChangeKeySetting (all overloads). Fires when a
        /// binding is applied — re-reads the just-edited command and announces the new mapping.
        /// Event-driven, no dedup.
        /// </summary>
        public static void ChangeKeySetting_Postfix(ConfigKeysSettingController __instance)
        {
            try
            {
                if (__instance == null) return;
                string announcement = ConfigKeysSettingController_SelectContent_Patch.BuildCommandAnnouncement(
                    __instance, __instance.selectedCommand);
                if (string.IsNullOrWhiteSpace(announcement)) return;
                FFIV_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ChangeKeySetting patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Controller-based patches for config menus (both title and in-game).
    /// Announces menu items directly from ConfigCommandController instead of hierarchy walking.
    ///
    /// Hook (2026-09-24): KeyInput ConfigActualDetailsControllerBase.SelectCommand(Cursor, WithinRangeType)
    /// (private, RVA 0x73A1D0, unique). It stores the focused row in SelectedCommand (@0x20) and is
    /// called by Initialize, ResetCursor, SetDefaultSelect, the mouse handler and the up/down move
    /// callbacks (&lt;UpdateController&gt;b__1 / b__7). It replaces a ConfigCommandController.SetFocus
    /// postfix that ran for every row every frame: the menu's per-frame UpdateController calls
    /// UpdateFocus, which calls SetFocus on each row to set its colours.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.UI.KeyInput.ConfigActualDetailsControllerBase), "SelectCommand")]
    public static class ConfigActualDetails_SelectCommand_Patch
    {
        private const string DEDUP_CONTEXT = AnnouncementContexts.CONFIG_COMMAND;
        private static bool retryScheduled;
        private static bool focusedRowReadScheduled;

        /// <summary>
        /// Set while the title OptionController.SetActive runs: its body calls ResetCursor (→
        /// SelectCommand) on every page's list, visible or not. The page Init that follows reads the
        /// focused list instead.
        /// </summary>
        internal static bool SuppressReads;

        /// <summary>
        /// The row guard only drops repeats while a list keeps focus. It is cleared whenever a list
        /// (re)gains focus, so the focused row is always read on open and on return from a sub-screen.
        /// </summary>
        internal static void ResetRowGuard() => AnnouncementDeduplicator.Reset(DEDUP_CONTEXT);

        /// <summary>
        /// One frame after a title-options page Init, reads the focused row of the list whose cursor is
        /// shown (the page Inits enable the page's cursor with SetEnableCursor but don't call
        /// SelectCommand). Goes through the same guard, so a SelectCommand read in the Init body
        /// isn't repeated.
        /// </summary>
        internal static void ScheduleFocusedRowRead(OptionController option)
        {
            if (option == null || focusedRowReadScheduled) return;
            focusedRowReadScheduled = true;
            CoroutineManager.StartManaged(ReadFocusedRowNextFrame(option));
        }

        private static System.Collections.IEnumerator ReadFocusedRowNextFrame(OptionController option)
        {
            yield return null;
            focusedRowReadScheduled = false;
            try
            {
                if (TryAnnounceFocusedList(option.configActualDetailsController)) yield break;
                var lists = option.configControllerList;
                if (lists == null) yield break;
                foreach (var list in lists)
                {
                    if (TryAnnounceFocusedList(list)) yield break;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in config focused-row read: {ex.Message}");
            }
        }

        /// <summary>Reads the list's focused row if its cursor is shown. True when the list had focus.</summary>
        private static bool TryAnnounceFocusedList(Il2CppLast.UI.KeyInput.ConfigActualDetailsControllerBase list)
        {
            var cursorObject = list?.selectCursor?.gameObject;
            if (cursorObject == null || !cursorObject.activeInHierarchy) return false;
            var command = list.SelectedCommand;
            if (command == null) return false;
            Announce(command);
            return true;
        }

        [HarmonyPostfix]
        public static void Postfix(Il2CppLast.UI.KeyInput.ConfigActualDetailsControllerBase __instance)
        {
            try
            {
                if (SuppressReads) return;

                var command = __instance?.SelectedCommand;
                if (command == null) return;

                // Focus placed while the menu is still being shown (open): read it once the menu is
                // on screen, one frame later (the old per-frame hook read it on its first visible frame).
                if (!Announce(command) && !retryScheduled)
                {
                    retryScheduled = true;
                    CoroutineManager.StartManaged(RetryNextFrame(__instance));
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ConfigActualDetailsControllerBase.SelectCommand patch: {ex.Message}");
            }
        }

        private static System.Collections.IEnumerator RetryNextFrame(Il2CppLast.UI.KeyInput.ConfigActualDetailsControllerBase controller)
        {
            yield return null;
            retryScheduled = false;
            try
            {
                var command = controller?.SelectedCommand;
                if (command != null)
                    Announce(command);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in config focus retry: {ex.Message}");
            }
        }

        /// <summary>
        /// Speaks the focused row (name and value). Returns false only when the menu isn't on
        /// screen yet.
        /// </summary>
        private static bool Announce(ConfigCommandController __instance)
        {
            try
            {
                // Safety checks
                if (__instance == null)
                {
                    return true;
                }

                // IMPORTANT: Check if the config menu is actually visible
                // This prevents announcements during initialization/map load
                if (__instance.gameObject == null || !__instance.gameObject.activeInHierarchy)
                {
                    return false;
                }

                // Check for a visible canvas parent (menu must be on screen)
                var canvas = __instance.GetComponentInParent<UnityEngine.Canvas>();
                if (canvas == null || !canvas.enabled)
                {
                    return false;
                }

                // Get the view which contains the localized text
                var view = __instance.view;
                if (view == null)
                {
                    return true;
                }

                // Get the name text (localized)
                var nameText = view.nameText;
                if (nameText == null || string.IsNullOrWhiteSpace(nameText.text))
                {
                    return true;
                }

                string menuText = nameText.text.Trim();

                // Skip duplicate announcements
                if (!AnnouncementDeduplicator.ShouldAnnounce(DEDUP_CONTEXT, menuText))
                {
                    return true;
                }

                // Set config menu state active
                MenuStates.Config.SetActive();

                // Also try to get the current value for this config option
                string configValue = ConfigMenuReader.FindConfigValueFromController(__instance);

                string announcement = menuText;
                if (!string.IsNullOrWhiteSpace(configValue))
                {
                    announcement = $"{menuText}: {configValue}";
                }

                FFIV_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error announcing config row: {ex.Message}");
            }
            return true;
        }
    }

    /// <summary>
    /// Patch for keyboard/gamepad/mouse control settings.
    /// Announces action name and current key binding.
    /// </summary>
    [HarmonyPatch(typeof(ConfigKeysSettingController), nameof(ConfigKeysSettingController.SelectContent),
        new Type[] { typeof(int), typeof(Il2CppLast.UI.CustomScrollView), typeof(Il2CppLast.UI.Cursor),
                     typeof(Il2CppSystem.Collections.Generic.IEnumerable<ConfigControllCommandController>),
                     typeof(Il2CppLast.UI.CustomScrollView.WithinRangeType) })]
    public static class ConfigKeysSettingController_SelectContent_Patch
    {
        private const string DEDUP_CONTEXT = AnnouncementContexts.CONFIG_KEYS_SETTING;

        [HarmonyPostfix]
        public static void Postfix(ConfigKeysSettingController __instance, int index,
            Il2CppSystem.Collections.Generic.IEnumerable<ConfigControllCommandController> contentList)
        {
            try
            {
                if (__instance == null || contentList == null)
                    return;

                // Check if the config menu is actually visible
                if (__instance.gameObject == null || !__instance.gameObject.activeInHierarchy)
                    return;

                var canvas = __instance.GetComponentInParent<UnityEngine.Canvas>();
                if (canvas == null || !canvas.enabled)
                    return;

                // Convert to list for index access
                var list = contentList.TryCast<Il2CppSystem.Collections.Generic.List<ConfigControllCommandController>>();
                var command = SelectContentHelper.TryGetItem(list, index);
                if (command == null)
                    return;

                string announcement = BuildCommandAnnouncement(__instance, command);
                if (string.IsNullOrWhiteSpace(announcement))
                {
                    return;
                }

                // Skip duplicate announcements
                if (!AnnouncementDeduplicator.ShouldAnnounce(DEDUP_CONTEXT, announcement))
                {
                    return;
                }

                // Set config menu state active
                MenuStates.Config.SetActive();

                announcement = FFIV_ScreenReader.Utils.MenuPosition.Format(announcement, index, list.Count);
                FFIV_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ConfigKeysSettingController.SelectContent patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Builds the controls-screen announcement for one command: action name + keyboard binding
        /// (readable key names) + gamepad binding. The gamepad icon is an unreadable controller glyph,
        /// so we translate the LIVE bound button (from the screen's KeyConfigData) to family-aware text
        /// via ControllerLabels. Shared by the navigation read (SelectContent) and the rebind read
        /// (ChangeKeySetting).
        /// </summary>
        internal static string BuildCommandAnnouncement(
            ConfigKeysSettingController owner,
            ConfigControllCommandController command)
        {
            if (command == null) return null;

            var textParts = new System.Collections.Generic.List<string>();

            // Action name from the view's nameTexts
            if (command.view != null && command.view.nameTexts != null && command.view.nameTexts.Count > 0)
            {
                foreach (var textComp in command.view.nameTexts)
                {
                    if (textComp != null && !string.IsNullOrWhiteSpace(textComp.text))
                    {
                        string text = textComp.text.Trim();
                        if (!text.StartsWith("MENU_") && !textParts.Contains(text))
                        {
                            textParts.Add(text);
                        }
                    }
                }
            }

            // Keyboard binding — already readable key names.
            AppendIconTexts(textParts, command.keyboardIconController);

            // Gamepad binding — the icon is a sprite glyph carrying NO readable text (iconTextList is
            // empty), so reading it never worked. The keyboard and gamepad remap sections are mutually
            // exclusive per row: keyboard rows carry a key name, gamepad rows don't. So when the keyboard
            // icon is empty we're on the gamepad section — translate the LIVE bound button via
            // ControllerLabels.
            if (ResolveGamepadButtonText(owner, command) is string btn && !string.IsNullOrEmpty(btn)
                && !IconHasContent(command.keyboardIconController))
            {
                textParts.Add($"({btn})");
            }

            return textParts.Count == 0 ? null : string.Join(" ", textParts);
        }

        /// <summary>Appends an icon controller's binding labels (iconTextList) to textParts, deduped.</summary>
        private static void AppendIconTexts(System.Collections.Generic.List<string> textParts, ConfigKeyIconController icon)
        {
            var iconView = icon?.view;
            if (iconView == null || iconView.iconTextList == null) return;
            for (int i = 0; i < iconView.iconTextList.Count; i++)
            {
                var iconText = iconView.iconTextList[i];
                if (iconText != null && !string.IsNullOrWhiteSpace(iconText.text))
                {
                    string text = iconText.text.Trim();
                    if (!textParts.Contains(text))
                        textParts.Add(text);
                }
            }
        }

        /// <summary>True if an icon controller is currently showing readable binding text.</summary>
        private static bool IconHasContent(ConfigKeyIconController icon)
        {
            var iconView = icon?.view;
            if (iconView == null || iconView.iconTextList == null) return false;
            for (int i = 0; i < iconView.iconTextList.Count; i++)
            {
                var t = iconView.iconTextList[i];
                if (t != null && !string.IsNullOrWhiteSpace(t.text)) return true;
            }
            return false;
        }

        /// <summary>
        /// Resolves a remap row's CURRENT (remappable) gamepad button to family-aware text. Reads the
        /// live binding from the screen's KeyConfigData (GameKey → Unity KeyCode), maps the KeyCode to
        /// an SDL button index, and lets ControllerLabels pick the text for the connected controller.
        /// Returns null if it can't be resolved.
        /// </summary>
        private static string ResolveGamepadButtonText(
            ConfigKeysSettingController owner,
            ConfigControllCommandController command)
        {
            try
            {
                if (owner == null || command == null) return null;
                var kd = owner.keydata;
                if (kd == null) return null;
                var dict = kd.GetGamePadKeyConfigtDictionary();
                if (dict == null || !dict.ContainsKey(command.Key)) return null;
                int sdl = JoystickKeyCodeToSdlButton((int)dict[command.Key]);
                if (sdl < 0) return null;
                return ControllerLabels.GetButtonLabel(sdl);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Maps a Unity legacy KeyCode.JoystickButtonN (330+) to an SDL gamepad button index using the
        /// XInput-standard layout. ControllerLabels then yields the right family text for whichever
        /// controller is connected. Returns -1 if not a mapped button.
        /// </summary>
        private static int JoystickKeyCodeToSdlButton(int keyCode)
        {
            switch (keyCode)
            {
                // FFPR keeps the bottom/right face buttons in the Japanese arrangement: Confirm is
                // stored on JoystickButton1 and Cancel on JoystickButton0. The American build confirms
                // with the BOTTOM button (A/Cross), so JB1→SOUTH and JB0→EAST — i.e. swapped from
                // Unity's XInput default. (X/Y below are unaffected.)
                case 330: return SDL3.SDL_GAMEPAD_BUTTON_EAST;           // JoystickButton0 — Cancel (B/Circle)
                case 331: return SDL3.SDL_GAMEPAD_BUTTON_SOUTH;          // JoystickButton1 — Confirm (A/Cross)
                case 332: return SDL3.SDL_GAMEPAD_BUTTON_WEST;           // JoystickButton2 — X/Square
                case 333: return SDL3.SDL_GAMEPAD_BUTTON_NORTH;          // JoystickButton3 — Y/Triangle
                case 334: return SDL3.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER;  // JoystickButton4 — LB
                case 335: return SDL3.SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER; // JoystickButton5 — RB
                case 336: return SDL3.SDL_GAMEPAD_BUTTON_BACK;           // JoystickButton6 — Back/View
                case 337: return SDL3.SDL_GAMEPAD_BUTTON_START;          // JoystickButton7 — Start/Menu
                case 338: return SDL3.SDL_GAMEPAD_BUTTON_LEFT_STICK;     // JoystickButton8 — LS
                case 339: return SDL3.SDL_GAMEPAD_BUTTON_RIGHT_STICK;    // JoystickButton9 — RS
                default: return -1;
            }
        }
    }

    /// <summary>
    /// Patch for SetNextSelect (KeyInput) - called when cycling forward through arrow-select options.
    /// Input-agnostic: works with keyboard, mouse, or controller.
    /// </summary>
    [HarmonyPatch(typeof(ConfigCommandController), nameof(ConfigCommandController.SetNextSelect))]
    public static class ConfigCommandController_SetNextSelect_Patch
    {
        private const string DEDUP_CONTEXT = AnnouncementContexts.CONFIG_ARROW_VALUE;

        [HarmonyPostfix]
        public static void Postfix(ConfigCommandController __instance)
        {
            try
            {
                if (__instance == null) return;

                // Get the displayed arrow value text
                string value = ConfigMenuReader.GetArrowChangeText(__instance);
                if (string.IsNullOrEmpty(value)) return;

                // Only announce if value changed
                if (!AnnouncementDeduplicator.ShouldAnnounce(DEDUP_CONTEXT, value))
                {
                    return;
                }

                FFIV_ScreenReaderMod.SpeakText(value, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in SetNextSelect patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch for SetPrevSelect (KeyInput) - called when cycling backward through arrow-select options.
    /// Input-agnostic: works with keyboard, mouse, or controller.
    /// </summary>
    [HarmonyPatch(typeof(ConfigCommandController), nameof(ConfigCommandController.SetPrevSelect))]
    public static class ConfigCommandController_SetPrevSelect_Patch
    {
        private const string DEDUP_CONTEXT = AnnouncementContexts.CONFIG_ARROW_VALUE;

        [HarmonyPostfix]
        public static void Postfix(ConfigCommandController __instance)
        {
            try
            {
                if (__instance == null) return;

                // Get the displayed arrow value text
                string value = ConfigMenuReader.GetArrowChangeText(__instance);
                if (string.IsNullOrEmpty(value)) return;

                // Only announce if value changed
                if (!AnnouncementDeduplicator.ShouldAnnounce(DEDUP_CONTEXT, value))
                {
                    return;
                }

                FFIV_ScreenReaderMod.SpeakText(value, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in SetPrevSelect patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch for SetSliderValue (KeyInput) - called when slider value changes.
    /// Only announces if this controller is the currently selected option (not during init).
    /// Input-agnostic: works with keyboard, mouse, or controller.
    /// </summary>
    [HarmonyPatch(typeof(ConfigCommandController), nameof(ConfigCommandController.SetSliderValue))]
    public static class ConfigCommandController_SetSliderValue_Patch
    {
        private const string DEDUP_CONTEXT = AnnouncementContexts.CONFIG_SLIDER_VALUE;

        [HarmonyPostfix]
        public static void Postfix(ConfigCommandController __instance, float value)
        {
            try
            {
                if (__instance == null) return;

                // Check if this controller is the currently selected one (filters out init calls)
                var detailsController = UnityEngine.Object.FindObjectOfType<Il2CppLast.UI.KeyInput.ConfigActualDetailsControllerBase>();
                if (detailsController == null || detailsController.SelectedCommand != __instance)
                {
                    return;
                }

                // Get the displayed slider value text (reads the UI text directly)
                string textValue = ConfigMenuReader.GetSliderValueText(__instance);
                if (string.IsNullOrEmpty(textValue)) return;

                // Check if value changed
                if (!AnnouncementDeduplicator.ShouldAnnounce(DEDUP_CONTEXT, textValue))
                {
                    return;
                }

                FFIV_ScreenReaderMod.SpeakText(textValue, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in SetSliderValue patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Wrapper class to hold string value in ConditionalWeakTable (requires reference type).
    /// </summary>
    internal class StringHolder
    {
        public string Value;
        public StringHolder(string value) { Value = value; }
    }

    /// <summary>
    /// Patch for SetArrowChangeText (Touch) - called when arrow-select text changes.
    /// Uses controller+value tracking to filter init calls and only announce user changes.
    /// Input-agnostic: works with touch or any input method.
    /// Uses ConditionalWeakTable to prevent memory leak when controllers are destroyed.
    /// </summary>
    [HarmonyPatch(typeof(ConfigCommandController_Touch), "SetArrowChangeText")]
    public static class ConfigCommandControllerTouch_SetArrowChangeText_Patch
    {
        private const string DEDUP_CONTEXT = AnnouncementContexts.CONFIG_TOUCH_ARROW_VALUE;
        // Track last value per controller using weak references to prevent memory leak
        private static readonly ConditionalWeakTable<ConfigCommandController_Touch, StringHolder> lastValues
            = new ConditionalWeakTable<ConfigCommandController_Touch, StringHolder>();

        [HarmonyPostfix]
        public static void Postfix(ConfigCommandController_Touch __instance, string text)
        {
            try
            {
                if (__instance == null || string.IsNullOrEmpty(text)) return;

                // Controller must be active and visible
                if (__instance.gameObject == null || !__instance.gameObject.activeInHierarchy)
                {
                    return;
                }

                string value = text.Trim();
                if (string.IsNullOrEmpty(value)) return;

                // Check if we've seen this controller before
                if (lastValues.TryGetValue(__instance, out StringHolder holder))
                {
                    // Same value = no change, don't announce
                    if (holder.Value == value) return;

                    // Value changed - this is a user action, announce it
                    holder.Value = value;
                    FFIV_ScreenReaderMod.SpeakText(value, interrupt: true);
                }
                else
                {
                    // First time seeing this controller - init call, just track it
                    lastValues.Add(__instance, new StringHolder(value));
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in Touch SetArrowChangeText patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch for SetSliderCurrentValue (Touch) - called when slider value changes.
    /// Uses controller+value tracking to filter init calls and only announce user changes.
    /// Input-agnostic: works with touch or any input method.
    /// Uses ConditionalWeakTable to prevent memory leak when controllers are destroyed.
    /// </summary>
    [HarmonyPatch(typeof(ConfigCommandController_Touch), "SetSliderCurrentValue")]
    public static class ConfigCommandControllerTouch_SetSliderCurrentValue_Patch
    {
        private const string DEDUP_CONTEXT = AnnouncementContexts.CONFIG_TOUCH_SLIDER_VALUE;
        // Track last value per controller using weak references to prevent memory leak
        private static readonly ConditionalWeakTable<ConfigCommandController_Touch, StringHolder> lastValues
            = new ConditionalWeakTable<ConfigCommandController_Touch, StringHolder>();

        [HarmonyPostfix]
        public static void Postfix(ConfigCommandController_Touch __instance, float value)
        {
            try
            {
                if (__instance == null) return;

                // Controller must be active and visible
                if (__instance.gameObject == null || !__instance.gameObject.activeInHierarchy)
                {
                    return;
                }

                // Get the displayed slider value text (reads the UI text directly)
                string textValue = ConfigMenuReader.GetSliderValueText(__instance);
                if (string.IsNullOrEmpty(textValue)) return;

                // Check if we've seen this controller before
                if (lastValues.TryGetValue(__instance, out StringHolder holder))
                {
                    // Same value = no change, don't announce
                    if (holder.Value == textValue) return;

                    // Value changed - this is a user action, announce it
                    holder.Value = textValue;
                    FFIV_ScreenReaderMod.SpeakText(textValue, interrupt: true);
                }
                else
                {
                    // First time seeing this controller - init call, just track it
                    lastValues.Add(__instance, new StringHolder(textValue));
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in Touch SetSliderCurrentValue patch: {ex.Message}");
            }
        }
    }
}
