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

// Import MenuState classes
using ConfigMenuState = FFIV_ScreenReader.Core.ConfigMenuState;

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
            if (!isActive && ConfigMenuState.IsActive)
            {
                ConfigMenuState.Reset();
            }
        }
    }

    /// <summary>
    /// Controller-based patches for config menus (both title and in-game).
    /// Announces menu items directly from ConfigCommandController instead of hierarchy walking.
    /// </summary>

    [HarmonyPatch(typeof(ConfigCommandController), nameof(ConfigCommandController.SetFocus))]
    public static class ConfigCommandController_SetFocus_Patch
    {
        private const string DEDUP_CONTEXT = "ConfigMenu.Command";

        [HarmonyPostfix]
        public static void Postfix(ConfigCommandController __instance, bool isFocus, bool isSelectable)
        {
            try
            {
                // Only announce when gaining focus (not losing it)
                if (!isFocus)
                {
                    return;
                }

                // Safety checks
                if (__instance == null)
                {
                    return;
                }

                // IMPORTANT: Check if the config menu is actually visible
                // This prevents announcements during initialization/map load
                if (__instance.gameObject == null || !__instance.gameObject.activeInHierarchy)
                {
                    return;
                }

                // Check for a visible canvas parent (menu must be on screen)
                var canvas = __instance.GetComponentInParent<UnityEngine.Canvas>();
                if (canvas == null || !canvas.enabled)
                {
                    return;
                }

                // Get the view which contains the localized text
                var view = __instance.view;
                if (view == null)
                {
                    return;
                }

                // Get the name text (localized)
                var nameText = view.nameText;
                if (nameText == null || string.IsNullOrWhiteSpace(nameText.text))
                {
                    return;
                }

                string menuText = nameText.text.Trim();

                // Skip duplicate announcements
                if (!AnnouncementDeduplicator.ShouldAnnounce(DEDUP_CONTEXT, menuText))
                {
                    return;
                }

                // Set config menu state active
                ConfigMenuState.SetActive();

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
                MelonLogger.Warning($"Error in ConfigCommandController.SetFocus patch: {ex.Message}");
            }
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
        private const string DEDUP_CONTEXT = "ConfigMenu.KeysSetting";

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

                var textParts = new System.Collections.Generic.List<string>();

                // Read action name from the view's nameTexts
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

                // Read key bindings from keyboardIconController.view (only works for keyboard settings)
                if (command.keyboardIconController != null && command.keyboardIconController.view != null)
                {
                    // Read from iconTextList - contains the actual key names (e.g., "Enter", "Backspace")
                    if (command.keyboardIconController.view.iconTextList != null)
                    {
                        for (int i = 0; i < command.keyboardIconController.view.iconTextList.Count; i++)
                        {
                            var iconText = command.keyboardIconController.view.iconTextList[i];
                            if (iconText != null && !string.IsNullOrWhiteSpace(iconText.text))
                            {
                                string text = iconText.text.Trim();
                                if (!textParts.Contains(text))
                                {
                                    textParts.Add(text);
                                }
                            }
                        }
                    }
                }

                if (textParts.Count == 0)
                {
                    return;
                }

                string announcement = string.Join(" ", textParts);

                // Skip duplicate announcements
                if (!AnnouncementDeduplicator.ShouldAnnounce(DEDUP_CONTEXT, announcement))
                {
                    return;
                }

                // Set config menu state active
                ConfigMenuState.SetActive();

                FFIV_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ConfigKeysSettingController.SelectContent patch: {ex.Message}");
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
        private const string DEDUP_CONTEXT = "ConfigMenu.ArrowValue";

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
        private const string DEDUP_CONTEXT = "ConfigMenu.ArrowValue";

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
        private const string DEDUP_CONTEXT = "ConfigMenu.SliderValue";

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
        private const string DEDUP_CONTEXT = "ConfigMenu.TouchArrowValue";
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
        private const string DEDUP_CONTEXT = "ConfigMenu.TouchSliderValue";
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
