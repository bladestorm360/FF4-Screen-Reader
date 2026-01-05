# FF4 Screen Reader Mod - Porting Plan

## Current Status: COMPLETE

All major features have been ported and tested successfully.

## Feature Completion Summary

| Feature | Status |
|---------|--------|
| Field Navigation & Pathfinding | ✅ Complete |
| Menu System (all menus) | ✅ Complete |
| Battle System | ✅ Complete |
| Shops | ✅ Complete |
| Victory Screen | ✅ Complete |
| Vehicle Support | ✅ Complete |
| Vehicle State Announcements | ✅ Complete |
| Status Screen Navigation | ✅ Complete |

---

## Overview

Port the FFVI_MOD screen reader accessibility mod to Final Fantasy IV Pixel Remaster.

## Source

- **From:** `d:\games\dev\unity\ffpr\ff6\ff6ScreenReader\FFVI_MOD`
- **To:** `D:\Games\Dev\Unity\FFPR\FF4\ff4-screen-reader`

## Exclusions

The following will NOT be ported:

### 1. Airship Navigation (FF6-specific)
- `Patches/AirshipNavigationPatches.cs` - Entire file
- `Utils/AirshipNavigationReader.cs` - Entire file
- Airship-related methods in main mod file (AnnounceAirshipStatus, heading announcements)

### 2. Esper/Magic Stone System (FF6-specific)
- Magicite reading in `Menus/StatusDetailsReader.cs` (lines 118-126)
- Any Esper management references in `Patches/AbilityMenuPatches.cs`

## Files to Port

### Priority 1: Core Infrastructure
| File | Purpose | Modifications |
|------|---------|---------------|
| `Core/FFVI_ScreenReaderMod.cs` | Main entry point | Rename to FFIV_ScreenReaderMod, remove airship methods |
| `Core/InputManager.cs` | Hotkey handling | Remove airship hotkey (H for airship status) |
| `Core/EntityCache.cs` | Entity registry | No changes expected |
| `Core/EntityNavigator.cs` | Navigation system | No changes expected |
| `Core/Filters/*.cs` | All filter classes | No changes expected |

### Priority 2: Menu Systems
| File | Purpose | Modifications |
|------|---------|---------------|
| `Menus/StatusDetailsReader.cs` | Character status | Remove Magicite reading |
| `Menus/CharacterSelectionReader.cs` | Character selection | May need FF4 character names |
| `Menus/ConfigMenuReader.cs` | Config menu | No changes expected |
| `Menus/TitleMenuReader.cs` | Title screen | No changes expected |
| `Menus/SaveSlotReader.cs` | Save/load slots | No changes expected |
| `Menus/MenuTextDiscovery.cs` | Menu text extraction | No changes expected |
| `Menus/KeyboardGamepadReader.cs` | Input config | No changes expected |

### Priority 3: Dialogue & Battle Messages
| File | Purpose | Modifications |
|------|---------|---------------|
| `Patches/BattleMessagePatches.cs` | Battle announcements | May need FF4-specific adjustments |
| `Patches/BattleCommandPatches.cs` | Battle menu commands | Remove FF6-specific commands (Tools, Blitz, etc.) |
| `Patches/BattleResultPatches.cs` | Battle results | No changes expected |

### Priority 4: Field Navigation
| File | Purpose | Modifications |
|------|---------|---------------|
| `Field/EntityFactory.cs` | Entity creation | No changes expected |
| `Field/NavigableEntity.cs` | Entity base class | No changes expected |
| `Field/GroupEntity.cs` | Entity grouping | No changes expected |
| `Field/FieldNavigationHelper.cs` | Pathfinding | No changes expected |
| `Field/MapNameResolver.cs` | Map name lookup | No changes expected |

### Priority 5: Other Patches
| File | Purpose | Modifications |
|------|---------|---------------|
| `Patches/TitleMenuPatches.cs` | Title menu | No changes expected |
| `Patches/ConfigMenuPatches.cs` | Config menu | No changes expected |
| `Patches/ShopPatches.cs` | Shop dialogue | No changes expected |
| `Patches/ItemMenuPatches.cs` | Item menu | No changes expected |
| `Patches/PartySettingPatches.cs` | Party setup | No changes expected |
| `Patches/FormationRowPatches.cs` | Formation | No changes expected |
| `Patches/CursorNavigationPatches.cs` | Cursor movement | No changes expected |
| `Patches/AbilityMenuPatches.cs` | Ability menu | Remove Esper references |
| `Patches/StatusDetailsPatches.cs` | Status patches | No changes expected |

### Priority 6: Utilities
| File | Purpose | Modifications |
|------|---------|---------------|
| `Utils/GameObjectCache.cs` | Component caching | No changes expected |
| `Utils/CoroutineManager.cs` | Coroutine management | No changes expected |
| `Utils/TextUtils.cs` | Text utilities | No changes expected |
| `Utils/Tolk.cs` | Screen reader wrapper | No changes expected |

## Files NOT to Port
- `Patches/AirshipNavigationPatches.cs`
- `Utils/AirshipNavigationReader.cs`
- `Utils/HierarchyDebug.cs` (debug only)

## Naming Changes

All references to:
- `FFVI` → `FFIV`
- `FFV` → `FFIV`
- `Final Fantasy VI` → `Final Fantasy IV`

## Directory Structure (Target)

```
ff4-screen-reader/
├── FFIV_ScreenReader.csproj
├── build_and_deploy.bat
├── docs/
│   └── plan.md
├── Core/
│   ├── FFIV_ScreenReaderMod.cs
│   ├── EntityCache.cs
│   ├── EntityNavigator.cs
│   ├── InputManager.cs
│   └── Filters/
│       ├── IEntityFilter.cs
│       ├── FilterContext.cs
│       ├── CategoryFilter.cs
│       ├── PathfindingFilter.cs
│       └── MapExitGroupingStrategy.cs
├── Field/
│   ├── EntityFactory.cs
│   ├── NavigableEntity.cs
│   ├── GroupEntity.cs
│   ├── FieldNavigationHelper.cs
│   └── MapNameResolver.cs
├── Menus/
│   ├── StatusDetailsReader.cs
│   ├── CharacterSelectionReader.cs
│   ├── ConfigMenuReader.cs
│   ├── TitleMenuReader.cs
│   ├── SaveSlotReader.cs
│   ├── MenuTextDiscovery.cs
│   └── KeyboardGamepadReader.cs
├── Patches/
│   ├── BattleMessagePatches.cs
│   ├── BattleCommandPatches.cs
│   ├── BattleResultPatches.cs
│   ├── TitleMenuPatches.cs
│   ├── ConfigMenuPatches.cs
│   ├── ShopPatches.cs
│   ├── ItemMenuPatches.cs
│   ├── PartySettingPatches.cs
│   ├── FormationRowPatches.cs
│   ├── CursorNavigationPatches.cs
│   ├── AbilityMenuPatches.cs
│   ├── StatusDetailsPatches.cs
│   └── MovementSpeechPatches.cs
└── Utils/
    ├── GameObjectCache.cs
    ├── CoroutineManager.cs
    ├── TextUtils.cs
    ├── TolkWrapper.cs
    └── MoveStateHelper.cs
```

## Estimated File Count

- **Total files to port:** ~45 files
- **Files requiring modification:** ~6 files
- **Files excluded:** 3 files

## Implementation Order

1. **Phase 1 - Core:** Main mod + input + entity system (5 files)
2. **Phase 2 - Menus:** All menu readers (7 files)
3. **Phase 3 - Battle:** Battle patches and messages (3 files)
4. **Phase 4 - Navigation:** Field navigation system (5 files)
5. **Phase 5 - Patches:** Remaining UI patches (9 files)
6. **Phase 6 - Utils:** Utility classes (4 files)

---

## Current Status: PORTING COMPLETE

All phases completed. Mod is fully functional.

### Vehicle State Announcements (Added)

Ported from FF5 with FF4-specific vehicle names:

**Files Added:**
- `Utils/MoveStateHelper.cs` - State tracking and announcements
- `Patches/MovementSpeechPatches.cs` - Harmony patch for ChangeMoveState

**Announcements:**
- "On hovercraft" - Boarding the Hovercraft
- "On Enterprise" - Boarding the Enterprise airship
- "On [airship name]" - Falcon/Lunar Whale (uses localized name)
- "On yellow chocobo" / "On black chocobo" - Mounting chocobos
- "On foot" - Disembarking any vehicle

---

### Issues Identified from Testing

#### Issue 1: Map Name Spoken Twice
**Symptom:** "Castle Baron 1F" then "Castle Baron" on map entry.
**Cause:** Multiple sources announcing map names (FadeMessageManager + other patches).
**Fix:** Add global deduplication for location/map messages with time-based throttling (1.5s).

#### Issue 2: Battle Messages Not Announced
**Symptom:** Missing announcements for:
- Character's turn (e.g., "Cecil's turn")
- Enemy attacks (e.g., "Floating Eyeball attacks", "Goblin uses Goblin Punch")
**Cause:** Missing `ParameterActFunctionManagment.CreateActFunction` patch.
**Fix:** Port FF5's battle action patches:
- `GlobalBattleMessageTracker` - Deduplication for battle messages
- `ParameterActFunctionManagment.CreateActFunction` - Announces actor + action
- Helper methods: `GetActorName()`, `GetActionName()`

#### Issue 3: Character Statistics on Initial Game Load
**Symptom:** Stats announced when game first loads, not when user opens menu.
**Cause:** `StatusDetailsPatches` fires on initialization, not just user navigation.
**Fix:** Port FF5's `StatusMenuTracker` with `IsUserOpened` flag to distinguish user actions from initialization.

#### Issue 4: Equipment Slots Menu Interrupted by "RHand"
**Symptom:** Equipment slot + item starts speaking, then interrupted by "RHand".
**Cause:** Multiple patches firing - slot patch and separate parts patch.
**Fix:** Port FF5's `EquipmentInfoWindowController.SelectContent` patch that reads both slot name + equipped item together with deduplication.

#### Issue 5: "New Game" Spoken on Title Menu Return
**Symptom:** "New Game" always announced when exiting submenus to title, regardless of cursor position.
**Cause:** Title menu patch uses string-based duplicate detection, which resets when leaving/returning.
**Fix:** Port FF5's approach - use `commandId` (enum) for duplicate detection instead of string.

### Implementation Plan

#### Phase 1: Battle Messages (BattleMessagePatches.cs)
1. Add `GlobalBattleMessageTracker` class with time-based deduplication (1.5s)
2. Add `ParameterActFunctionManagment.CreateActFunction` patch for actor+action
3. Helper methods to get actor name (player/enemy) and action name (ability/command)
4. Natural language formatting: "Cecil attacks", "Goblin uses Goblin Punch"

#### Phase 2: Status Menu Spam Fix (StatusDetailsPatches.cs)
1. Add `StatusMenuTracker` with `IsUserOpened` flag
2. Guard `InitDisplay` patch to check `IsUserOpened` before announcing
3. Set flag when user explicitly navigates to status screen

#### Phase 3: Equipment Slots (New EquipmentMenuPatches.cs)
1. Port `EquipmentInfoWindowController.SelectContent` patch from FF5
2. Read slot name from `partText`, item from `Data.Name`
3. Add time-based deduplication (0.1s throttle)
4. Strip icon markup from text

#### Phase 4: Title Menu Fix (TitleMenuPatches.cs)
1. Change duplicate detection from string to `TitleCommandId` enum
2. Track `lastAnnouncedCommand` as enum value

#### Phase 5: Map Name Deduplication (MessagePatches.cs)
1. Add static tracker for last location message + timestamp
2. Add 1.5s deduplication window to `FadeMessageManager_Play_Patch`
3. Prevent same location being announced from multiple sources

---

## Previous Status: Round 1 Bug Fixes (Completed)

### Build Status
- **Compilation:** ✅ Successful
- **Deployment:** ✅ Working (Tolk.dll + NVDAControllerClient64.dll deployed)
- **Title Menu:** ✅ Working (menu items announced)
- **In-Game Menus:** ⚠️ Partially working (see issues below)

### Issues Identified

#### Issue 1: Dialogue Not Being Read
**Symptom:** In-game dialogue/message windows are not announced.
**Cause:** Missing or broken MessageWindow patches.
**Fix:** Verify `MessageWindowController` patches exist and target correct FF4 methods.

#### Issue 2: Character Stats Read on Map Load (Spam)
**Symptom:** Character statistics are announced when loading maps, not just in menus.
**Cause:** `StatusDetailsPatches.cs` lacks active state checks - patches fire during initialization.
**Fix:** Add checks to ensure patches only fire when:
- The status menu is actually visible and active
- User explicitly requests info (H key in battle)

#### Issue 3: Failed Harmony Patches (from logs)

| Patch | Error | Fix |
|-------|-------|-----|
| `BattleMenuController.SetCommadnMessage` | Parameter "isLeft" not found | Remove `isLeft` parameter from postfix signature |
| `StatusDetailsController.SetNextPlayer` | Method not found in FF4 | Remove patch (FF6-specific method) |
| `StatusDetailsController.SetPrevPlayer` | Method not found in FF4 | Remove patch (FF6-specific method) |
| `StatusDetailsController.SetParameter` | Method not found in FF4 | Remove patch (FF6-specific method) |

### Proposed Fixes

#### Fix 1: Dialogue Patches
- Search dump.cs for `MessageWindow` classes and `ShowMessage` methods
- Add patches for FF4's dialogue system (likely `MessageWindowController`)

#### Fix 2: Active State Guards
Add to all menu patches:
```csharp
// Skip if menu not visible/active
if (__instance.gameObject == null || !__instance.gameObject.activeInHierarchy)
    return;

// Skip if this is initialization (check for valid cursor/selection)
if (targetCursor == null || !targetCursor.gameObject.activeInHierarchy)
    return;
```

#### Fix 3: Remove Non-Existent Methods
In `StatusDetailsPatches.cs`, remove these patches entirely:
- `StatusDetailsController_SetNextPlayer_Patch`
- `StatusDetailsController_SetPrevPlayer_Patch`
- `StatusDetailsController_SetParameter_Patch`

#### Fix 4: Fix BattleMenuController Signature
In battle patches, change:
```csharp
// WRONG - FF6 signature
public static void Postfix(string message, bool isLeft)

// CORRECT - FF4 signature
public static void Postfix(string message)
```

### Implementation Order

1. Remove broken patches (SetNextPlayer, SetPrevPlayer, SetParameter)
2. Fix BattleMenuController signature
3. Add active state guards to StatusDetailsPatches
4. Investigate and fix dialogue reading

---

## Status Screen Navigation Enhancement (Complete)

### Overview

Port the enhanced `StatusDetailsReader` navigation system from FF5 to FF4. This adds arrow key navigation through individual stats on the status screen, allowing users to browse stats one at a time instead of hearing everything at once.

### Current FF4 Implementation

The existing `StatusDetailsReader.cs` provides:
- `ReadStatusDetails()` - Announces name, level, HP/MP on screen entry
- `ReadPhysicalStats()` - Hotkey for Strength, Stamina, Defense, Evade
- `ReadMagicalStats()` - Hotkey for Magic, Spirit, Magic Defense, Magic Evade

**Limitation:** No way to navigate individual stats; users get all-or-nothing announcements.

### FF5 Features to Port

1. **StatusNavigationTracker** - Tracks navigation state (current index, active controller, character data)
2. **StatusNavigationReader** - Arrow key navigation through stats
3. **StatusDetailsHelpers** - Helper to extract character data from controller

### Stats for FF4 (Visible on Status Screen)

| Group | Stats | Notes |
|-------|-------|-------|
| Character Info | Level, Experience, Next Level | FF4 has no Job system |
| Vitals | HP, MP | Current/Max |
| Attributes | Strength, Agility, Stamina, Magic, Spirit | Core stats |
| Combat Stats | Attack, Defense, Evasion, Magic Defense, Magic Evade | Derived stats |

**Total: 13 navigable stats** (vs FF5's 17 - excludes Job, Job Level, ABP, Jobs count, Abilities count)

### Stats NOT Applicable to FF4 (Excluded)

- Job (FF5 job system)
- Job Level (FF5 job system)
- ABP (FF5 ability points)
- Jobs count (FF5 job system)
- Abilities count (FF5 learned abilities)

### Keyboard Controls

| Key | Action |
|-----|--------|
| Up Arrow | Previous stat |
| Down Arrow | Next stat |
| Page Up | Jump to previous group |
| Page Down | Jump to next group |
| Home | Jump to first stat |
| End | Jump to last stat |

### Implementation Steps

#### Step 1: Add StatusNavigationTracker to StatusDetailsPatches.cs

Add the tracker class to manage navigation state:
```csharp
public class StatusNavigationTracker
{
    private static StatusNavigationTracker instance = null;
    public static StatusNavigationTracker Instance { get; }

    public bool IsNavigationActive { get; set; }
    public int CurrentStatIndex { get; set; }
    public OwnedCharacterData CurrentCharacterData { get; set; }
    public StatusDetailsController ActiveController { get; set; }

    public void Reset();
    public bool ValidateState();
}
```

#### Step 2: Add StatusDetailsHelpers to StatusDetailsPatches.cs

Add helper to extract character data:
```csharp
public static class StatusDetailsHelpers
{
    public static OwnedCharacterData GetCharacterDataFromController(StatusDetailsController controller);
}
```

#### Step 3: Extend StatusDetailsReader.cs

Add navigation infrastructure:
- `StatGroup` enum (CharacterInfo, Vitals, Attributes, CombatStats)
- `StatusStatDefinition` class for stat definitions
- `StatusNavigationReader` class with:
  - 13 stat reader methods (FF4-specific)
  - Navigation methods (NavigateNext, NavigatePrevious, JumpToNextGroup, etc.)
  - Group indices array for group jumping

#### Step 4: Update InitDisplay Patch

Modify the existing `StatusDetailsController_InitDisplay_Patch` to:
- Initialize navigation state after announcing status
- Set `StatusNavigationTracker.Instance` properties
- Call `StatusNavigationReader.InitializeStatList()`

#### Step 5: Add Input Handling for Status Screen

Add to `InputManager.cs`:
```csharp
// In HandleGlobalInput() or new HandleStatusInput():
if (StatusNavigationTracker.Instance.IsNavigationActive)
{
    if (Input.GetKeyDown(KeyCode.UpArrow)) StatusNavigationReader.NavigatePrevious();
    if (Input.GetKeyDown(KeyCode.DownArrow)) StatusNavigationReader.NavigateNext();
    if (Input.GetKeyDown(KeyCode.PageUp)) StatusNavigationReader.JumpToPreviousGroup();
    if (Input.GetKeyDown(KeyCode.PageDown)) StatusNavigationReader.JumpToNextGroup();
    if (Input.GetKeyDown(KeyCode.Home)) StatusNavigationReader.JumpToTop();
    if (Input.GetKeyDown(KeyCode.End)) StatusNavigationReader.JumpToBottom();
}
```

### Files to Modify

| File | Changes |
|------|---------|
| `Menus/StatusDetailsReader.cs` | Add StatGroup, StatusStatDefinition, StatusNavigationReader |
| `Patches/StatusDetailsPatches.cs` | Add StatusNavigationTracker, StatusDetailsHelpers, update InitDisplay |
| `Core/InputManager.cs` | Add arrow key handling for status navigation |

### Sighted User Parity Consideration

The status screen displays all stats simultaneously to sighted users. This feature provides **equivalent access** by allowing blind users to navigate the same information that sighted users can see at a glance. All 15 stats are visible on the status screen UI.

### Implementation Status

✅ **Complete** - Tested and working as intended.

**Future Refinement**: Review which stats are actually visible on FF4's status screen to ensure hidden/internal stats are not being exposed. May need to adjust the stat list to match exactly what sighted users see.

---

## Approval Required

Please review this plan and approve before proceeding with the fixes.
