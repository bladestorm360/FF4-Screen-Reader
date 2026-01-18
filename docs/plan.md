# FF4 Screen Reader Mod - Porting Plan

## Status: COMPLETE

All features ported from FF6 mod and tested successfully.

---

## Feature Completion

| Feature | Status |
|---------|--------|
| Field Navigation & Pathfinding | ✅ |
| Menu System (all menus) | ✅ |
| Battle System | ✅ |
| Shops | ✅ |
| Victory Screen | ✅ |
| Vehicle Support | ✅ |
| Vehicle State Announcements | ✅ |
| Status Screen Navigation | ✅ |
| Wall Bump Detection | ✅ |
| Vehicle Landing Announcements | ✅ |
| Story Event Text (LineFade) | ✅ |
| Per-Page Dialogue Reading | ✅ |
| Moon Pathfinding (One-Way Ledges) | ✅ |
| Popup/Confirmation Dialogs | ✅ |
| Save/Load Confirmation Popups | ✅ |
| Save Slot Navigation | ✅ |
| Title Screen "Press any button" | ✅ |

---

## Overview

Port the FFVI_MOD screen reader accessibility mod to Final Fantasy IV Pixel Remaster.

- **Source:** `d:\games\dev\unity\ffpr\ff6\ff6ScreenReader\FFVI_MOD`
- **Target:** `D:\Games\Dev\Unity\FFPR\FF4\ff4-screen-reader`

## Exclusions (FF6-Specific)

- Airship Navigation (`AirshipNavigationPatches.cs`, `AirshipNavigationReader.cs`)
- Esper/Magicite system (in `StatusDetailsReader.cs`, `AbilityMenuPatches.cs`)

## Files Ported

### Core Infrastructure
| File | Modifications |
|------|---------------|
| `FFIV_ScreenReaderMod.cs` | Renamed from FFVI, removed airship methods |
| `InputManager.cs` | Removed airship hotkey (H) |
| `EntityCache.cs` | None |
| `EntityNavigator.cs` | None |
| `Filters/*.cs` | None |

### Menu Systems
| File | Modifications |
|------|---------------|
| `StatusDetailsReader.cs` | Removed Magicite, added FF4 stats |
| `CharacterSelectionReader.cs` | FF4 character names |
| Others | None |

### Battle & Patches
| File | Modifications |
|------|---------------|
| `BattleCommandPatches.cs` | Removed FF6-specific commands |
| Others | Minor namespace changes |

### Field Navigation
| File | Modifications |
|------|---------------|
| `EntityFactory.cs` | Removed non-existent ObjectTypes |
| `NavigableEntity.cs` | Removed non-existent ObjectTypes |
| `FieldNavigationHelper.cs` | Added moon reverse-path validation |
| Others | None |

---

## Key Implementation Notes

### Vehicle State Announcements
- **Primary Hook:** `FieldController.ChangeTransportation(int transportationId, ...)` - most reliable
- **Supplementary:** `FieldPlayer.GetOn/GetOff` patches (for scenarios where they fire)
- **Backup:** `FieldPlayer.ChangeMoveState` patch (catches edge cases)
- `MoveStateHelper.cs` - State caching, `OnMapTransition()` for interior maps
- `MovementSpeechPatches.cs` - All Harmony patches with duplicate prevention
- Announces: Hovercraft, Enterprise, Falcon, Lunar Whale, Chocobos, "On foot"
- Interior maps (e.g., Lunar Whale 2F) automatically set to on-foot state
- Skips intermediate states (TRANSPORT_CONTENT) during cinematics
- Uses `interrupt: false` to not interrupt location announcements

### Status Screen Navigation (17 stats, 4 groups)
| Group | Stats |
|-------|-------|
| CharacterInfo | Level, Handed, Experience, Next Level |
| Vitals | HP, MP |
| Attributes | Strength, Agility, Stamina, Intellect, Spirit |
| CombatStats | Attack, Accuracy, Defense, Evasion, Magic Defense, Magic Evasion |

**Controls:** Up/Down (navigate), PgUp/PgDn (jump groups), Home/End

**UI Hook Approach:** Count multipliers (9x, 6x) read from `ParameterContentController` UI components, not data API.

### Vehicle Landing Announcements
- Patches `MapUIManager.SwitchLandable(bool)`
- Announces "Can land" when entering landable zone while in vehicle

### Map Name Deduplication
- Content-based matching: if SystemMessage text is contained in recent FadeMessage, skip it
- Prevents "Entering Mysidia. Mysidia." duplicates
- Short location-like strings (1-4 words) suppressed when no FadeMessage fired

### Per-Page Dialogue Reading
- `DialogueTracker` stores messages from `SetContent`
- `PlayingInit` hook announces one page at a time
- Format: "Speaker: Text" (speaker only on change)
- Speaker tracking resets on scene transitions and LineFade events

### Moon Pathfinding (One-Way Ledges)
- Moon surface (MapId=3) has disconnected regions separated by ledges
- **Solution:** After finding forward path, test reverse path (dest→player)
- If reverse fails, path crosses one-way ledge → reject

### Entity Scan Timing
- **Hook:** `MainGame.set_FieldReady(bool value)` - game's internal signal that field is ready
- When `value == true`, triggers `EntityCache.ForceScan()` automatically
- Entities available immediately when user presses navigation keys
- 5-second periodic rescan continues for cache maintenance

### Save Slot Navigation
- Patches `SaveListController.SelectContent` to announce slot info when navigating
- Reads `SaveContentView` fields via memory offsets:
  | Field | Offset | Purpose |
  |-------|--------|---------|
  | slotNameText | 0x28 | "File", "Autosave", "Quick Save" |
  | slotNumText | 0x38 | Slot number |
  | timeStampDate | 0xD0 | Save date |
  | timeStampTime | 0xD8 | Save time |
  | areaNameText | 0x58 | Location |
  | charaNameText | 0x40 | Lead character |
  | levelText | 0x50 | Level |
  | hourText | 0x70 | Play time hours |
  | minuteText | 0x80 | Play time minutes |
  | emptyText | 0x88 | "Empty" |
- Format: "File 2, 01/17/2026 8:10, Moon, Edge Level 45, Time 13:06"
- `SaveLoadMenuState.IsActive` suppresses `MenuTextDiscovery`
- Visibility check prevents announcement during title screen initialization

---

## Bug Fixes Applied

| Issue | Solution |
|-------|----------|
| Map name spoken twice | Content-based deduplication in `LocationMessageTracker` |
| Battle messages missing | Added `CreateActFunction` patch, `GlobalBattleMessageTracker` |
| Stats announced on load | Added `MenuManager.IsOpen` check |
| Equipment slots interrupted | Added `IsInEquipmentSlotContext()` check |
| Title menu "New Game" on return | Use `commandId` enum for dedup |
| Stale messageLineIndex | Track own `nextAnnouncementIndex` |
| Item targets not read | Added `ItemUseController.SelectContent` patch |
| "Potion" interruption | Added skip conditions to `SkipNextIndex`/`SkipPrevIndex` |
| Vehicle announcements not firing | Hook `FieldController.ChangeTransportation` + interior map detection |
| Moon exits unreachable | Reverse-path validation for MapId=3 |
| Popup dialogs not reading | Added `PopupPatches.cs` with base Popup.Open()/Close() hooks |
| Save/Load confirmations silent | Added `SaveLoadPatches.cs` with SetPopupActive(bool) hooks |
| Title "Press any button" missing | SplashController.InitializeTitle + TitleWindowController.SetEnableStartObject |
| Save slots only reading location | Added `SaveListController.SelectContent` patch with full slot info |
| "Empty" on splash screen load | Added visibility checks (`activeInHierarchy`) before announcing |
| Entity scan timing on map load | Hook `MainGame.set_FieldReady` to trigger scan when field is ready |
