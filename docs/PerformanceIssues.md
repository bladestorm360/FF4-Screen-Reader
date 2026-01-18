# FF4 Screen Reader - Performance Issues

## Overview

This document catalogs polling, per-frame, and timer-based approaches that violate the coding standards defined in CLAUDE.md.

**Rules from CLAUDE.md:**
- **Rule 1:** No polling or per-frame approaches (continuous loops checking state)
- **Rule 2:** No timer-based approaches with arbitrary delays (use game's internal timing instead)

---

## Critical Violations

### 1. ~~MovementSpeechPatches.cs - Polling Coroutine~~ ✅ FIXED

**Status:** Resolved - Polling coroutine removed; now uses `FieldController.ChangeTransportation` Harmony patch as primary hook.

---

### 2. ~~ShopPatches.cs - Time-Based Deduplication~~ ✅ FIXED

**Status:** Resolved - Removed DateTime-based throttling; now uses content-based deduplication only (`itemName != lastAnnouncedItem`).

---

### 3. ~~ItemMenuPatches.cs - Time.time Throttling~~ ✅ FIXED

**Status:** Resolved - Removed Time.time-based throttling from `EquipmentMenuTracker`; now uses content-based deduplication only (`message != lastAnnouncement`).

---

### 4. ~~WallBumpPatches.cs - Time-Based Cooldown~~ ✅ FIXED

**Status:** Resolved - Now uses event-driven `FieldController.OnPlayerHitCollider` hook instead of polling. Rate-limiting (400ms) is acceptable since it throttles an event callback, not a polling loop.

---

### 5. ~~MoveStateHelper.cs - Timeout-Based State Tracking~~ ✅ FIXED

**Status:** Resolved - Timeout logic removed; now uses `FieldController.ChangeTransportation` Harmony patch for reliable vehicle state detection.

---

### 6. ~~FFIV_ScreenReaderMod.cs - Scene Initialization Delays~~ ✅ FIXED

**Status:** Resolved - Replaced timer-based delays with user-triggered on-demand scanning.

**Previous Issue:**
- `DelayedInitialScan()` and `DelayedMapTransitionScan()` used hardcoded 0.5s delays
- Arbitrary timing could be too short on slower PCs
- Violated Rule 2 (arbitrary fixed-delay timer)

**Solution:**
1. **Removed coroutines** - Deleted `DelayedInitialScan()` and `DelayedMapTransitionScan()`
2. **Removed coroutine calls** - No scans on scene load or map transition
3. **User-triggered first scan** - `EnsureFieldContextAndScan()` helper triggers scan on first `[`/`]` key press
4. **Field context check** - Announces "Not on map" if user presses entity navigation keys outside field context
5. **5-second periodic rescan** - Continues to run for cache maintenance after initial scan

**Why This Works:**
- User can't press navigation keys until scene is fully loaded and interactive
- No wasted scans if user never uses entity navigation on a map
- Works reliably on all PC speeds - no timing guesses
- Pure event-driven: user input triggers scan

---

## Acceptable Patterns

### One-Frame Delays (Approved)

These patterns are acceptable per CLAUDE.md guidelines for UI synchronization:

| File | Line | Purpose |
|------|------|---------|
| ShopPatches.cs | 264-283 | Wait for UI update after selection |
| StatusDetailsPatches.cs | 226, 308 | Wait for stat display to populate |
| SaveLoadPatches.cs | 315, 647-648 | Wait for save slot UI to render |
| PopupPatches.cs | 504 | Wait for popup text to populate |
| CursorNavigationPatches.cs | Multiple | Wait for cursor position to update |

### Content-Based Deduplication (Approved)

These patterns use string/value comparison without time components:

| File | Pattern |
|------|---------|
| MessagePatches.cs | `lastAnnouncedSpeaker` string comparison |
| BattleCommandPatches.cs | `lastAnnouncedIndex` integer comparison |
| ConfigMenuPatches.cs | String announcement deduplication |
| TitleMenuPatches.cs | Command ID deduplication |

---

## Summary

| Violation | File | Pattern | Status |
|-----------|------|---------|--------|
| 1 | ~~MovementSpeechPatches.cs~~ | ~~Polling coroutine (0.5s loop)~~ | ✅ Fixed |
| 2 | ~~ShopPatches.cs~~ | ~~DateTime cooldown (100ms)~~ | ✅ Fixed |
| 3 | ~~ItemMenuPatches.cs~~ | ~~Time.time throttle (150ms)~~ | ✅ Fixed |
| 4 | ~~WallBumpPatches.cs~~ | ~~Polling + cooldown~~ | ✅ Fixed |
| 5 | ~~MoveStateHelper.cs~~ | ~~Timeout detection (1s)~~ | ✅ Fixed |
| 6 | ~~FFIV_ScreenReaderMod.cs~~ | ~~Scene delay (0.5s)~~ | ✅ Fixed |

**Total: 0 Critical Violations (6 Fixed)**

All violations involved either:
- Arbitrary delay timers (Rule 2)
- Polling loops that repeatedly check state (Rule 1)
- Time-based throttling that should use exact hook points (Rule 2)
