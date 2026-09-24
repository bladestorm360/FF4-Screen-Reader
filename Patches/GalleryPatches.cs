using System;
using System.Collections;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFIV_ScreenReader.Core;
using FFIV_ScreenReader.Menus;
using FFIV_ScreenReader.Utils;
using static FFIV_ScreenReader.Utils.ModTextTranslator;
using Il2CppLast.Management;
using Il2CppLast.UI.KeyInput;

namespace FFIV_ScreenReader.Patches
{
    /// <summary>
    /// Tracks gallery scene state.
    /// Mirrors MusicPlayerStateTracker pattern.
    /// </summary>
    public static class GalleryStateTracker
    {
        public static bool IsInGallery { get; set; } = false;
        public static bool SuppressContentChange { get; set; } = false;
        public static IntPtr CachedFocusedPtr { get; set; } = IntPtr.Zero;
        public static int PreviousState { get; set; } = 0;
        // Entry read waiting for the list's first focus (SetFocusContent schedules it).
        public static bool EntryReadPending { get; set; } = false;
        private static bool entryReadScheduled = false;

        public static void ClearState()
        {
            IsInGallery = false;
            SuppressContentChange = false;
            CachedFocusedPtr = IntPtr.Zero;
            PreviousState = 0;
            EntryReadPending = false;
            entryReadScheduled = false;
            MenuStateRegistry.Reset(MenuStateRegistry.GALLERY);
            AnnouncementDeduplicator.Reset(AnnouncementContexts.GALLERY_LIST_ENTRY);
            AnnouncementDeduplicator.Reset(AnnouncementContexts.TITLE_MENU_COMMAND);
        }

        /// <summary>
        /// Speaks the cached focused entry after "Gallery" and ends the entry suppression.
        /// Returns false when nothing is cached yet.
        /// </summary>
        public static bool TryReadCachedEntry()
        {
            IntPtr focusedPtr = CachedFocusedPtr;
            if (focusedPtr == IntPtr.Zero ||
                !GalleryReader.ReadContentFromPointer(focusedPtr, out int number, out string name))
                return false;

            string entry = GalleryReader.ReadListEntry(number, name);
            if (!string.IsNullOrEmpty(entry))
                FFIV_ScreenReaderMod.SpeakText(entry, false);
            EntryReadPending = false;
            SuppressContentChange = false;
            return true;
        }

        /// <summary>
        /// The list's first focus arrived after "Gallery" was spoken: read it one frame later, so the
        /// last focus set in this frame (list construction) is the one read.
        /// </summary>
        public static void ScheduleEntryRead()
        {
            if (entryReadScheduled) return;
            entryReadScheduled = true;
            CoroutineManager.StartManaged(ReadEntryNextFrame());
        }

        private static IEnumerator ReadEntryNextFrame()
        {
            yield return null;
            entryReadScheduled = false;
            try
            {
                if (EntryReadPending && IsInGallery)
                    TryReadCachedEntry();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Gallery] Error announcing entry item: {ex.Message}");
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Patch 1: State transitions — SubSceneManagerExtraGallery.ChangeState
    // ─────────────────────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(SubSceneManagerExtraGallery), nameof(SubSceneManagerExtraGallery.ChangeState))]
    public static class SubSceneManagerExtraGallery_ChangeState_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(int state)
        {
            try
            {
                switch (state)
                {
                    case 1: // View
                        if (GalleryStateTracker.PreviousState == 0) // First entry from Init
                        {
                            GalleryStateTracker.IsInGallery = true;
                            GalleryStateTracker.SuppressContentChange = true;
                            MenuStateRegistry.SetActiveExclusive(MenuStateRegistry.GALLERY);
                            CoroutineManager.StartManaged(AnnounceGalleryEntry());
                        }
                        else if (GalleryStateTracker.PreviousState == 2) // Returning from Details
                        {
                            AnnouncementDeduplicator.Reset(AnnouncementContexts.GALLERY_LIST_ENTRY);
                        }
                        GalleryStateTracker.PreviousState = 1;
                        break;

                    case 2: // Details — image opened
                        FFIV_ScreenReaderMod.SpeakText(T("Image open"), true);
                        GalleryStateTracker.PreviousState = 2;
                        break;

                    case 3: // GotoTitle — leaving gallery
                        GalleryStateTracker.ClearState();
                        break;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Gallery] Error in ChangeState patch: {ex.Message}");
            }
        }

        /// <summary>
        /// "Gallery", then the focused entry. The entry is read one frame later if the list's focus
        /// is already cached (the same moment the old loop first checked); otherwise the list's
        /// first SetFocusContent reads it. Event-driven since 2026-09-24 (was a 2 s per-frame poll
        /// of the cache).
        /// </summary>
        private static IEnumerator AnnounceGalleryEntry()
        {
            yield return null;
            FFIV_ScreenReaderMod.SpeakText(T("Gallery"), true);
            yield return null;

            try
            {
                if (!GalleryStateTracker.IsInGallery) yield break;
                if (!GalleryStateTracker.TryReadCachedEntry())
                    GalleryStateTracker.EntryReadPending = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Gallery] Error announcing entry item: {ex.Message}");
                GalleryStateTracker.SuppressContentChange = false;
            }
        }

    }

    // ─────────────────────────────────────────────────────────────────────────
    // Patch 2: List navigation — GalleryTopListController.SetFocusContent
    // ─────────────────────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(GalleryTopListController), "SetFocusContent")]
    public static class GalleryTopListController_SetFocusContent_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(GalleryTopListController __instance, bool isFocus)
        {
            try
            {
                if (!isFocus) return;
                if (!GalleryStateTracker.IsInGallery) return;

                IntPtr ptr;
                try
                {
                    if (__instance == null) return;
                    ptr = __instance.Pointer;
                }
                catch { return; }
                if (ptr == IntPtr.Zero) return;

                if (GalleryStateTracker.SuppressContentChange)
                {
                    GalleryStateTracker.CachedFocusedPtr = ptr;
                    if (GalleryStateTracker.EntryReadPending)
                        GalleryStateTracker.ScheduleEntryRead();
                    return;
                }

                if (!GalleryReader.ReadContentFromPointer(ptr, out int number, out string name))
                    return;

                string entry = GalleryReader.ReadListEntry(number, name);
                if (!string.IsNullOrEmpty(entry))
                {
                    AnnouncementDeduplicator.AnnounceIfNew(
                        AnnouncementContexts.GALLERY_LIST_ENTRY, entry);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Gallery] Error in SetFocusContent patch: {ex.Message}");
            }
        }
    }

}
