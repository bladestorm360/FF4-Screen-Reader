# FF4 Screen Reader - Status

**COMPLETE** (ported from `ff6/ff6ScreenReader/FFVI_MOD`)

**TODO:** Multi-phase Victory Screen - break into phases (gil/items, then per-character XP/level-ups)

## Features ✅
Field Navigation, Pathfinding, Moon Pathfinding, Wall Bump | Menu System | Battle (actions, damage, status, messages, two-part abilities, defeat message) | Shops | Victory Screen | Vehicles | Status Screen (17 stats) | Story Text, Dialogue (multi-line pages) | Popups | Game Over Popup (Load/Title buttons, Yes/No confirmation) | Save/Load | Title | Namingway | Item Equipment Info (I key) | Config Announcements | Entity Name Translation (0 key dump) | F1 Walk/Run Toggle | F3 Encounters Toggle

## Code Quality (2026-01-20)

**Event-Driven Entity Refresh:** Treasure chests and dialogue end trigger immediate entity cache refresh via `FieldTresureBox.Open()` and `MessageWindowManager.Close()` hooks. Timer removed entirely.

**Deduplication Consolidation:** 18 independent `lastAnnounced*` variables → centralized utilities:
- `Utils/AnnouncementDeduplicator.cs` - Context-keyed deduplication
- `Utils/CharacterStatusHelper.cs` - HP/MP/status reading

**Contexts:** AbilityMenu (Command/Content/UseTarget), ItemMenu (List/UseTarget), EquipmentMenu (Select/Tracker), Battle (Target.Player/Enemy, ConditionAdd, Turn), BattleCommand/Item/Ability.Select, ConfigMenu (Command/KeysSetting), PartySetting.Select, Shop.Item, Naming.Select

**Not converted:** MovementSpeechPatches (value comparison), MessagePatches (formatting decisions)

**Removed:** All timer-based deduplication (MESSAGE_THROTTLE_SECONDS, etc.) - now uses simple equality

## Sound System Improvements (2026-01-29)
Ported from FF1 screen reader:

**16-bit Audio:** Eliminates quantization noise/static at low volumes. WAVEFORMATEX updated to 16-bit PCM (nAvgBytesPerSec=88200, nBlockAlign=4, wBitsPerSample=16). Buffer allocation increased to 32KB.

**Volume Controls:** Per-sound volume preferences (0-100%, default 50%). MelonPreferences entries for WallBump, Footstep, WallTone, Beacon volumes. `ScaleSamples()` applies 16-bit scaling. Wall tones use volume-baked generation to preserve dynamic range.

**ModMenu (F8):** Audio-only virtual menu accessible via F8 key. Toggle audio features, adjust volumes (5% steps with Left/Right), uses Windows API input for focus-independent navigation. Focus blocker window steals game focus while menu is open.

**IL2CPP-Compatible Loops:** Wall tone and beacon loops use manual time-based waiting (`Time.time < nextTime`) instead of `WaitForSeconds`. Self-terminating loops (`while (enableFeature)`) with cleanup on exit.

**CoroutineManager Enhancements:** Added `StopManaged()` with wrapper-to-original tracking (dictionaries). Fixed start/stop mismatch where `StartManaged()` used manager but stop bypassed it. Increased `maxConcurrentCoroutines` from 3 to 20. Scene load now calls `GameObjectCache.ClearAll()` before audio loop restart.

**Safety Checks:** NaN/bounds validation for positions. Beacon debouncing (80% interval minimum). Pre-cached direction vectors to avoid per-cycle allocations.

**Files:** `Utils/SoundPlayer.cs`, `Core/FFIV_ScreenReaderMod.cs`, `Core/ModMenu.cs`, `Core/InputManager.cs`, `Field/FieldNavigationHelper.cs`

## Entity Name Translation (2026-01-28)
Ported `EntityTranslator` from FF3. Translates Japanese entity names to English via JSON dictionary.

**Files:** `Utils/EntityTranslator.cs`, `Field/NavigableEntity.cs` (Name property), `Core/InputManager.cs` (hotkey)

**Hotkey change:** `0` now dumps untranslated names (was reset to All category). Reset to All is now `Shift+K` only.

**Translation file:** `UserData/FFIV_ScreenReader/FF4_translations.json`

**Workflow:** Play → encounter Japanese names → press `0` → edit `EntityNames.json` → copy to `FF4_translations.json` → restart mod

## F1/F3 Toggle Announcements (2026-01-29)
Ported from FF1. Announces walk/run and encounters toggle states.

**F1 Walk/Run:** Announces "Run" or "Walk" after pressing F1. Uses `SetDashFlag` patch to cache toggle state + XOR with AutoDash config for effective running state. **Note:** Only affects dungeons/towns - world map uses fixed walk speed (game limitation).

**F3 Encounters:** Announces "Encounters on" or "Encounters off" after pressing F3. Reads `CheatSettingsData.IsEnableEncount` property.

**Files:** `Utils/MoveStateHelper.cs` (SetCachedDashFlag, GetDashFlag), `Patches/MovementSpeechPatches.cs` (SetDashFlagPatch), `Core/InputManager.cs` (F1/F3 hotkeys)

## Vehicle Name Enhancement (2026-01-29)
Vehicles with same `TransportationType` (e.g., type 8 = "SpecialPlane") now announce their specific names instead of generic types.

**Solution:** Use `TransportationInfo.MessageId` to get localized vehicle names. Each vehicle instance has a unique MessageId pointing to its display name (e.g., "Falcon", "Lunar Whale").

**Changes:**
- `VehicleTypeMap` now stores `(int Type, string MessageId)` tuple instead of just int
- `VehicleEntity.GetVehicleName()` tries MessageId lookup first, falls back to type-based generic name
- Also fixed type 3 fallback name from "Airship" to "Enterprise" (FF4's airship)

**Files:** `Field/FieldNavigationHelper.cs`, `Field/NavigableEntity.cs`, `Field/EntityFactory.cs`

## Wall Tones Victory Screen Fix (2026-01-29)
Fixed wall tones reactivating on victory screen instead of waiting for field return.

**Root cause:** `BattleState.Reset()` was called in `ResultMenuController.Show` patch (victory screen). This triggered `RestoreNavigationAfterBattle()` while still in the Battle scene.

**Fix:**
- Removed `BattleState.Reset()` from `BattleResultPatches.cs:32`
- Added battle→field transition detection in `OnSceneLoaded()` - resets battle state only when leaving Battle scene for a non-battle scene

**Files:** `Patches/BattleResultPatches.cs`, `Core/FFIV_ScreenReaderMod.cs`

## Game Over Popup & Defeat Message (2026-01-31)
Added accessibility support for party wipe/game over flow.

**Defeat Message:**
- Patched `BattleCommandMessageController.SetMessage` (KeyInput) and `SetCommandMessage`/`SetSystemMessage` (Touch)
- Announces "The party was defeated" with interrupt=true for immediate playback
- Deduplication via string comparison, skips redundant action names

**Game Over Popup (Load/Title):**
- `GameOverSelectPopup.UpdateCommand` → announces "Load" or "Title" on button navigation
- Uses cursor index deduplication to prevent repeat announcements

**"Start from recent save data?" Confirmation:**
- `GameOverPopupController.InitSaveLoadPopup` → triggers delayed popup message read
- `GameOverLoadPopup.UpdateCommand` → announces "Yes" or "No" on button navigation
- Navigates controller→view→loadPopup→messageText via pointer offsets

**Files:** `Patches/BattleMessagePatches.cs`, `Patches/PopupPatches.cs`, `Core/FFIV_ScreenReaderMod.cs`

## Performance Optimizations (2026-01-29)
Addressed memory leaks and performance issues from code audit.

**Memory Leak Fixes (HIGH priority):**
- Event handler leak: Stored `_onSceneLoadedHandler` delegate as field for proper unsubscription
- AnnouncementDeduplicator unbounded growth: Added `PruneIfNeeded()` with `MaxCacheSize=100`
- ConfigMenuPatches dictionary leak: Replaced `Dictionary<Controller, string>` with `ConditionalWeakTable<Controller, StringHolder>` for Touch controller tracking

**Performance Optimizations (MEDIUM priority):**
- EntityNavigator: Added `_navigationListDirty` flag to skip unnecessary re-sorting
- GroupEntity: Added per-frame caching (`_cachedRepresentative`, `_lastCacheFrame`) for `GetRepresentative()`
- MapTransitionPatches: Added 0.1s throttling to `IsScreenFading` property
- SoundPlayer: Added `_toneCache` dictionary (max 16 entries) for wall tone buffers

**Dead Code Removal:**
- Removed unused `IsInBattle()` method from InputManager

**Files:** `Core/FFIV_ScreenReaderMod.cs`, `Utils/AnnouncementDeduplicator.cs`, `Patches/ConfigMenuPatches.cs`, `Core/EntityNavigator.cs`, `Field/GroupEntity.cs`, `Patches/MapTransitionPatches.cs`, `Utils/SoundPlayer.cs`, `Core/InputManager.cs`

## Entity Scanner Placeholder Filter (2026-02-02)
Filters out placeholder entities from the entity scanner to reduce clutter.

**Filtered entity types:**
- "Unknown" entities (no name defined)
- Vehicle spawn point defaults ("Default _xxx", "Default_xxx")
- Generic/placeholder prefixes ("汎用xxx" = "generic")
- Visual effects ("渦" whirlpool, "水飛沫" water splash)
- GotoMap state indicators ("城１", "壊れたお城", "_崩壊後" suffix)

**Conservative approach:**
- TransportationEventAction entities only filtered if obviously placeholder
- Same-map teleports preserved (needed for dungeon navigation like Sealed Cave ropes)
- State indicators only filtered for GotoMap type, not Event/Transportation types

**Files:** `Field/EntityFactory.cs` (IsPlaceholderEntity, IsPlaceholderGotoMap methods)

## Exclusions
Esper/Magicite, Airship Navigation (FF6-specific)
