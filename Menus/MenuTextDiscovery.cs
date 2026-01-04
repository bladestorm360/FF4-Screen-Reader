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
                if (cursor == null)
                {
                    MelonLogger.Msg("Cursor is null, skipping");
                    yield break;
                }

                if (cursor.gameObject == null)
                {
                    MelonLogger.Msg("Cursor GameObject is null, skipping");
                    yield break;
                }

                if (cursor.transform == null)
                {
                    MelonLogger.Msg("Cursor transform is null, skipping");
                    yield break;
                }

                // Get scene info for debugging
                var sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

                MelonLogger.Msg($"=== {direction} called (delayed) ===");
                MelonLogger.Msg($"Scene: {sceneName}");
                MelonLogger.Msg($"Cursor Index: {cursor.Index}");
                MelonLogger.Msg($"Cursor GameObject: {cursor.gameObject?.name ?? "null"}");
                MelonLogger.Msg($"Count: {count}, IsLoop: {isLoop}");

                // Try multiple strategies to find menu text
                string menuText = TryAllStrategies(cursor);

                // Check for config menu values
                if (menuText != null)
                {
                    string configValue = ConfigMenuReader.FindConfigValueText(cursor.transform, cursor.Index);
                    if (configValue != null)
                    {
                        MelonLogger.Msg($"Found config value: '{configValue}'");
                        // Combine option name and value
                        string fullText = $"{menuText}: {configValue}";
                        FFIV_ScreenReaderMod.SpeakText(fullText);
                    }
                    else
                    {
                        FFIV_ScreenReaderMod.SpeakText(menuText);
                    }
                }
                else
                {
                    MelonLogger.Msg("No menu text found in hierarchy");
                }

                MelonLogger.Msg("========================");
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
            if (IsInEquipmentSlotContext(cursor.transform))
            {
                MelonLogger.Msg("In equipment slot context - skipping all fallback strategies");
                return null;
            }

            // Strategy 0: Battle enemy targeting (check first as it's very specific)
            menuText = TryReadBattleEnemyTarget(cursor);
            if (menuText != null) return menuText;

            // Strategy 1: Save/Load slot information
            menuText = SaveSlotReader.TryReadSaveSlot(cursor.transform, cursor.Index);
            if (menuText != null) return menuText;

            // Strategy 2: Character selection (formation, status, equipment, etc.)
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
            try
            {
                // Check if we're in battle by looking for BattleEnemyEntity
                var enemyEntities = UnityEngine.Object.FindObjectsOfType<Il2CppLast.Battle.BattleEnemyEntity>();
                if (enemyEntities == null || enemyEntities.Length == 0)
                {
                    return null;
                }

                MelonLogger.Msg($"[Battle Enemy] In battle with {enemyEntities.Length} enemies, cursor at index {cursor.Index}");

                // Log the cursor hierarchy to see where enemy names might be
                Transform current = cursor.transform;
                for (int depth = 0; depth < 10 && current != null; depth++)
                {
                    var texts = current.GetComponentsInChildren<UnityEngine.UI.Text>();
                    if (texts != null && texts.Length > 0)
                    {
                        foreach (var text in texts)
                        {
                            if (text != null && !string.IsNullOrWhiteSpace(text.text))
                            {
                                MelonLogger.Msg($"[Battle Enemy] Depth {depth}, Text on '{text.gameObject.name}': '{text.text}'");
                            }
                        }
                    }
                    current = current.parent;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error reading battle enemy target: {ex.Message}");
            }

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
                    MelonLogger.Msg("Cursor is in dialog, skipping config controller");
                    return null;
                }

                int cursorIndex = cursor.Index;

                // Try Touch version (title screen)
                var controllerTouch = UnityEngine.Object.FindObjectOfType<ConfigActualDetailsControllerBase_Touch>();
                if (controllerTouch != null && controllerTouch.CommandList != null)
                {
                    MelonLogger.Msg($"Found Touch ConfigActualDetailsControllerBase with {controllerTouch.CommandList.Count} commands");

                    if (cursorIndex >= 0 && cursorIndex < controllerTouch.CommandList.Count)
                    {
                        var command = controllerTouch.CommandList[cursorIndex];
                        if (command != null && command.view != null && command.view.nameText != null)
                        {
                            string menuText = command.view.nameText.text?.Trim();
                            if (!string.IsNullOrEmpty(menuText))
                            {
                                MelonLogger.Msg($"Read name from Touch controller at index {cursorIndex}: '{menuText}'");
                                return menuText;
                            }
                        }
                    }
                }

                // Try KeyInput version (in-game)
                var controllerKeyInput = UnityEngine.Object.FindObjectOfType<ConfigActualDetailsControllerBase_KeyInput>();
                if (controllerKeyInput != null && controllerKeyInput.CommandList != null)
                {
                    MelonLogger.Msg($"Found KeyInput ConfigActualDetailsControllerBase with {controllerKeyInput.CommandList.Count} commands");

                    if (cursorIndex >= 0 && cursorIndex < controllerKeyInput.CommandList.Count)
                    {
                        var command = controllerKeyInput.CommandList[cursorIndex];
                        if (command != null && command.view != null && command.view.nameText != null)
                        {
                            string menuText = command.view.nameText.text?.Trim();
                            if (!string.IsNullOrEmpty(menuText))
                            {
                                MelonLogger.Msg($"Read name from KeyInput controller at index {cursorIndex}: '{menuText}'");
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
                        MelonLogger.Msg($"Cursor is inside dialog: {current.name}");
                        return true;
                    }
                    current = current.parent;
                    depth++;
                }

                return false;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error checking cursor dialog context: {ex.Message}");
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
                    MelonLogger.Msg("Cursor is in dialog, skipping keys setting controller");
                    return null;
                }

                int cursorIndex = cursor.Index;

                var keysController = UnityEngine.Object.FindObjectOfType<ConfigKeysSettingController>();
                if (keysController == null)
                {
                    return null;
                }

                MelonLogger.Msg("Found ConfigKeysSettingController");

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
                            MelonLogger.Msg($"Read from keyboard command list at index {cursorIndex}: '{text}'");
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
                            MelonLogger.Msg($"Read from gamepad command list at index {cursorIndex}: '{text}'");
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
                            MelonLogger.Msg($"Read from mouse command list at index {cursorIndex}: '{text}'");
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
                                MelonLogger.Msg($"Localized MessageId '{command.MessageId}' to '{localizedText}'");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"Could not localize MessageId '{command.MessageId}': {ex.Message}");
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
                        MelonLogger.Msg("Current gameObject is null, breaking hierarchy walk");
                        break;
                    }

                    var text = current.GetComponent<UnityEngine.UI.Text>();
                    if (text?.text != null && !string.IsNullOrEmpty(text.text.Trim()))
                    {
                        string menuText = text.text;
                        MelonLogger.Msg($"Found menu text: '{menuText}' from {current.name} (direct)");
                        return menuText;
                    }

                    current = current.parent;
                    hierarchyDepth++;
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"Error walking hierarchy at depth {hierarchyDepth}: {ex.Message}");
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
                        string menuText = configViewTouch.nameText.text.Trim();
                        MelonLogger.Msg($"Found menu text: '{menuText}' from Touch ConfigCommandView.nameText");
                        return menuText;
                    }

                    // Try KeyInput version (in-game config)
                    var configViewKeyInput = current.GetComponent<ConfigCommandView_KeyInput>();
                    if (configViewKeyInput != null && configViewKeyInput.nameText?.text != null)
                    {
                        string menuText = configViewKeyInput.nameText.text.Trim();
                        MelonLogger.Msg($"Found menu text: '{menuText}' from KeyInput ConfigCommandView.nameText");
                        return menuText;
                    }

                    // Check parent too
                    if (current.parent != null)
                    {
                        configViewTouch = current.parent.GetComponent<ConfigCommandView_Touch>();
                        if (configViewTouch != null && configViewTouch.nameText?.text != null)
                        {
                            string menuText = configViewTouch.nameText.text.Trim();
                            MelonLogger.Msg($"Found menu text: '{menuText}' from parent Touch ConfigCommandView.nameText");
                            return menuText;
                        }

                        configViewKeyInput = current.parent.GetComponent<ConfigCommandView_KeyInput>();
                        if (configViewKeyInput != null && configViewKeyInput.nameText?.text != null)
                        {
                            string menuText = configViewKeyInput.nameText.text.Trim();
                            MelonLogger.Msg($"Found menu text: '{menuText}' from parent KeyInput ConfigCommandView.nameText");
                            return menuText;
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
                    MelonLogger.Msg($"In-game config: Found content with {content.childCount} items, cursor at {cursorIndex}");

                    var configItem = content.GetChild(cursorIndex);
                    if (configItem != null && configItem.gameObject != null)
                    {
                        MelonLogger.Msg($"Config item name: {configItem.name}");

                        var rootChild = configItem.Find("root");
                        if (rootChild != null)
                        {
                            var rootConfigViewTouch = rootChild.GetComponent<ConfigCommandView_Touch>();
                            if (rootConfigViewTouch != null && rootConfigViewTouch.nameText?.text != null)
                            {
                                string menuText = rootConfigViewTouch.nameText.text.Trim();
                                MelonLogger.Msg($"Found menu text from root Touch ConfigCommandView: '{menuText}'");
                                return menuText;
                            }

                            var rootConfigViewKeyInput = rootChild.GetComponent<ConfigCommandView_KeyInput>();
                            if (rootConfigViewKeyInput != null && rootConfigViewKeyInput.nameText?.text != null)
                            {
                                string menuText = rootConfigViewKeyInput.nameText.text.Trim();
                                MelonLogger.Msg($"Found menu text from root KeyInput ConfigCommandView: '{menuText}'");
                                return menuText;
                            }
                        }

                        var itemConfigViewKeyInput = configItem.GetComponentInChildren<ConfigCommandView_KeyInput>();
                        if (itemConfigViewKeyInput != null && itemConfigViewKeyInput.nameText?.text != null)
                        {
                            string menuText = itemConfigViewKeyInput.nameText.text.Trim();
                            MelonLogger.Msg($"Found menu text: '{menuText}' from config item KeyInput ConfigCommandView");
                            return menuText;
                        }

                        var configText = configItem.GetComponentInChildren<UnityEngine.UI.Text>();
                        if (configText?.text != null && !string.IsNullOrEmpty(configText.text.Trim()))
                        {
                            string menuText = configText.text;
                            MelonLogger.Msg($"Found menu text (fallback): '{menuText}'");
                            return menuText;
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
                        MelonLogger.Msg("Current gameObject is null in IconTextView check");
                        break;
                    }

                    var iconTextView = current.GetComponent<IconTextView>();
                    if (iconTextView != null && iconTextView.nameText != null && iconTextView.nameText.text != null)
                    {
                        string menuText = iconTextView.nameText.text.Trim();
                        if (!string.IsNullOrEmpty(menuText))
                        {
                            MelonLogger.Msg($"Found menu text: '{menuText}' from IconTextView.nameText");
                            return menuText;
                        }
                    }

                    Transform contentList = FindContentList(current);
                    if (contentList != null && cursor.Index >= 0 && cursor.Index < contentList.childCount)
                    {
                        MelonLogger.Msg($"Found Content list with {contentList.childCount} children, cursor at index {cursor.Index}");
                        Transform selectedChild = contentList.GetChild(cursor.Index);

                        if (selectedChild != null)
                        {
                            iconTextView = selectedChild.GetComponentInChildren<IconTextView>();
                            if (iconTextView != null && iconTextView.nameText != null && iconTextView.nameText.text != null)
                            {
                                string menuText = iconTextView.nameText.text.Trim();
                                if (!string.IsNullOrEmpty(menuText))
                                {
                                    MelonLogger.Msg($"Found menu text: '{menuText}' from Content[{cursor.Index}] IconTextView.nameText");
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
                        MelonLogger.Msg($"Found in-game list structure: {current.name}");

                        Transform contentList = FindContentList(current);

                        if (contentList != null && cursor.Index >= 0 && cursor.Index < contentList.childCount)
                        {
                            MelonLogger.Msg($"Found content list with {contentList.childCount} items, cursor at {cursor.Index}");

                            var menuItem = contentList.GetChild(cursor.Index);
                            MelonLogger.Msg($"Menu item at index {cursor.Index}: {menuItem.name}");

                            var commandViewKeyInput = menuItem.GetComponentInChildren<ConfigCommandView_KeyInput>();
                            if (commandViewKeyInput != null && commandViewKeyInput.nameText != null)
                            {
                                string menuText = commandViewKeyInput.nameText.text.Trim();
                                MelonLogger.Msg($"Got text from KeyInput ConfigCommandView.nameText: '{menuText}'");
                                return menuText;
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
                                string menuText = foundText.text.Trim();
                                MelonLogger.Msg($"Got text from Text component: '{menuText}'");
                                return menuText;
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
                        MelonLogger.Msg("Current gameObject is null in fallback check");
                        break;
                    }

                    var text = current.GetComponentInChildren<UnityEngine.UI.Text>();
                    if (text?.text != null && !string.IsNullOrEmpty(text.text.Trim()))
                    {
                        string menuText = text.text;
                        MelonLogger.Msg($"Found menu text: '{menuText}' from {current.name} (fallback)");
                        return menuText;
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
