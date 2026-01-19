using System;
using FFIV_ScreenReader.Core;
using FFIV_ScreenReader.Patches;
using Il2CppLast.UI;
using Il2CppLast.UI.Touch;
using MelonLoader;
using UnityEngine;
using GameCursor = Il2CppLast.UI.Cursor;
using ConfigCommandView_Touch = Il2CppLast.UI.Touch.ConfigCommandView;
using ConfigCommandView_KeyInput = Il2CppLast.UI.KeyInput.ConfigCommandView;
using ConfigCommandController_Touch = Il2CppLast.UI.Touch.ConfigCommandController;
using ConfigCommandController_KeyInput = Il2CppLast.UI.KeyInput.ConfigCommandController;
using ConfigActualDetailsControllerBase_Touch = Il2CppLast.UI.Touch.ConfigActualDetailsControllerBase;
using ConfigActualDetailsControllerBase_KeyInput = Il2CppLast.UI.KeyInput.ConfigActualDetailsControllerBase;
using ConfigKeysSettingController = Il2CppLast.UI.KeyInput.ConfigKeysSettingController;
using ConfigControllCommandController = Il2CppLast.UI.KeyInput.ConfigControllCommandController;
using MessageManager = Il2CppLast.Management.MessageManager;
using static FFIV_ScreenReader.Utils.TextUtils;

namespace FFIV_ScreenReader.Menus
{
    /// <summary>
    /// Core text discovery system that tries multiple strategies to find menu text.
    /// This is the heart of the mod's menu reading capability.
    /// </summary>
    public static class MenuTextDiscovery
    {
        /// <summary>
        /// Coroutine to wait one frame then read cursor position.
        /// This delay is critical because the game updates cursor position asynchronously.
        /// </summary>
        public static System.Collections.IEnumerator WaitAndReadCursor(GameCursor cursor, string direction, int count, bool isLoop)
        {
            yield return null; // Wait one frame

            try
            {
                // Safety checks to prevent crashes
                if (cursor == null || cursor.gameObject == null || cursor.transform == null)
                {
                    yield break;
                }

                // Try multiple strategies to find menu text
                string menuText = TryAllStrategies(cursor);

                // Check for config menu values
                if (menuText != null)
                {
                    string configValue = ConfigMenuReader.FindConfigValueText(cursor.transform, cursor.Index);
                    if (configValue != null)
                    {
                        // Combine option name and value
                        string fullText = $"{menuText}: {configValue}";
                        FFIV_ScreenReaderMod.SpeakText(fullText);
                    }
                    else
                    {
                        FFIV_ScreenReaderMod.SpeakText(menuText);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in delayed cursor read: {ex.Message}");
            }
        }

        /// <summary>
        /// Try all text discovery strategies in sequence until one succeeds.
        /// </summary>
        private static string TryAllStrategies(GameCursor cursor)
        {
            string menuText = null;

            // CRITICAL: Check if we're in equipment slot context - if so, skip ALL strategies
            // The EquipmentInfoWindowController.SelectContent patch handles this menu
            // BUT only skip if EquipmentMenuState is active (meaning we're in equipment slots, not command bar)
            if (EquipmentMenuState.IsActive && IsInEquipmentSlotContext(cursor.transform))
            {
                return null;
            }

            // Strategy 0: Battle enemy targeting (check first as it's very specific)
            menuText = TryReadBattleEnemyTarget(cursor);
            if (menuText != null) return menuText;

            // Note: Save/Load slot information is now handled by SaveLoadPatches.cs
            // which provides dedicated support for save/load confirmation popups

            // Strategy 1: Character selection (formation, status, equipment, etc.)
            // CharacterSelectionReader has its own MenuManager.IsOpen check to prevent
            // false positives during game initialization/scene preload
            menuText = CharacterSelectionReader.TryReadCharacterSelection(cursor.transform, cursor.Index);
            if (menuText != null) return menuText;

            // Strategy 3: Try to read directly from ConfigActualDetailsControllerBase (most reliable for config menus)
            menuText = TryReadFromConfigController(cursor);
            if (menuText != null) return menuText;

            // Strategy 4: Try to read directly from ConfigKeysSettingController (keyboard/gamepad settings)
            menuText = TryReadFromKeysSettingController(cursor);
            if (menuText != null) return menuText;

            // Strategy 5: Title-style approach (cursor moves in hierarchy)
            menuText = TryDirectTextSearch(cursor.transform);
            if (menuText != null) return menuText;

            // Strategy 5: Config-style menus (ConfigCommandView)
            menuText = TryConfigCommandView(cursor);
            if (menuText != null) return menuText;

            // Strategy 5: Battle menus with IconTextView (ability/item lists)
            menuText = TryIconTextView(cursor);
            if (menuText != null) return menuText;

            // Strategy 6: Keyboard/Gamepad settings
            menuText = KeyboardGamepadReader.TryReadSettings(cursor.transform, cursor.Index);
            if (menuText != null) return menuText;

            // Strategy 7: In-game config menu structure
            menuText = TryInGameConfigMenu(cursor);
            if (menuText != null) return menuText;

            // Strategy 8: Fallback with GetComponentInChildren
            menuText = TryFallbackTextSearch(cursor.transform);
            if (menuText != null) return menuText;

            return null;
        }

        /// <summary>
        /// Strategy 0: Try to read enemy name during battle targeting.
        /// </summary>
        private static string TryReadBattleEnemyTarget(GameCursor cursor)
        {
            // Battle enemy targeting is handled by dedicated patches
            return null;
        }

        /// <summary>
        /// Try to read menu item name directly from ConfigActualDetailsControllerBase.CommandList.
        /// </summary>
        private static string TryReadFromConfigController(GameCursor cursor)
        {
            try
            {
                if (IsCursorInDialog(cursor.transform))
                {
                    return null;
                }

                int cursorIndex = cursor.Index;

                // Try Touch version (title screen)
                var controllerTouch = UnityEngine.Object.FindObjectOfType<ConfigActualDetailsControllerBase_Touch>();
                if (controllerTouch != null && controllerTouch.CommandList != null)
                {
                    if (cursorIndex >= 0 && cursorIndex < controllerTouch.CommandList.Count)
                    {
                        var command = controllerTouch.CommandList[cursorIndex];
                        if (command != null && command.view != null && command.view.nameText != null)
                        {
                            string menuText = command.view.nameText.text?.Trim();
                            if (!string.IsNullOrEmpty(menuText))
                            {
                                return menuText;
                            }
                        }
                    }
                }

                // Try KeyInput version (in-game)
                var controllerKeyInput = UnityEngine.Object.FindObjectOfType<ConfigActualDetailsControllerBase_KeyInput>();
                if (controllerKeyInput != null && controllerKeyInput.CommandList != null)
                {
                    if (cursorIndex >= 0 && cursorIndex < controllerKeyInput.CommandList.Count)
                    {
                        var command = controllerKeyInput.CommandList[cursorIndex];
                        if (command != null && command.view != null && command.view.nameText != null)
                        {
                            string menuText = command.view.nameText.text?.Trim();
                            if (!string.IsNullOrEmpty(menuText))
                            {
                                return menuText;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error reading from config controller: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Check if the cursor is inside a dialog/popup context.
        /// </summary>
        private static bool IsCursorInDialog(Transform cursorTransform)
        {
            try
            {
                Transform current = cursorTransform;
                int depth = 0;
                while (current != null && depth < 15)
                {
                    string name = current.name.ToLower();
                    if (name.Contains("popup") || name.Contains("dialog") || name.Contains("prompt") ||
                        name.Contains("message_window") || name.Contains("yesno") || name.Contains("confirm"))
                    {
                        return true;
                    }
                    current = current.parent;
                    depth++;
                }

                return false;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error checking cursor dialog context: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Try to read menu item name from ConfigKeysSettingController.
        /// </summary>
        private static string TryReadFromKeysSettingController(GameCursor cursor)
        {
            try
            {
                if (IsCursorInDialog(cursor.transform))
                {
                    return null;
                }

                int cursorIndex = cursor.Index;

                var keysController = UnityEngine.Object.FindObjectOfType<ConfigKeysSettingController>();
                if (keysController == null)
                {
                    return null;
                }

                // Try keyboard list first
                if (keysController.keyboardCommandList != null &&
                    cursorIndex >= 0 && cursorIndex < keysController.keyboardCommandList.Count)
                {
                    var command = keysController.keyboardCommandList[cursorIndex];
                    if (command != null)
                    {
                        string text = ReadKeyCommandText(command);
                        if (text != null)
                        {
                            return text;
                        }
                    }
                }

                // Try gamepad list
                if (keysController.gamepadCommandList != null &&
                    cursorIndex >= 0 && cursorIndex < keysController.gamepadCommandList.Count)
                {
                    var command = keysController.gamepadCommandList[cursorIndex];
                    if (command != null)
                    {
                        string text = ReadKeyCommandText(command);
                        if (text != null)
                        {
                            return text;
                        }
                    }
                }

                // Try mouse list
                if (keysController.mouseCommandList != null &&
                    cursorIndex >= 0 && cursorIndex < keysController.mouseCommandList.Count)
                {
                    var command = keysController.mouseCommandList[cursorIndex];
                    if (command != null)
                    {
                        string text = ReadKeyCommandText(command);
                        if (text != null)
                        {
                            return text;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error reading from keys setting controller: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Read text from a ConfigControllCommandController.
        /// </summary>
        private static string ReadKeyCommandText(ConfigControllCommandController command)
        {
            try
            {
                var textParts = new System.Collections.Generic.List<string>();

                // First, get the localized action name from MessageId
                if (!string.IsNullOrWhiteSpace(command.MessageId))
                {
                    try
                    {
                        var messageManager = MessageManager.Instance;
                        if (messageManager != null)
                        {
                            string localizedText = messageManager.GetMessage(command.MessageId, false);
                            if (!string.IsNullOrWhiteSpace(localizedText))
                            {
                                textParts.Add(localizedText.Trim());
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // Silently ignore localization failures
                    }
                }

                // Then, read all text components from messageTexts (includes key bindings)
                if (command.messageTexts != null && command.messageTexts.Count > 0)
                {
                    foreach (var textComponent in command.messageTexts)
                    {
                        if (textComponent != null && !string.IsNullOrWhiteSpace(textComponent.text))
                        {
                            string text = textComponent.text.Trim();
                            if (!text.StartsWith("MENU_") && !textParts.Contains(text))
                            {
                                textParts.Add(text);
                            }
                        }
                    }
                }

                if (textParts.Count > 0)
                {
                    return string.Join(" ", textParts);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error reading key command text: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Strategy 2: Walk up parent hierarchy looking for direct text components.
        /// </summary>
        private static string TryDirectTextSearch(Transform cursorTransform)
        {
            Transform current = cursorTransform;
            int hierarchyDepth = 0;

            while (current != null && hierarchyDepth < 10)
            {
                try
                {
                    if (current.gameObject == null)
                    {
                        break;
                    }

                    var text = current.GetComponent<UnityEngine.UI.Text>();
                    if (text?.text != null && !string.IsNullOrEmpty(text.text.Trim()))
                    {
                        return text.text;
                    }

                    current = current.parent;
                    hierarchyDepth++;
                }
                catch (Exception)
                {
                    break;
                }
            }

            return null;
        }

        /// <summary>
        /// Strategy 3: Look for ConfigCommandView components.
        /// </summary>
        private static string TryConfigCommandView(GameCursor cursor)
        {
            try
            {
                Transform current = cursor.transform;
                int hierarchyDepth = 0;

                while (current != null && hierarchyDepth < 10)
                {
                    // Try Touch version (title screen config)
                    var configViewTouch = current.GetComponent<ConfigCommandView_Touch>();
                    if (configViewTouch != null && configViewTouch.nameText?.text != null)
                    {
                        return configViewTouch.nameText.text.Trim();
                    }

                    // Try KeyInput version (in-game config)
                    var configViewKeyInput = current.GetComponent<ConfigCommandView_KeyInput>();
                    if (configViewKeyInput != null && configViewKeyInput.nameText?.text != null)
                    {
                        return configViewKeyInput.nameText.text.Trim();
                    }

                    // Check parent too
                    if (current.parent != null)
                    {
                        configViewTouch = current.parent.GetComponent<ConfigCommandView_Touch>();
                        if (configViewTouch != null && configViewTouch.nameText?.text != null)
                        {
                            return configViewTouch.nameText.text.Trim();
                        }

                        configViewKeyInput = current.parent.GetComponent<ConfigCommandView_KeyInput>();
                        if (configViewKeyInput != null && configViewKeyInput.nameText?.text != null)
                        {
                            return configViewKeyInput.nameText.text.Trim();
                        }
                    }

                    // Look for config_root which indicates config-style menu
                    if (current.name == "config_root")
                    {
                        return TryConfigRootMenu(current, cursor.Index);
                    }

                    current = current.parent;
                    hierarchyDepth++;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in config menu check: {ex.Message}");
            }

            return null;
        }

        private static string TryConfigRootMenu(Transform configRoot, int cursorIndex)
        {
            try
            {
                var content = configRoot.GetComponentInChildren<Transform>()?.Find("MaskObject/Scroll View/Viewport/Content");
                if (content != null && cursorIndex >= 0 && cursorIndex < content.childCount)
                {
                    var configItem = content.GetChild(cursorIndex);
                    if (configItem != null && configItem.gameObject != null)
                    {
                        var rootChild = configItem.Find("root");
                        if (rootChild != null)
                        {
                            var rootConfigViewTouch = rootChild.GetComponent<ConfigCommandView_Touch>();
                            if (rootConfigViewTouch != null && rootConfigViewTouch.nameText?.text != null)
                            {
                                return rootConfigViewTouch.nameText.text.Trim();
                            }

                            var rootConfigViewKeyInput = rootChild.GetComponent<ConfigCommandView_KeyInput>();
                            if (rootConfigViewKeyInput != null && rootConfigViewKeyInput.nameText?.text != null)
                            {
                                return rootConfigViewKeyInput.nameText.text.Trim();
                            }
                        }

                        var itemConfigViewKeyInput = configItem.GetComponentInChildren<ConfigCommandView_KeyInput>();
                        if (itemConfigViewKeyInput != null && itemConfigViewKeyInput.nameText?.text != null)
                        {
                            return itemConfigViewKeyInput.nameText.text.Trim();
                        }

                        var configText = configItem.GetComponentInChildren<UnityEngine.UI.Text>();
                        if (configText?.text != null && !string.IsNullOrEmpty(configText.text.Trim()))
                        {
                            return configText.text;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in config root menu: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Strategy 4: Battle menus with IconTextView.
        /// </summary>
        private static string TryIconTextView(GameCursor cursor)
        {
            try
            {
                Transform current = cursor.transform;
                int hierarchyDepth = 0;

                while (current != null && hierarchyDepth < 10)
                {
                    if (current.gameObject == null)
                    {
                        break;
                    }

                    var iconTextView = current.GetComponent<IconTextView>();
                    if (iconTextView != null && iconTextView.nameText != null && iconTextView.nameText.text != null)
                    {
                        string menuText = iconTextView.nameText.text.Trim();
                        if (!string.IsNullOrEmpty(menuText))
                        {
                            return menuText;
                        }
                    }

                    Transform contentList = FindContentList(current);
                    if (contentList != null && cursor.Index >= 0 && cursor.Index < contentList.childCount)
                    {
                        Transform selectedChild = contentList.GetChild(cursor.Index);

                        if (selectedChild != null)
                        {
                            iconTextView = selectedChild.GetComponentInChildren<IconTextView>();
                            if (iconTextView != null && iconTextView.nameText != null && iconTextView.nameText.text != null)
                            {
                                string menuText = iconTextView.nameText.text.Trim();
                                if (!string.IsNullOrEmpty(menuText))
                                {
                                    return menuText;
                                }
                            }
                        }
                    }

                    current = current.parent;
                    hierarchyDepth++;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in IconTextView check: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Strategy 6: In-game config menu structure.
        /// </summary>
        private static string TryInGameConfigMenu(GameCursor cursor)
        {
            try
            {
                Transform current = cursor.transform;
                int hierarchyDepth = 0;

                while (current != null && hierarchyDepth < 10)
                {
                    if (current.name.Contains("command_list") || current.name.Contains("menu_list"))
                    {
                        Transform contentList = FindContentList(current);

                        if (contentList != null && cursor.Index >= 0 && cursor.Index < contentList.childCount)
                        {
                            var menuItem = contentList.GetChild(cursor.Index);

                            var commandViewKeyInput = menuItem.GetComponentInChildren<ConfigCommandView_KeyInput>();
                            if (commandViewKeyInput != null && commandViewKeyInput.nameText != null)
                            {
                                return commandViewKeyInput.nameText.text.Trim();
                            }

                            var foundText = FindFirstText(menuItem, t =>
                            {
                                if (string.IsNullOrEmpty(t.text?.Trim()))
                                    return false;
                                var textValue = t.text.Trim();
                                return !System.Text.RegularExpressions.Regex.IsMatch(textValue, @"^\d+%?$|^On$|^Off$|^Active$|^Wait$");
                            });

                            if (foundText != null)
                            {
                                return foundText.text.Trim();
                            }
                        }
                        break;
                    }
                    current = current.parent;
                    hierarchyDepth++;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in in-game config menu check: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Strategy 7: Final fallback with GetComponentInChildren.
        /// </summary>
        private static string TryFallbackTextSearch(Transform cursorTransform)
        {
            try
            {
                Transform current = cursorTransform;
                int hierarchyDepth = 0;

                while (current != null && hierarchyDepth < 10)
                {
                    if (current.gameObject == null)
                    {
                        break;
                    }

                    var text = current.GetComponentInChildren<UnityEngine.UI.Text>();
                    if (text?.text != null && !string.IsNullOrEmpty(text.text.Trim()))
                    {
                        return text.text;
                    }
                    current = current.parent;
                    hierarchyDepth++;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in fallback text search: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Find Content list under Scroll View.
        /// </summary>
        private static Transform FindContentList(Transform root)
        {
            var allTransforms = root.GetComponentsInChildren<Transform>();
            foreach (var t in allTransforms)
            {
                if (t.name == "Content" && t.parent != null &&
                    (t.parent.name == "Viewport" || t.parent.parent?.name == "Scroll View"))
                {
                    return t;
                }
            }
            return null;
        }

        /// <summary>
        /// Check if cursor is in equipment slot context (equipment info window).
        /// When true, the EquipmentInfoWindowController.SelectContent patch handles announcements.
        /// </summary>
        private static bool IsInEquipmentSlotContext(Transform cursorTransform)
        {
            try
            {
                Transform current = cursorTransform;
                int depth = 0;

                while (current != null && depth < 15)
                {
                    string name = current.name.ToLower();

                    // Check for equipment info window patterns
                    if (name.Contains("equip_info") || name.Contains("equipinfo") ||
                        name.Contains("equipment_info") || name.Contains("equipmentinfo"))
                    {
                        return true;
                    }

                    // Also check for the specific content pattern with part_text
                    if (current.name.Contains("info_content"))
                    {
                        // Check if this has equipment slot markers (part_text and last_text)
                        var texts = current.GetComponentsInChildren<UnityEngine.UI.Text>();
                        bool hasPartText = false;
                        bool hasLastText = false;

                        foreach (var text in texts)
                        {
                            if (text.name.Contains("part_text")) hasPartText = true;
                            if (text.name.Contains("last_text")) hasLastText = true;
                        }

                        if (hasPartText && hasLastText)
                        {
                            return true;
                        }
                    }

                    current = current.parent;
                    depth++;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error checking equipment slot context: {ex.Message}");
            }

            return false;
        }
    }
}
