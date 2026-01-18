# FF4 Screen Reader - Debug Log

## Status: FUNCTIONAL

**Build:** Successful (0 warnings, 0 errors)
**Deployment:** Successful
**Runtime:** Fully functional

## Feature Completion

| Feature | Status |
|---------|--------|
| Field Navigation & Pathfinding | ✅ |
| Menu System | ✅ |
| Battle System | ✅ |
| Shops | ✅ |
| Victory Screen | ✅ |
| Vehicles | ✅ |
| Status Screen Navigation | ✅ |
| Wall Bump Detection | ✅ |
| Story Event Text (LineFade) | ✅ |
| Per-Page Dialogue Reading | ✅ |
| Map Name Deduplication | ✅ |
| Moon Pathfinding | ✅ |
| Popup/Confirmation Dialogs | ✅ |
| Save/Load Confirmation Popups | ✅ |
| Save Slot Navigation | ✅ |
| Title Screen "Press any button" | ✅ |

---

## Bug Fixes

### Map Name Duplication
**Problem:** "Entering Mysidia. Mysidia." / Location spoken when opening menu
**Solution:** Content-based deduplication - SystemMessage skipped if text contained in recent FadeMessage
**Files:** `MessagePatches.cs`, `FFIV_ScreenReaderMod.cs`

### Stale messageLineIndex
**Problem:** Multi-line dialogue skipped, speaker changes missed
**Cause:** Game's `messageLineIndex` doesn't reset on new `SetContent`
**Solution:** Track own `nextAnnouncementIndex`, reset on `StoreContent`
**File:** `MessagePatches.cs`

### Item Target Selection
**Problem:** Character name/HP/MP not announced when selecting item use targets
**Solution:** Added `ItemUseController.SelectContent` patch
**File:** `ItemMenuPatches.cs`

### "Potion" Interruption
**Problem:** Items interrupted by generic cursor patch announcing "Potion"
**Solution:** Added skip conditions to `SkipNextIndex`/`SkipPrevIndex` for all menu contexts
**File:** `CursorNavigationPatches.cs`

### Vehicle Announcements
**Problem:** Boarding/disembarking vehicles not reliably announced
**Cause:** `FieldPlayer.GetOff()` not called by game in many scenarios; `ChangeMoveState` unreliable
**Solution:** Hook `FieldController.ChangeTransportation(int transportationId, ...)` as primary detection:
- Fires for ALL transportation changes (most reliable)
- Track previous transportationId to detect transitions
- Only announce "on foot" when transitioning to TRANSPORT_PLAYER (1)
- Skip intermediate states (TRANSPORT_CONTENT, TRANSPORT_SYMBOL) used during cinematics
- Interior maps (non-world) automatically set state to on-foot via `OnMapTransition()`
- Vehicle state announcements use `interrupt: false` to not interrupt location announcements
**Files:** `MovementSpeechPatches.cs`, `MoveStateHelper.cs`, `FFIV_ScreenReaderMod.cs`

### Location as Speaker
**Problem:** "Castle Baron – 1F" announced as speaker name
**Solution:** Filter speaker names containing "–" or "-"
**File:** `MessagePatches.cs`

### Stats on Game Load
**Problem:** Character vitals announced during scene preload
**Solution:** Check `MenuManager.Instance.IsOpen` before reading
**File:** `CharacterSelectionReader.cs`

### Equipment Slot Interruption
**Problem:** Slot announcement interrupted by "RHand"
**Solution:** Skip fallback strategies when `IsInEquipmentSlotContext()`
**File:** `MenuTextDiscovery.cs`

### H Key Outside Battle
**Problem:** Silent when pressing H outside battle
**Solution:** Removed `IsInBattle()` gate, let method announce "Not in battle"
**File:** `InputManager.cs`

### Moon Pathfinding
**Problem:** Paths found to unreachable destinations across one-way ledges
**Solution:** Reverse-path validation for MapId=3 - if dest→player fails, reject
**File:** `FieldNavigationHelper.cs`

### Popup Dialogs Not Reading
**Problem:** Confirmation popups (save/load, return to title, etc.) not announced
**Solution:** Ported popup handling from FF3:
- `PopupPatches.cs` - Hooks base `Popup.Open()`/`Close()` for CommonPopup, GameOverSelectPopup, InfomationPopup
- `SaveLoadPatches.cs` - Hooks `SetPopupActive(bool)` on LoadGameWindowController, LoadWindowController, SaveWindowController and `SetEnablePopup(bool)` on InterruptionWindowController
- Title screen "Press any button" via SplashController.InitializeTitle + TitleWindowController.SetEnableStartObject
- CursorNavigationPatches checks `PopupState.ShouldSuppress()` for button navigation
**Files:** `PopupPatches.cs`, `SaveLoadPatches.cs`, `CursorNavigationPatches.cs`, `FFIV_ScreenReaderMod.cs`

### Save Slot Navigation
**Problem:** Save slots only reading "Moon" (location) instead of full slot info
**Solution:** Added `SaveListController.SelectContent` patch in `SaveLoadPatches.cs`:
- Reads `SaveContentView` fields via memory offsets (slotName, date/time, location, character, level, playtime)
- Format matches visual display: "File 2, 01/17/2026 8:10, Moon, Edge Level 45, Time 13:06"
- Empty slots: "File 3, Empty"
- `SaveLoadMenuState.IsActive` suppresses `MenuTextDiscovery` when in save/load menu
- State cleared on scene change (backing out to title/main menu)
**File:** `SaveLoadPatches.cs`

### "Empty" Announced on Splash Screen
**Problem:** "Empty" spoken when title screen loads (before user opens Load Game)
**Cause:** `SaveListController.SelectContent` called during initialization when window not visible
**Solution:** Added visibility checks - only announce if `controller.gameObject.activeInHierarchy` and `cursor.gameObject.activeInHierarchy`
**File:** `SaveLoadPatches.cs`

### Entity Scan Timing Issues
**Problem:** Entity cache scan used arbitrary 0.5s delay after scene/map load
**Cause:** `DelayedInitialScan()` and `DelayedMapTransitionScan()` used hardcoded timers that could fail on slower PCs
**Solution:** Hook `MainGame.set_FieldReady(bool value)` - the game's internal signal that entities are instantiated:
- Removed timer-based coroutines entirely
- `set_FieldReady(true)` triggers `EntityCache.ForceScan()` automatically
- Entities available immediately when user presses `[`/`]` keys
- 5-second periodic rescan continues for cache maintenance
**Files:** `MovementSpeechPatches.cs`, `FFIV_ScreenReaderMod.cs`

---

## Compilation Fixes

| File | Change |
|------|--------|
| `StatusDetailsReader.cs` | `param.Level` → `param.ConfirmedLevel()` |
| `PartySettingPatches.cs` | `FirstSlotSelect` → `SlotSelect`, etc. |
| `EntityFactory.cs` | Removed non-existent ObjectTypes |
| `BattleMessagePatches.cs` | Removed `SetSpeaker`, `SetCommandText` patches |
| `AbilityMenuPatches.cs` | Added FF4 namespace, removed `AbilityChangeController` |

---

## Debug Commands

**View latest log:**
```
Read file_path="D:\Games\steamlibrary\steamapps\common\final fantasy iv pr\MelonLoader\Logs\Latest.log"
```

**Find logs by timestamp:**
```
Glob pattern="*.log" path="D:\Games\steamlibrary\steamapps\common\final fantasy iv pr\MelonLoader\Logs"
```

**Build and deploy:**
```
powershell -Command "& cmd /c 'D:\Games\Dev\Unity\FFPR\FF4\ff4-screen-reader\build_and_deploy.bat'"
```

---

## Key Namespaces

```
Il2CppLast.UI                    - Base UI classes
Il2CppLast.UI.KeyInput           - Keyboard/controller UI
Il2CppLast.Management            - MenuManager, etc.
Il2CppSerial.FF4.UI.KeyInput     - FF4-specific UI
Il2CppSerial.Template.UI.KeyInput - Template UI
```
