using System;
using HarmonyLib;
using MelonLoader;
using Il2CppLast.Message;
using Il2CppLast.Management;
using Il2CppLast.Map;
using FFIV_ScreenReader.Core;
using UnityEngine;

namespace FFIV_ScreenReader.Patches
{
    /// <summary>
    /// Tracker for location/map name announcements.
    /// Prevents duplicate announcements when multiple sources fire (e.g., FadeMessageManager + other patches).
    /// </summary>
    public static class LocationMessageTracker
    {
        private static string lastLocationMessage = "";
        private static float lastLocationMessageTime = 0f;
        private const float DEDUP_WINDOW_SECONDS = 1.5f;

        /// <summary>
        /// Check if a location message should be announced.
        /// Returns true if the message is new or enough time has passed.
        /// </summary>
        public static bool ShouldAnnounce(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            float currentTime = UnityEngine.Time.time;

            // Allow if message is different or enough time has passed
            if (message != lastLocationMessage || (currentTime - lastLocationMessageTime) >= DEDUP_WINDOW_SECONDS)
            {
                lastLocationMessage = message;
                lastLocationMessageTime = currentTime;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Reset the tracker.
        /// </summary>
        public static void Reset()
        {
            lastLocationMessage = "";
            lastLocationMessageTime = 0f;
        }
    }

    /// <summary>
    /// Patches for dialogue/message display in FF4.
    /// Ported from FF5 screen reader with FF4 adaptations.
    /// </summary>

    /// <summary>
    /// Patch MessageWindowView.SetSpeker for speaker names in dialogue.
    /// Filters out location names that get incorrectly passed as speaker names.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.Message.MessageWindowView), "SetSpeker")]
    public static class MessageWindowView_SetSpeker_Patch
    {
        private static string lastSpeaker = "";

        [HarmonyPostfix]
        public static void Postfix(string value)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    lastSpeaker = "";
                    return;
                }

                string cleanSpeaker = value.Trim();

                // Filter out location names that get passed as speaker names
                // Location names typically contain "–" (en-dash) separator like "Castle Baron – 1F"
                if (cleanSpeaker.Contains("–") || cleanSpeaker.Contains("-"))
                {
                    MelonLogger.Msg($"[Speaker - Filtered location] {cleanSpeaker}");
                    return;
                }

                if (cleanSpeaker == lastSpeaker)
                {
                    return;
                }

                lastSpeaker = cleanSpeaker;
                MelonLogger.Msg($"[Speaker] {cleanSpeaker}");
                FFIV_ScreenReaderMod.SpeakText(cleanSpeaker, interrupt: false);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in MessageWindowView.SetSpeker patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch MessageWindowView.SetMessage for dialogue text display.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.Message.MessageWindowView), "SetMessage")]
    public static class MessageWindowView_SetMessage_Patch
    {
        private static string lastMessage = "";

        [HarmonyPostfix]
        public static void Postfix(Il2CppLast.Message.MessageWindowView __instance, string message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(message))
                {
                    if (!string.IsNullOrWhiteSpace(lastMessage))
                    {
                        lastMessage = "";
                    }
                    return;
                }

                string cleanMessage = message.Trim();

                if (cleanMessage == lastMessage)
                {
                    return;
                }

                if (!string.IsNullOrWhiteSpace(lastMessage) && cleanMessage.StartsWith(lastMessage))
                {
                    string newText = cleanMessage.Substring(lastMessage.Length).Trim();
                    if (!string.IsNullOrWhiteSpace(newText))
                    {
                        MelonLogger.Msg($"[Message - New] {newText}");
                        FFIV_ScreenReaderMod.SpeakText(newText, interrupt: false);
                    }
                }
                else
                {
                    MelonLogger.Msg($"[Message - Full] {cleanMessage}");
                    FFIV_ScreenReaderMod.SpeakText(cleanMessage, interrupt: false);
                }

                lastMessage = cleanMessage;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in MessageWindowView.SetMessage patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch Unity Text component's text setter as a fallback for dialogue.
    /// Only captures text from Message-related components.
    /// </summary>
    [HarmonyPatch(typeof(UnityEngine.UI.Text), "set_text")]
    public static class UnityText_SetText_Patch
    {
        private static string lastTextValue = "";
        private static float lastTextTime = 0f;

        [HarmonyPostfix]
        public static void Postfix(UnityEngine.UI.Text __instance, string value)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return;
                }

                string cleanText = value.Trim();

                // Debounce within 0.1 seconds
                float currentTime = UnityEngine.Time.time;
                if (cleanText == lastTextValue && (currentTime - lastTextTime) < 0.1f)
                {
                    return;
                }

                bool isMessageText = false;
                string gameObjectName = "";
                try
                {
                    if (__instance != null && __instance.gameObject != null)
                    {
                        gameObjectName = __instance.gameObject.name;

                        // INCLUDE: Message/Dialogue text
                        if (gameObjectName.Contains("Message") && !gameObjectName.Contains("System"))
                        {
                            isMessageText = true;
                        }

                        // EXCLUDE: Menu, UI, Status, and other non-dialogue text
                        if (gameObjectName.Contains("Menu") ||
                            gameObjectName.Contains("Item") ||
                            gameObjectName.Contains("Ability") ||
                            gameObjectName.Contains("Status") ||
                            gameObjectName.Contains("HP") ||
                            gameObjectName.Contains("MP") ||
                            gameObjectName.Contains("Level") ||
                            gameObjectName.Contains("Button") ||
                            gameObjectName.Contains("Icon") ||
                            gameObjectName.Contains("Gauge") ||
                            gameObjectName.Contains("Number") ||
                            gameObjectName.Contains("Command"))
                        {
                            isMessageText = false;
                        }

                        // Exclude battle UI components to prevent overwriting target announcements
                        if (__instance.GetComponentInParent<Il2CppLast.UI.KeyInput.BattleCommandSelectController>() != null ||
                            __instance.GetComponentInParent<Il2CppLast.UI.KeyInput.BattleItemInfomationController>() != null ||
                            __instance.GetComponentInParent<Il2CppLast.UI.KeyInput.ItemUseController>() != null)
                        {
                            return;
                        }
                    }
                }
                catch { }

                if (isMessageText)
                {
                    MelonLogger.Msg($"[UnityText:{gameObjectName}] {cleanText}");
                    FFIV_ScreenReaderMod.SpeakText(cleanText, interrupt: false);
                    lastTextValue = cleanText;
                    lastTextTime = currentTime;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in UnityText.set_text patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch MessageWindowManager.SetSpeker for speaker names from manager level.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.Message.MessageWindowManager), "SetSpeker")]
    public static class MessageWindowManager_SetSpeker_Patch
    {
        private static string lastSpeaker = "";

        [HarmonyPostfix]
        public static void Postfix(string name)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    lastSpeaker = "";
                    return;
                }

                string cleanSpeaker = name.Trim();
                if (cleanSpeaker == lastSpeaker)
                {
                    return;
                }

                lastSpeaker = cleanSpeaker;
                MelonLogger.Msg($"[MessageWindowManager.Speaker] {cleanSpeaker}");
                FFIV_ScreenReaderMod.SpeakText(cleanSpeaker, interrupt: false);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in MessageWindowManager.SetSpeker patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch MessageWindowManager.SetContent for dialogue content.
    /// This is the KEY patch for reading dialogue text in FF4/FF5.
    /// BaseContent is in Last.Systems.Message namespace and has ContentText property.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.Message.MessageWindowManager), "SetContent")]
    public static class MessageWindowManager_SetContent_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Il2CppSystem.Collections.Generic.List<Il2CppLast.Systems.Message.BaseContent> contentList)
        {
            try
            {
                if (contentList == null || contentList.Count == 0)
                {
                    return;
                }

                MelonLogger.Msg($"[MessageWindowManager.Content] Content list with {contentList.Count} items set");

                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < contentList.Count; i++)
                {
                    var content = contentList[i];
                    if (content != null && !string.IsNullOrWhiteSpace(content.ContentText))
                    {
                        sb.AppendLine(content.ContentText.Trim());
                    }
                }

                string fullText = sb.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(fullText))
                {
                    MelonLogger.Msg($"[MessageWindowManager.Content - Text] {fullText}");
                    FFIV_ScreenReaderMod.SpeakText(fullText, interrupt: false);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in MessageWindowManager.SetContent patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch FadeMessageManager for location names, chapter titles, etc.
    /// Uses time-based deduplication to prevent "Castle Baron 1F" then "Castle Baron".
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.Message.FadeMessageManager), "Play")]
    public static class FadeMessageManager_Play_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(string message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(message))
                {
                    return;
                }

                string cleanMessage = message.Trim();

                // Use location tracker for deduplication
                // This prevents duplicate map name announcements from multiple sources
                if (!LocationMessageTracker.ShouldAnnounce(cleanMessage))
                {
                    MelonLogger.Msg($"[FadeMessage - Deduplicated] {cleanMessage}");
                    return;
                }

                MelonLogger.Msg($"[FadeMessage] {cleanMessage}");
                FFIV_ScreenReaderMod.SpeakText(cleanMessage, interrupt: false);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in FadeMessageManager.Play patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch LineFadeMessageManager for scrolling credits, intro text, etc.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.Message.LineFadeMessageManager), "Play")]
    public static class LineFadeMessageManager_Play_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Il2CppSystem.Collections.Generic.List<string> messages)
        {
            try
            {
                if (messages == null || messages.Count == 0)
                {
                    return;
                }

                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < messages.Count; i++)
                {
                    string msg = messages[i];
                    if (!string.IsNullOrWhiteSpace(msg))
                    {
                        sb.AppendLine(msg.Trim());
                    }
                }

                string fullText = sb.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(fullText))
                {
                    MelonLogger.Msg($"[LineFadeMessage] {fullText}");
                    FFIV_ScreenReaderMod.SpeakText(fullText, interrupt: false);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in LineFadeMessageManager.Play patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch SystemMessageWindowManager for system messages.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.Management.SystemMessageWindowManager), "SetMessage")]
    public static class SystemMessageWindowManager_SetMessage_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(string messageId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(messageId))
                {
                    return;
                }

                var messageManager = Il2CppLast.Management.MessageManager.Instance;
                if (messageManager != null)
                {
                    string message = messageManager.GetMessage(messageId);
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        MelonLogger.Msg($"[SystemMessage] {message}");
                        FFIV_ScreenReaderMod.SpeakText(message, interrupt: true);
                    }
                }
                else
                {
                    MelonLogger.Msg($"[SystemMessage ID] {messageId}");
                    FFIV_ScreenReaderMod.SpeakText(messageId, interrupt: true);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in SystemMessageWindowManager.SetMessage patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch MessageChoiceWindowManager for choice menus.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.Management.MessageChoiceWindowManager), "Play", new Type[] { typeof(string[]) })]
    public static class MessageChoiceWindowManager_Play_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(string[] values)
        {
            try
            {
                if (values == null || values.Length == 0)
                {
                    return;
                }

                var sb = new System.Text.StringBuilder("Choices: ");
                for (int i = 0; i < values.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(values[i]))
                    {
                        sb.Append(values[i].Trim());
                        if (i < values.Length - 1)
                        {
                            sb.Append(", ");
                        }
                    }
                }

                string choicesText = sb.ToString();
                MelonLogger.Msg($"[MessageChoice] {choicesText}");
                FFIV_ScreenReaderMod.SpeakText(choicesText, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in MessageChoiceWindowManager.Play patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch EventProcedure.EventTalk for event-based dialogue.
    /// </summary>
    [HarmonyPatch(typeof(EventProcedure), "EventTalk")]
    public static class EventProcedure_EventTalk_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(string messageId, Vector3 worldPos, int changeCharacterStatusId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(messageId))
                {
                    return;
                }

                var messageManager = Il2CppLast.Management.MessageManager.Instance;
                if (messageManager != null)
                {
                    string message = messageManager.GetMessage(messageId);
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        MelonLogger.Msg($"[EventTalk] {message}");
                        FFIV_ScreenReaderMod.SpeakText(message, interrupt: false);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in EventProcedure.EventTalk patch: {ex.Message}");
            }
        }
    }
}
