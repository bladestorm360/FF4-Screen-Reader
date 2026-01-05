# FF4 Screen Reader - Debug Log & Feature Status

## Current Status: FUNCTIONAL

## Feature Completion

| Feature | Status | Notes |
|---------|--------|-------|
| **Field Navigation & Pathfinding** | ✅ Complete | Entity detection, obstacle detection, pathfinding |
| **Menu System** | ✅ Complete | Main menu, items, equipment, abilities, status, config |
| **Battle System** | ✅ Complete | Turn order, targeting, damage/heals, status effects |
| **Shops** | ✅ Complete | Buy/sell navigation, item info |
| **Victory Screen** | ✅ Complete | Gil, items, XP, level-ups |
| **Vehicles** | ✅ Complete | Hovercraft, Enterprise, Falcon, Lunar Whale |
| **Status Screen Navigation** | ✅ Complete | Arrow key stat browsing (15 stats in 4 groups) |

## Build Status
- **Compilation**: Successful (0 warnings, 0 errors)
- **Deployment**: Successful (deployed to Mods folder)
- **Runtime**: Fully functional

---

## Round 3 Bug Fixes - Completed

### Issue 1: Characters Not Read When Selecting Item Targets
**Problem**: After selecting an item for use, navigating to select a character target did not announce the character name, HP, or MP.

**Solution**: Added `ItemUseController.SelectContent` patch to `ItemMenuPatches.cs` that announces character name, HP, MP, and status conditions when selecting item use targets.

**File Modified**: `Patches/ItemMenuPatches.cs` - Added `ItemUseController_SelectContent_Patch`

---

### Issue 2: "Potion" Interruption When Navigating Items with Up/Down
**Problem**: When navigating the items menu with up/down arrows, items were being interrupted with "Potion" announcements from the generic cursor patch.

**Root Cause**: `SkipNextIndex` and `SkipPrevIndex` patches in `CursorNavigationPatches.cs` were missing skip conditions for item menus (and other menus). They only checked for battle conditions.

**Solution**: Added all skip conditions to `SkipNextIndex` and `SkipPrevIndex` patches matching `NextIndex`/`PrevIndex`:
- `item_target_select` - item target selection
- `list_window` - item menu list
- `equip_select` - equipment menu
- `equip_info_content` - equipment slots
- `shop` - shops
- `party` - party settings
- `status` - status screen
- `ability` - ability menus
- Battle-related UI elements
- Title and config menus

**File Modified**: `Patches/CursorNavigationPatches.cs` - Added skip conditions to `SkipNextIndex` and `SkipPrevIndex`

---

## Round 2 Bug Fixes - Completed

### Issue 1: Map Name Spoken When Opening Main Menu
**Problem**: Location names like "Castle Baron – 1F" were being passed to the speaker patch and announced as if they were character names.

**Solution**: Added filter in `MessagePatches.cs` to skip speaker names containing location separators:
```csharp
// Filter out location names that get passed as speaker names
// Location names typically contain "–" (en-dash) separator like "Castle Baron – 1F"
if (cleanSpeaker.Contains("–") || cleanSpeaker.Contains("-"))
{
    MelonLogger.Msg($"[Speaker - Filtered location] {cleanSpeaker}");
    return;
}
```

**File Modified**: `Patches/MessagePatches.cs` - `MessageWindowView_SetSpeker_Patch`

---

### Issue 2: Character Statistics Spoken on Game Load
**Problem**: Character vitals (name, HP, MP) were being announced during game initialization when menu scenes were preloaded, even though the user hadn't opened any menu.

**Root Cause**: `StatusWindowController.SelectContent` is called during scene preload, which triggered `CharacterSelectionReader`. The `StatusMenuTracker` approach wasn't working because it couldn't distinguish between preload and actual menu opening.

**Solution**: Added `MenuManager.Instance.IsOpen` check to `CharacterSelectionReader.TryReadCharacterSelection()`, matching FF5's approach:
```csharp
// Safety check: Only read character data if we're in a menu or battle
// This prevents false positives during scene load when menu scenes are preloaded
var sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
bool isBattleScene = sceneName != null && sceneName.Contains("Battle");
bool isMenuOpen = false;

try
{
    var menuManager = MenuManager.Instance;
    if (menuManager != null)
    {
        isMenuOpen = menuManager.IsOpen;
    }
}
catch (Exception ex)
{
    MelonLogger.Warning($"Could not check MenuManager.IsOpen: {ex.Message}");
}

if (!isBattleScene && !isMenuOpen)
{
    MelonLogger.Msg("CharacterSelectionReader: Menu not open and not in battle - skipping character data read to prevent false positives during scene load");
    return null;
}
```

**Files Modified**:
- `Menus/CharacterSelectionReader.cs` - Added MenuManager.IsOpen check at start of TryReadCharacterSelection()
- `Menus/MenuTextDiscovery.cs` - Removed redundant StatusMenuTracker.ShouldAnnounce() wrapper

---

### Issue 3: Equipment Slots Menu Interrupted by "RHand"
**Problem**: When navigating equipment slots, the dedicated patch would announce the slot correctly, but then fallback strategies in MenuTextDiscovery would also run and announce "RHand" or similar partial text.

**Solution**: Added `IsInEquipmentSlotContext()` check at the start of `TryAllStrategies()` in MenuTextDiscovery to skip all fallback strategies when in equipment slot navigation:
```csharp
// CRITICAL: Check if we're in equipment slot context - if so, skip ALL strategies
// The EquipmentInfoWindowController.SelectContent patch handles this menu
if (IsInEquipmentSlotContext(cursor.transform))
{
    MelonLogger.Msg("In equipment slot context - skipping all fallback strategies");
    return null;
}
```

**File Modified**: `Menus/MenuTextDiscovery.cs` - Added IsInEquipmentSlotContext() method and check

---

### Issue 4: H Key Not Announcing "Not in Battle"
**Problem**: When pressing H outside of battle, nothing was announced. FF5 announces "Not in battle or no active character".

**Root Cause**: InputManager.cs had an `IsInBattle()` gate that silently ignored H key presses when not in battle, even though `AnnounceCurrentCharacterStatus()` already had the "Not in battle or no active character" message.

**Solution**: Removed the IsInBattle() gate from InputManager.cs:
```csharp
// Before:
if (Input.GetKeyDown(KeyCode.H))
{
    if (IsInBattle())
    {
        mod.AnnounceCurrentCharacterStatus();
    }
    // Silently ignore if not in battle
}

// After:
if (Input.GetKeyDown(KeyCode.H))
{
    mod.AnnounceCurrentCharacterStatus();
}
```

**File Modified**: `Core/InputManager.cs` - Removed IsInBattle() check for H key

---

## Analysis Summary

### dump.cs Search Results

Searched `D:\Games\Dev\Unity\FFPR\FF4\dump.cs` (~493K lines) for menu-related classes and methods.

#### Key Menu Classes Found in FF4

| Class | Namespace | Line | Purpose |
|-------|-----------|------|---------|
| `MainMenuController` | `Last.UI.KeyInput` | 445067 | Main menu navigation (keyboard/controller) |
| `MainMenuController` | `Last.UI.Touch` | 413505 | Main menu navigation (touch) |
| `CommandMenuController` | `Last.UI` | 391454 | Command menu (Items, Magic, Equip, etc.) |
| `CommandMenuController` | `Last.UI.Touch` | 413169 | Touch version |
| `StatusWindowController` | `Last.UI.KeyInput` | 430447 | Character status screen |
| `StatusWindowController` | `Last.UI.Touch` | 396956 | Touch version |
| `ItemWindowController` | `Last.UI.KeyInput` | 453628 | Item menu |
| `ItemWindowController` | `Last.UI.Touch` | 419222 | Touch version |
| `ItemListController` | `Last.UI.KeyInput` | 451948 | Item list navigation |
| `StatusWindowControllerBase` | `Serial.Template.UI` | 284083 | Abstract base for status windows |
| `StatusWindowControllerBase` | `Serial.Template.UI.KeyInput` | 287354 | KeyInput version |

#### SelectContent Methods Found

```
Line 452040: private void SelectContent(IEnumerable<ItemListContentData>, int, Cursor, CustomScrollView.WithinRangeType)
             - Location: Last.UI.KeyInput.ItemListController

Line 280259: public void SelectContent(int index)
             - Location: AbilityCommandController

Line 280568: public void SelectContent(int index)
             - Location: AbilityContentListController

Line 396052: protected void SelectContent(int index)
             - Location: PartySettingMenuBaseController

Line 430501: protected override void SelectContent(List<StatusWindowContentControllerBase>, int, Cursor)
             - Location: Last.UI.KeyInput.StatusWindowController
```

---

## Current Patches Analysis

### Working Patches (Verified)

| File | Target | Method | Status |
|------|--------|--------|--------|
| `CursorNavigationPatches.cs` | `Il2CppLast.UI.Cursor` | `NextIndex`, `PrevIndex` | Working |
| `TitleMenuPatches.cs` | Title menu | Various | Working |
| `PartySettingPatches.cs` | `PartySettingMenuBaseController` | `SelectContent` | Working |
| `MessagePatches.cs` | Dialogue/Messages | Various | Working |
| `StatusDetailsPatches.cs` | Status screen | InitDisplay, ExitDisplay | Working |

### Patches with Bug Fixes Applied

| File | Target | Fix Applied |
|------|--------|-------------|
| `MessagePatches.cs` | `MessageWindowView.SetSpeker` | Location name filter |
| `MenuTextDiscovery.cs` | `TryAllStrategies` | Equipment slot context check |
| `CharacterSelectionReader.cs` | `TryReadCharacterSelection` | MenuManager.IsOpen check |
| `InputManager.cs` | H key handler | Removed IsInBattle gate |

---

## Changes Made During Port

### Compilation Fixes Applied

1. **StatusDetailsReader.cs** (line ~21)
   - Added missing `ReadStatusDetails` method
   - Changed `param.Level` to `param.ConfirmedLevel()` (property vs method)

2. **PartySettingPatches.cs**
   - Changed `FirstSlotSelect` → `SlotSelect`
   - Changed `FirstMemberSelect` → `MemberSelect`
   - Changed `OnlySlotSelect` → `SlotSelect`
   - (FF4 has different State enum values than FF6)

3. **EntityFactory.cs**
   - Removed `MapConstants.ObjectType.MapRange`
   - Removed `MapConstants.ObjectType.ChangeAnimationKeyArea`
   - Removed `MapConstants.ObjectType.SwitchEvent`
   - Removed `MapConstants.ObjectType.RandomEvent`
   - (These ObjectType values don't exist in FF4)

4. **NavigableEntity.cs**
   - Removed non-existent ObjectType references

5. **BattleMessagePatches.cs**
   - Removed `SetSpeaker` patch (method doesn't exist in FF4)
   - Removed `SetCommandText` patch (method doesn't exist in FF4)

6. **AbilityMenuPatches.cs**
   - Added `Il2CppSerial.FF4.UI.KeyInput` namespace
   - Removed `AbilityChangeController` patches (doesn't exist)

7. **BattleCommandPatches.cs**
   - Added `Il2CppSerial.FF0.UI.KeyInput` namespace

8. **StatusDetailsPatches.cs**
   - Added `Il2CppSerial.Template.UI.KeyInput` namespace

### Round 2 Bug Fixes Applied

1. **MessagePatches.cs** - Speaker filter for location names
2. **CharacterSelectionReader.cs** - MenuManager.IsOpen check
3. **MenuTextDiscovery.cs** - Removed StatusMenuTracker wrapper, added equipment slot context check
4. **InputManager.cs** - Removed IsInBattle gate for H key

---

## Testing Checklist

- [x] Launch game, check MelonLoader logs for patch errors
- [x] Map name no longer announced when opening main menu
- [x] Character statistics not announced on game load
- [x] Equipment slot navigation not interrupted by "RHand"
- [x] H key announces "Not in battle or no active character" when not in battle
- [x] Open main menu (X button) - verify command menu reads
- [x] Navigate Items menu - verify item names read
- [x] Navigate Equipment menu - verify slot names read
- [x] Open Status screen - verify character info reads
- [x] Navigate Abilities menu - verify ability names read
- [x] Test Config menu - verify option names read
- [x] Test Save/Load menu - verify slot names read
- [x] Battle: turn announcements working
- [x] Battle: target selection working
- [x] Battle: damage/healing announcements working
- [x] Shop: item browsing working
- [x] Field: pathfinding and navigation working
- [x] Item use: character selection reads correctly (Round 3 fix)
- [x] Status screen: arrow key navigation through stats (Up/Down/PgUp/PgDn/Home/End)

---

## Files Modified in Round 2

| File | Changes |
|------|---------|
| `Patches/MessagePatches.cs` | Added location name filter to speaker patch |
| `Menus/CharacterSelectionReader.cs` | Added MenuManager.IsOpen check |
| `Menus/MenuTextDiscovery.cs` | Removed StatusMenuTracker wrapper, added IsInEquipmentSlotContext() |
| `Core/InputManager.cs` | Removed IsInBattle() gate for H key |

---

## Reference: Key Namespaces

```
Il2CppLast.UI                    - Base UI classes
Il2CppLast.UI.KeyInput           - Keyboard/controller UI
Il2CppLast.UI.Touch              - Touch UI
Il2CppLast.Management            - Game management (MenuManager, etc.)
Il2CppSerial.FF4.UI.KeyInput     - FF4-specific keyboard UI
Il2CppSerial.Template.UI         - Template/shared UI
Il2CppSerial.Template.UI.KeyInput - Template keyboard UI
```

---

## Debug Commands

```bash
# View latest log (use cmd-compatible commands)
cmd //c "type d:\Games\SteamLibrary\steamapps\common\FINAL FANTASY IV PR\MelonLoader\Logs\Latest.log"

# Build and deploy
cmd //c "D:\Games\Dev\Unity\FFPR\FF4\ff4-screen-reader\build_and_deploy.bat"
```
