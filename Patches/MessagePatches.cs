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
    // ============================================================
    // Per-Page Dialogue System
    // Announces dialogue text page-by-page as player advances,
    // formatted as "Speaker: Text" with speaker announced only on change.
    // ============================================================

    /// <summary>
    /// Tracks dialogue state for per-page announcements.
    /// Stores content from SetContent, announces via PlayingInit hook.
    /// </summary>
    public static class DialogueTracker
    {
        private static string[] storedMessages = null;
        private static string currentSpeaker = "";
        private static string lastAnnouncedSpeaker = "";
        private static int nextAnnouncementIndex = 0;  // Our own index, not the game's stale messageLineIndex

        /// <summary>
        /// Known invalid speaker names (locations, menu labels, etc.)
        /// </summary>
        private static readonly string[] InvalidSpeakers = new string[]
        {
            "Load", "Save", "New Game", "Continue", "Config", "Quit",
            "Yes", "No", "OK", "Cancel"
        };

        /// <summary>
        /// Store content from SetContent for per-page retrieval.
        /// </summary>
        public static void StoreContent(Il2CppSystem.Collections.Generic.List<Il2CppLast.Systems.Message.BaseContent> contentList)
        {
            if (contentList == null || contentList.Count == 0)
            {
                storedMessages = null;
                return;
            }

            // Extract text from each content item
            var messages = new System.Collections.Generic.List<string>();
            for (int i = 0; i < contentList.Count; i++)
            {
                var content = contentList[i];
                if (content != null && !string.IsNullOrWhiteSpace(content.ContentText))
                {
                    messages.Add(content.ContentText.Trim());
                }
            }

            storedMessages = messages.ToArray();
            nextAnnouncementIndex = 0; // Reset our index for new dialogue
        }

        /// <summary>
        /// Set the current speaker. Will be included in announcement if changed.
        /// </summary>
        public static void SetSpeaker(string speaker)
        {
            if (string.IsNullOrWhiteSpace(speaker))
                return;

            string cleanSpeaker = speaker.Trim();

            // Filter out invalid speakers (locations, menu labels)
            if (!IsValidSpeaker(cleanSpeaker))
                return;

            currentSpeaker = cleanSpeaker;
        }

        /// <summary>
        /// Check if a speaker name is valid (not a location or menu label).
        /// </summary>
        private static bool IsValidSpeaker(string speaker)
        {
            // Filter location names with separators
            if (speaker.Contains("–") || speaker.Contains("-"))
                return false;

            // Filter known invalid strings
            foreach (var invalid in InvalidSpeakers)
            {
                if (speaker.Equals(invalid, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Announce the current page. Called from PlayingInit.
        /// Uses our own nextAnnouncementIndex instead of game's stale messageLineIndex.
        /// </summary>
        public static void AnnounceForLineIndex(int gameLineIndex, string speakerFromInstance)
        {
            // Update speaker from instance if available
            if (!string.IsNullOrWhiteSpace(speakerFromInstance))
            {
                SetSpeaker(speakerFromInstance);
            }

            // Skip if no stored messages
            if (storedMessages == null || storedMessages.Length == 0)
                return;

            // Use our own index instead of game's potentially stale messageLineIndex
            int localIndex = nextAnnouncementIndex;

            // Skip if we've already announced all pages
            if (localIndex >= storedMessages.Length)
                return;

            string text = storedMessages[localIndex];
            if (string.IsNullOrWhiteSpace(text))
            {
                nextAnnouncementIndex++; // Still advance past empty page
                return;
            }

            // Build announcement with speaker if changed
            string announcement;
            if (!string.IsNullOrEmpty(currentSpeaker) && currentSpeaker != lastAnnouncedSpeaker)
            {
                announcement = $"{currentSpeaker}: {text}";
                lastAnnouncedSpeaker = currentSpeaker;
            }
            else
            {
                announcement = text;
            }

            FFIV_ScreenReaderMod.SpeakText(announcement, interrupt: false);
            nextAnnouncementIndex++;
        }

        /// <summary>
        /// Reset the tracker (e.g., when dialogue ends).
        /// </summary>
        public static void Reset()
        {
            storedMessages = null;
            currentSpeaker = "";
            lastAnnouncedSpeaker = "";
            nextAnnouncementIndex = 0;
        }

        /// <summary>
        /// Clear last announced speaker to force re-announcement on next dialogue.
        /// Call on scene transitions and after auto-scroll events to re-establish context.
        /// </summary>
        public static void ClearLastAnnouncedSpeaker()
        {
            lastAnnouncedSpeaker = "";
        }
    }

    /// <summary>
    /// Tracker for location/map name announcements.
    /// Uses content-based matching (no timers) to prevent duplicates.
    /// E.g., "Mysidia" is skipped if "Entering Mysidia" was just announced.
    /// </summary>
    public static class LocationMessageTracker
    {
        private static string lastFadeMessage = "";
        private static bool inMapTransition = false;

        /// <summary>
        /// Record a FadeMessage and mark that we're in a map transition.
        /// Called from FadeMessageManager_Play_Patch and CheckMapTransition before announcing.
        /// </summary>
        public static void SetLastFadeMessage(string message)
        {
            lastFadeMessage = message?.Trim() ?? "";
            inMapTransition = !string.IsNullOrEmpty(lastFadeMessage);
        }

        /// <summary>
        /// Check if a SystemMessage should be announced.
        /// Returns false if:
        /// - The message is contained in the last FadeMessage (duplicate)
        /// - No FadeMessage fired but this looks like a location (menu open case)
        /// </summary>
        public static bool ShouldAnnounceSystemMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            string trimmed = message.Trim();

            // If we're in a map transition (FadeMessage or CheckMapTransition fired)
            if (inMapTransition && !string.IsNullOrEmpty(lastFadeMessage))
            {
                // Skip if this message is contained in the FadeMessage
                // E.g., "Mysidia" contained in "Entering Mysidia"
                if (lastFadeMessage.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            else if (!inMapTransition)
            {
                // No FadeMessage fired - this might be menu open or other UI event
                // Skip if it looks like a short location name (1-3 words, no punctuation)
                // This prevents "Mysidia" from being announced when opening menu
                if (LooksLikeLocationName(trimmed))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Check if a string looks like a location name.
        /// Location names are typically 1-3 words without special punctuation.
        /// </summary>
        private static bool LooksLikeLocationName(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            // If it has sentence-like punctuation, it's probably a system message
            if (text.Contains('.') || text.Contains('!') || text.Contains('?'))
                return false;

            // Count words (simple split)
            string[] words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            // Location names are typically 1-4 words (e.g., "Mysidia", "Castle Baron", "Tower of Babil")
            // System messages tend to be longer
            return words.Length <= 4;
        }

        /// <summary>
        /// Reset state on scene transition.
        /// </summary>
        public static void Reset()
        {
            lastFadeMessage = "";
            inMapTransition = false;
        }
    }

    /// <summary>
    /// Patches for dialogue/message display in FF4.
    /// Ported from FF5 screen reader with FF4 adaptations.
    /// </summary>

    /// <summary>
    /// Patch MessageWindowView.SetSpeker for speaker names in dialogue.
    /// NOTE: Now only logs - DialogueTracker handles announcements via PlayingInit.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.Message.MessageWindowView), "SetSpeker")]
    public static class MessageWindowView_SetSpeker_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(string value)
        {
            // Speaker is now handled by DialogueTracker via PlayingInit
            // This patch is kept for logging only
        }
    }

    /// <summary>
    /// Patch MessageWindowView.SetMessage for dialogue text display.
    /// NOTE: Now disabled - DialogueTracker handles announcements via PlayingInit.
    /// This was previously used for typewriter effect tracking.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.Message.MessageWindowView), "SetMessage")]
    public static class MessageWindowView_SetMessage_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Il2CppLast.Message.MessageWindowView __instance, string message)
        {
            // Dialogue is now handled by DialogueTracker via PlayingInit
            // This patch is kept but disabled
        }
    }

    /// <summary>
    /// Patch Unity Text component's text setter as a fallback for dialogue.
    /// NOTE: Now disabled - DialogueTracker handles announcements via PlayingInit.
    /// This was previously used as a fallback for message text.
    /// </summary>
    [HarmonyPatch(typeof(UnityEngine.UI.Text), "set_text")]
    public static class UnityText_SetText_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(UnityEngine.UI.Text __instance, string value)
        {
            // Dialogue is now handled by DialogueTracker via PlayingInit
            // This patch is kept but disabled to prevent double announcements
        }
    }

    /// <summary>
    /// Patch MessageWindowManager.SetSpeker for speaker names from manager level.
    /// Stores speaker in DialogueTracker for announcement with dialogue text.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.Message.MessageWindowManager), "SetSpeker")]
    public static class MessageWindowManager_SetSpeker_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(string name)
        {
            try
            {
                DialogueTracker.SetSpeaker(name);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in MessageWindowManager.SetSpeker patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch MessageWindowManager.SetContent for dialogue content.
    /// Stores content in DialogueTracker for per-page announcement via PlayingInit.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.Message.MessageWindowManager), "SetContent")]
    public static class MessageWindowManager_SetContent_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Il2CppSystem.Collections.Generic.List<Il2CppLast.Systems.Message.BaseContent> contentList)
        {
            try
            {
                DialogueTracker.StoreContent(contentList);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in MessageWindowManager.SetContent patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch FadeMessageManager for location names, chapter titles, etc.
    /// Records message for content-based deduplication with SystemMessage.
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

                // Record this FadeMessage for deduplication with SystemMessage
                // E.g., "Entering Mysidia" recorded so "Mysidia" can be skipped
                LocationMessageTracker.SetLastFadeMessage(cleanMessage);

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
    /// Uses content-based deduplication to prevent duplicate location announcements.
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
                        // Check for duplicate location announcement
                        if (!LocationMessageTracker.ShouldAnnounceSystemMessage(message))
                        {
                            return;
                        }

                        FFIV_ScreenReaderMod.SpeakText(message, interrupt: true);
                    }
                }
                else
                {
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
                FFIV_ScreenReaderMod.SpeakText(choicesText, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in MessageChoiceWindowManager.Play patch: {ex.Message}");
            }
        }
    }

    // ============================================================
    // Per-Page Dialogue Announcement via PlayingInit
    // Fires once per page when text starts displaying.
    // ============================================================

    /// <summary>
    /// Patch MessageWindowManager.PlayingInit for per-page dialogue announcements.
    /// Fires when entering Playing state - once per page.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.Message.MessageWindowManager), "PlayingInit")]
    public static class MessageWindowManager_PlayingInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Il2CppLast.Message.MessageWindowManager __instance)
        {
            try
            {
                int lineIndex = __instance.messageLineIndex;
                string speaker = __instance.spekerValue;
                DialogueTracker.AnnounceForLineIndex(lineIndex, speaker);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in MessageWindowManager.PlayingInit patch: {ex.Message}");
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

    // ============================================================
    // LineFade Per-Line Announcement System
    // Announces each line of story text as it appears on screen,
    // using the game's internal timing via PlayInit hook.
    // ============================================================

    /// <summary>
    /// Tracks LineFade message state for per-line announcements.
    /// </summary>
    public static class LineFadeMessageTracker
    {
        private static string[] storedMessages = null;
        private static int currentLineIndex = 0;

        /// <summary>
        /// Store messages when SetData is called.
        /// </summary>
        public static void SetMessages(Il2CppSystem.Collections.Generic.List<string> messages)
        {
            if (messages == null || messages.Count == 0)
            {
                storedMessages = null;
                currentLineIndex = 0;
                return;
            }

            storedMessages = new string[messages.Count];
            for (int i = 0; i < messages.Count; i++)
            {
                storedMessages[i] = messages[i];
            }
            currentLineIndex = 0;
        }

        /// <summary>
        /// Get and announce the next line. Called when PlayInit fires.
        /// </summary>
        public static void AnnounceNextLine()
        {
            if (storedMessages == null || currentLineIndex >= storedMessages.Length)
            {
                return;
            }

            string line = storedMessages[currentLineIndex];
            if (!string.IsNullOrWhiteSpace(line))
            {
                FFIV_ScreenReaderMod.SpeakText(line.Trim(), interrupt: false);
            }

            currentLineIndex++;
        }

        /// <summary>
        /// Reset the tracker.
        /// </summary>
        public static void Reset()
        {
            storedMessages = null;
            currentLineIndex = 0;
        }
    }

    /// <summary>
    /// Patch LineFadeMessageWindowController.SetData to store messages for per-line announcement.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.UI.Message.LineFadeMessageWindowController), "SetData")]
    public static class LineFadeController_SetData_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Il2CppSystem.Collections.Generic.List<string> messages)
        {
            try
            {
                LineFadeMessageTracker.SetMessages(messages);

                // Clear speaker context so next regular dialogue re-announces the speaker
                // This re-establishes context after auto-scrolling text events
                DialogueTracker.ClearLastAnnouncedSpeaker();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in LineFadeController.SetData patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch LineFadeMessageWindowController.PlayInit to announce each line as it appears.
    /// PlayInit is called once per line by the game's internal state machine.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.UI.Message.LineFadeMessageWindowController), "PlayInit")]
    public static class LineFadeController_PlayInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                LineFadeMessageTracker.AnnounceNextLine();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in LineFadeController.PlayInit patch: {ex.Message}");
            }
        }
    }
}
