using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MelonLoader;
using FFIV_ScreenReader.Utils;
using static FFIV_ScreenReader.Utils.ModTextTranslator;

namespace FFIV_ScreenReader.Core
{
    /// <summary>
    /// Audio-only virtual menu for adjusting screen reader settings.
    /// Accessible via F8 key. No Unity UI overlay - purely navigational state + announcements.
    /// </summary>
    public static class ModMenu
    {
        /// <summary>
        /// Whether the mod menu is currently open.
        /// </summary>
        public static bool IsOpen { get; private set; }

        private static int currentIndex = 0;
        private static List<MenuItem> items;

        #region Menu Item Types

        // Names, option labels and descriptions are English keys translated each time they are
        // read: the menu is built at mod init, before the game language is known, so translating
        // at construction would freeze every label in English.
        private abstract class MenuItem
        {
            protected string NameKey;
            public string Name => T(NameKey);
            /// <summary>Description read by the I key (null = none).</summary>
            public virtual string Description => null;
            public abstract string GetValueString();
            public abstract void Adjust(int delta);
            public abstract void Toggle();
        }

        private class ToggleItem : MenuItem
        {
            private readonly Func<bool> getter;
            private readonly Action toggle;
            private readonly string onDescription;
            private readonly string offDescription;

            public ToggleItem(string nameKey, Func<bool> getter, Action toggle, string onDescription, string offDescription)
            {
                NameKey = nameKey;
                this.getter = getter;
                this.toggle = toggle;
                this.onDescription = onDescription;
                this.offDescription = offDescription;
            }

            public override string Description => T(getter() ? onDescription : offDescription);
            public override string GetValueString() => getter() ? T("On") : T("Off");
            public override void Adjust(int delta) => toggle();
            public override void Toggle() => toggle();
        }

        private class VolumeItem : MenuItem
        {
            private readonly Func<int> getter;
            private readonly Action<int> setter;

            public VolumeItem(string nameKey, Func<int> getter, Action<int> setter)
            {
                NameKey = nameKey;
                this.getter = getter;
                this.setter = setter;
            }

            public override string Description => T("Left and Right change the volume in steps of 5 percent. Enter mutes it, or sets it to 50 percent when muted.");
            public override string GetValueString() => $"{getter()}%";

            public override void Adjust(int delta)
            {
                int current = getter();
                int newValue = Math.Clamp(current + (delta * 5), 0, 100);
                setter(newValue);
            }

            public override void Toggle()
            {
                // Toggle between 0 and 50 for quick mute/unmute
                int current = getter();
                setter(current == 0 ? 50 : 0);
            }
        }

        private class EnumItem : MenuItem
        {
            private readonly string[] optionKeys;
            private readonly Func<int> getter;
            private readonly Action<int> setter;
            private readonly string description;

            public EnumItem(string nameKey, string[] optionKeys, Func<int> getter, Action<int> setter, string description)
            {
                NameKey = nameKey;
                this.optionKeys = optionKeys;
                this.getter = getter;
                this.setter = setter;
                this.description = description;
            }

            public override string Description => T(description);

            public override string GetValueString()
            {
                int index = getter();
                if (index >= 0 && index < optionKeys.Length)
                    return T(optionKeys[index]);
                return T("Unknown");
            }

            public override void Adjust(int delta)
            {
                int current = getter();
                int newValue = current + delta;
                if (newValue < 0) newValue = optionKeys.Length - 1;
                if (newValue >= optionKeys.Length) newValue = 0;
                setter(newValue);
            }

            public override void Toggle() => Adjust(1);
        }

        private class SectionHeader : MenuItem
        {
            public SectionHeader(string nameKey)
            {
                NameKey = nameKey;
            }

            public override string GetValueString() => "";
            public override void Adjust(int delta) { }
            public override void Toggle() { }
        }

        private class ActionItem : MenuItem
        {
            private readonly Action action;
            private readonly string description;

            public ActionItem(string nameKey, Action action, string description)
            {
                NameKey = nameKey;
                this.action = action;
                this.description = description;
            }

            public override string Description => T(description);
            public override string GetValueString() => "";
            public override void Adjust(int delta) => action();
            public override void Toggle() => action();
        }

        #endregion

        /// <summary>
        /// Initializes the mod menu with all menu items.
        /// Call this once during mod initialization.
        /// </summary>
        public static void Initialize()
        {
            items = new List<MenuItem>
            {
                // Audio Feedback section
                new SectionHeader("Audio Feedback"),
                new ToggleItem("Wall Tones",
                    () => AudioLoopManager.WallTonesEnabled,
                    () => FFIV_ScreenReaderMod.Instance?.ToggleWallTones(),
                    "On. Tones sound toward any wall right next to you.",
                    "Off. No wall tones."),
                new ToggleItem("Footsteps",
                    () => AudioLoopManager.FootstepsEnabled,
                    () => FFIV_ScreenReaderMod.Instance?.ToggleFootsteps(),
                    "On. A click plays for every tile you move.",
                    "Off. Movement is silent."),
                new ToggleItem("Beacon Navigation",
                    () => AudioLoopManager.AudioBeaconsEnabled,
                    () => FFIV_ScreenReaderMod.Instance?.ToggleAudioBeacons(),
                    "On. Choosing a destination starts an audio beacon instead of spoken directions.",
                    "Off. Choosing a destination speaks step-by-step directions."),
                new ToggleItem("Beacon Destination Announcement",
                    () => FFIV_ScreenReaderMod.AnnounceOnBeaconRestartEnabled,
                    () => FFIV_ScreenReaderMod.Instance?.ToggleAnnounceOnBeaconRestart(),
                    "On. Restarting the beacon also speaks the destination.",
                    "Off. Restarting the beacon only pings."),
                new ToggleItem("Stick Click Normalization",
                    () => FFIV_ScreenReaderMod.StickClickNormalizationEnabled,
                    () => FFIV_ScreenReaderMod.Instance?.ToggleStickClickNormalization(),
                    "On. L3 and R3 go to the game; their mod functions move to mod mode.",
                    "Off. L3 toggles beacon navigation and R3 the pathfinding filter."),
                new ToggleItem("Menu Position Announcements",
                    () => PreferencesManager.MenuPositionAnnouncementsEnabled,
                    () => PreferencesManager.SaveMenuPositionAnnouncements(!PreferencesManager.MenuPositionAnnouncementsEnabled),
                    "On. Menu entries end with their position, such as 3 of 12.",
                    "Off. Menu entries are read without their position."),
                new ToggleItem("Auto Detail",
                    () => FFIV_ScreenReaderMod.AutoDetailEnabled,
                    () => FFIV_ScreenReaderMod.Instance?.ToggleAutoDetail(),
                    "On. Descriptions are read as soon as an item, spell or shop entry is focused.",
                    "Off. Press I to hear the description of the focused entry."),

                // Volume Controls section
                new SectionHeader("Volume Controls"),
                new VolumeItem("Wall Bump Volume",
                    () => PreferencesManager.WallBumpVolume,
                    PreferencesManager.SetWallBumpVolume),
                new VolumeItem("Footstep Volume",
                    () => PreferencesManager.FootstepVolume,
                    PreferencesManager.SetFootstepVolume),
                new VolumeItem("Wall Tone Volume",
                    () => PreferencesManager.WallToneVolume,
                    PreferencesManager.SetWallToneVolume),
                new VolumeItem("Beacon Volume",
                    () => PreferencesManager.BeaconVolume,
                    PreferencesManager.SetBeaconVolume),

                // Navigation Filters section
                new SectionHeader("Navigation Filters"),
                new ToggleItem("Pathfinding Filter",
                    () => FFIV_ScreenReaderMod.PathfindingFilterEnabled,
                    () => FFIV_ScreenReaderMod.Instance?.entityNavFacade?.TogglePathfindingFilter(),
                    "On. Entity lists skip anything you cannot walk to.",
                    "Off. Entity lists include unreachable entities."),
                new ToggleItem("Map Exit Filter",
                    () => EntityNavigationFacade.MapExitFilterEnabled,
                    () => FFIV_ScreenReaderMod.Instance?.entityNavFacade?.ToggleMapExitFilter(),
                    "On. Exits leading to the same place are merged into the closest one.",
                    "Off. Every map exit is listed."),
                new ToggleItem("Layer Transition Filter",
                    () => EntityNavigationFacade.ToLayerFilterEnabled,
                    () => FFIV_ScreenReaderMod.Instance?.entityNavFacade?.ToggleToLayerFilter(),
                    "On. Layer transition points are hidden from entity lists.",
                    "Off. Layer transition points are listed."),

                // Battle Results section
                new SectionHeader("Battle Results"),
                new ToggleItem("EXP Counter Sound",
                    () => FFIV_ScreenReaderMod.ExpCounterEnabled,
                    FFIV_ScreenReaderMod.ToggleExpCounter,
                    "On. A beep ticks while the EXP bar fills after battle.",
                    "Off. The EXP bar fills silently."),
                new VolumeItem("EXP Counter Volume",
                    () => PreferencesManager.ExpCounterVolume,
                    PreferencesManager.SetExpCounterVolume),

                // Battle Settings section
                new SectionHeader("Battle Settings"),
                new EnumItem("Enemy HP Display",
                    new[] { "Numbers", "Percentage", "Hidden" },
                    () => PreferencesManager.EnemyHPDisplay,
                    PreferencesManager.SetEnemyHPDisplay,
                    "How enemy HP is read while targeting: as numbers, as a percentage, or not at all."),

                // Close Menu action
                new ActionItem("Close Menu", Close, "Closes the mod menu and returns to the game.")
            };
        }

        /// <summary>
        /// Opens the mod menu.
        /// </summary>
        public static void Open()
        {
            if (IsOpen) return;

            IsOpen = true;
            currentIndex = 0;

            // Skip section header at index 0
            if (items != null && items.Count > 1 && items[0] is SectionHeader)
                currentIndex = 1;

            // Announce that the menu opened (both F8 and the controller Start button reach here),
            // then the first item after a short delay. The menu is virtual — game input is
            // suppressed via ControllerRouter.SuppressGameInput + InputPassthroughPatches (no
            // window stealing), so we speak the title ourselves instead of relying on NVDA.
            FFIV_ScreenReaderMod.SpeakText(T("Mod menu"), interrupt: true);
            CoroutineManager.StartManaged(AnnounceFirstItemDelayed());
        }

        private static IEnumerator AnnounceFirstItemDelayed()
        {
            // Wait 2 frames for TTS to queue "Mod menu" before adding first item
            yield return null;
            yield return null;

            if (IsOpen) // Still open after delay
            {
                AnnounceCurrentItem(interrupt: false);
            }
        }

        /// <summary>
        /// Closes the mod menu.
        /// </summary>
        public static void Close()
        {
            if (!IsOpen) return;

            IsOpen = false;
            // Announce on every close path (keyboard Escape/F8, "Close Menu" item, controller B/Start).
            // Game input is restored automatically — ControllerRouter.SuppressGameInput becomes false.
            FFIV_ScreenReaderMod.SpeakText(T("Mod menu closed"), interrupt: true);
        }

        /// <summary>
        /// Handles input when the mod menu is open. Reads keys via GamepadManager
        /// (SDL3 + GetAsyncKeyState — hardware state); game input is suppressed by
        /// InputPassthroughPatches + Input.ResetInputAxes while open. No window focus stealing.
        /// Returns true if input was consumed (menu is open).
        /// </summary>
        public static bool HandleInput()
        {
            if (!IsOpen) return false;
            if (items == null || items.Count == 0) return false;

            // Escape or F8 to close
            if (GamepadManager.IsKeyCodePressed(KeyCode.Escape) || GamepadManager.IsKeyCodePressed(KeyCode.F8))
            {
                Close();
                return true;
            }

            // Up arrow - navigate to previous item
            if (GamepadManager.IsKeyCodePressed(KeyCode.UpArrow))
            {
                NavigatePrevious();
                return true;
            }

            // Down arrow - navigate to next item
            if (GamepadManager.IsKeyCodePressed(KeyCode.DownArrow))
            {
                NavigateNext();
                return true;
            }

            // Left arrow - decrease value
            if (GamepadManager.IsKeyCodePressed(KeyCode.LeftArrow))
            {
                AdjustCurrentItem(-1);
                return true;
            }

            // Right arrow - increase value
            if (GamepadManager.IsKeyCodePressed(KeyCode.RightArrow))
            {
                AdjustCurrentItem(1);
                return true;
            }

            // Enter or Space - toggle/activate
            if (GamepadManager.IsKeyCodePressed(KeyCode.Return) || GamepadManager.IsKeyCodePressed(KeyCode.Space))
            {
                ToggleCurrentItem();
                return true;
            }

            // I - describe the current setting
            if (GamepadManager.IsKeyCodePressed(KeyCode.I))
            {
                AnnounceCurrentItemDescription();
                return true;
            }

            return true; // Consume all input while menu is open
        }

        private static void AnnounceCurrentItemDescription()
        {
            if (currentIndex < 0 || currentIndex >= items.Count) return;

            string description = items[currentIndex].Description;
            FFIV_ScreenReaderMod.SpeakText(
                string.IsNullOrWhiteSpace(description) ? T("No description") : description,
                interrupt: true);
        }

        internal static void NavigateNext()
        {
            int startIndex = currentIndex;
            do
            {
                currentIndex++;
                if (currentIndex >= items.Count)
                    currentIndex = 0;

                // Skip section headers
                if (!(items[currentIndex] is SectionHeader))
                    break;

            } while (currentIndex != startIndex);

            AnnounceCurrentItem();
        }

        internal static void NavigatePrevious()
        {
            int startIndex = currentIndex;
            do
            {
                currentIndex--;
                if (currentIndex < 0)
                    currentIndex = items.Count - 1;

                // Skip section headers
                if (!(items[currentIndex] is SectionHeader))
                    break;

            } while (currentIndex != startIndex);

            AnnounceCurrentItem();
        }

        internal static void AdjustCurrentItem(int delta)
        {
            if (currentIndex < 0 || currentIndex >= items.Count) return;

            var item = items[currentIndex];
            if (item is SectionHeader) return;

            item.Adjust(delta);
            AnnounceCurrentItem();
        }

        internal static void ToggleCurrentItem()
        {
            if (currentIndex < 0 || currentIndex >= items.Count) return;

            var item = items[currentIndex];
            if (item is SectionHeader) return;

            item.Toggle();

            // For action items (like Close Menu), don't re-announce
            if (item is ActionItem) return;

            AnnounceCurrentItem();
        }

        private static void AnnounceCurrentItem(bool interrupt = true)
        {
            if (currentIndex < 0 || currentIndex >= items.Count) return;

            var item = items[currentIndex];
            string value = item.GetValueString();

            string announcement;
            if (string.IsNullOrEmpty(value))
            {
                announcement = item.Name;
            }
            else
            {
                announcement = $"{item.Name}: {value}";
            }

            var (index, count) = NavigablePosition();
            announcement = MenuPosition.Format(announcement, index, count);

            FFIV_ScreenReaderMod.SpeakText(announcement, interrupt: interrupt);
        }

        /// <summary>
        /// Position of the current item among the navigable (non-header) items. Section headers are
        /// silently skipped during navigation, so the user hears "(N of total settings)" — not counting
        /// the invisible headers.
        /// </summary>
        private static (int index, int count) NavigablePosition()
        {
            int count = 0, index = -1;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] is SectionHeader) continue;
                if (i == currentIndex) index = count;
                count++;
            }
            return (index, count);
        }

    }
}
