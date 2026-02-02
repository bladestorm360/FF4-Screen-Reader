# FF4 Screen Reader - Architecture

## Commands
```bash
powershell -Command "& cmd /c 'D:\Games\Dev\Unity\FFPR\FF4\ff4-screen-reader\build_and_deploy.bat'"
Glob pattern="*.log" path="D:\Games\steamlibrary\steamapps\common\final fantasy iv pr\MelonLoader\Logs"
```

## Namespaces
`Il2CppLast.UI.KeyInput` (keyboard/controller), `Il2CppLast.Management` (MenuManager), `Il2CppSerial.FF4.UI.KeyInput` (FF4-specific)

## State Management (`Core/MenuState.cs`)
12 boolean flags: `BattleState`, `ShopState`, `ItemMenuState`, `EquipmentMenuState`, `AbilityMenuState`, `ConfigMenuState`, `StatusMenuState`, `PartyMenuState`, `TitleMenuState`, `SaveLoadMenuState`, `PopupMenuState`, `NamingMenuState`

**Methods:** `ClearOtherMenuStates(except)`, `ClearAllMenuStates()` (on scene load/main menu)

**State clearing hooks:** `SetActive(false)` (menu closes), `*Init` methods (return to command bar), `SetFocus(true)` (focus returns)

## Battle Deduplication (`GlobalBattleMessageTracker`)
For two-part abilities (Pray, Steal, Flee): `RecordAction(actor, action)`, `HasRecentActionForActor(actor)`, `IsRedundantActionMessage(msg)`, `IsFleeInProgress`

## Centralized Deduplication (`Utils/AnnouncementDeduplicator.cs`)
```csharp
if (AnnouncementDeduplicator.ShouldAnnounce("Context.Name", text)) { ... }   // Text-based
if (AnnouncementDeduplicator.ShouldAnnounce("Context.Name", objectRef)) { ... } // Object-based
AnnouncementDeduplicator.Reset("Context.Name");
```
Context format: `MenuName.ElementType` (e.g., `Shop.Item`, `Battle.Turn`)

**Object-based deduplication:** Use for battle actions where different enemies with same name should both be announced. Each `BattleActData` is unique per action.

## Character Status Helper (`Utils/CharacterStatusHelper.cs`)
```csharp
CharacterStatusHelper.GetVitalsString(param);     // "HP 100/200, MP 50/100"
CharacterStatusHelper.GetStatusConditions(param); // "Poison, Blind"
CharacterStatusHelper.GetFullStatus(param);       // ", HP 100/200, MP 50/100, Poison"
```
Uses `CharacterParameterBase` for menu and battle contexts.

## Patching Rules
1. No polling - hook exact state change | 2. No timers - find precise hooks | 3. Use `AccessTools.Method()` for Il2Cpp private methods | 4. One-frame delay for UI: `yield return null`

## Bug Fixes
| Issue | Solution |
|-------|----------|
| Map name twice | `LocationMessageTracker` deduplication |
| State flags stuck | `ClearAllMenuStates()` on Show() |
| Silent command bars | Hook `*Init` methods |
| Navigation lag | State flags vs hierarchy walks |
| Entity timing | Hook `MainGame.set_FieldReady` |
| Moon exits | Step-by-step validation (MapId=3) |
| Two-part ability dupes | `GlobalBattleMessageTracker` |
| Flee menu | `IsFleeInProgress` flag |
| Timer drift | Content-based equality |
| Ability icon tags | `StripIconMarkup` on ability names |
| Same-name enemy attacks deduplicated | Object-based deduplication with `BattleActData` (2026-01-19) |
| Map transition polling | `GameStatePatches` hooks `ChangeState` (2026-01-23) |
| Wall tone lag on load | `CoroutineManager.StopManaged()` with wrapper tracking (2026-01-29) |
| Wall tones on victory | Reset battle state on scene transition, not victory screen (2026-01-29) |
| Audio keys toggle during battle | Block `;`, `'`, `0` with feedback (2026-01-30) |
| Defeat message not reading | Patch BattleCommandMessageController.SetMessage (2026-01-31) |
| Game Over popup buttons silent | Patch GameOverSelectPopup/LoadPopup.UpdateCommand (2026-01-31) |
| Lunar Whale placeholder in NPCs | Filter "魔導船" in `IsPlaceholderEntity()` (2026-02-02) |

## Item Equipment Info (I Key)
Press **I** in Items menu to announce who can equip selected item.

**Files:** `MenuState.cs` (LastSelectedItem), `ItemMenuPatches.cs`, `ItemDetailsAnnouncer.cs`, `InputManager.cs`

**APIs:** `UserDataManager.SearchOwnedItem(contentId)`, `EquipUtility.CanEquipped(OwnedItemData, jobId)` (not `JobInfomationProvider.CanEquipped`)

## Config Value Announcements
Announces config changes via any input method (keyboard/mouse/controller/touch).

**KeyInput hooks:** `SetNextSelect()`, `SetPrevSelect()`, `SetSliderValue(float)` - filter with `SelectedCommand == controller`

**Touch hooks:** `SetArrowChangeText(string)`, `SetSliderCurrentValue(float)` - first call = init, subsequent = announce

Slider values read from UI text (`sliderValueText`).

## Event-Driven Entity Refresh (2026-01-20)
Treasure chests and NPC interactions trigger immediate entity cache refresh. No timer-based polling.

**Hooks:**
| Hook | Method | Trigger |
|------|--------|---------|
| Treasure chest opened | `FieldTresureBox.Open()` | Chest interaction |
| Dialogue ends | `MessageWindowManager.Close()` | NPC interaction complete |
| Field ready | `MainGame.set_FieldReady` | Map loaded |

**Implementation:** `Core/FFIV_ScreenReaderMod.cs`
```csharp
internal void ScheduleEntityRefresh()
{
    CoroutineManager.StartManaged(EntityRefreshCoroutine());
}

private IEnumerator EntityRefreshCoroutine()
{
    yield return null;  // Wait 1 frame for game state update
    entityCache.ForceScan();
}
```

**Postfix patches:** `EntityInteractionPatches` class calls `Instance.ScheduleEntityRefresh()`

**Removed:** Timer fields from `EntityCache`, `Update()` method, `ENTITY_SCAN_INTERVAL` constant.

## Event-Driven Map Transitions (2026-01-23)
**Problem:** `CheckMapTransition()` in `OnUpdate()` used per-frame polling, violating the "no polling" rule.
**Fix:** Created `GameStatePatches` to hook `SubSceneManagerMainGame.ChangeState`. The game's state machine fires on all major transitions (field, battle, menu). When state changes to field states (`FieldReady=2`, `Player=3`, `ChangeMap=1`), map transitions are announced and battle state is cleared. Removes need for polling.

**States:**
| Value | Name | Description |
|-------|------|-------------|
| 1 | ChangeMap | Map is changing |
| 2 | FieldReady | Field map ready |
| 3 | Player | Player has control |
| 13 | Battle | In battle |

## Battle State System (Portable)

Architecture for disabling field-only features during battle. Designed for easy porting to FF1/FF3/FF5 mods.

### Core Components

**BattleState class** (`Core/MenuState.cs`):
```csharp
public static class BattleState
{
    public static bool IsInBattle => MenuStateRegistry.IsActive("Battle");

    // Pre-suppression state storage
    private static bool _preBattleWallTones, _preBattleFootsteps, _preBattleAudioBeacons;
    private static bool _hasStoredState = false;

    public static void SetActive()
    {
        // Store current navigation state
        if (!_hasStoredState && Mod.Instance != null)
        {
            _preBattleWallTones = Mod.Instance.IsWallTonesEnabled();
            _preBattleFootsteps = Mod.Instance.IsFootstepsEnabled();
            _preBattleAudioBeacons = Mod.Instance.IsAudioBeaconsEnabled();
            _hasStoredState = true;
            Mod.Instance.SuppressNavigationForBattle();
        }
        MenuStateRegistry.SetActiveExclusive("Battle");
    }

    public static void Reset()
    {
        MenuStateRegistry.SetState("Battle", false);
        // Restore navigation state
        if (_hasStoredState && Mod.Instance != null)
        {
            Mod.Instance.RestoreNavigationAfterBattle(
                _preBattleWallTones, _preBattleFootsteps, _preBattleAudioBeacons);
            _hasStoredState = false;
        }
    }
}
```

### Entry Hook (Battle Start)

Hook `BattleController.StartBattle`:
```csharp
[HarmonyPatch(typeof(Il2CppLast.Battle.BattleController), "StartBattle",
    new Type[] { typeof(Il2CppLast.Battle.InstantiateManager), typeof(bool) })]
public static class BattleController_StartBattle_Patch
{
    [HarmonyPrefix]
    public static void Prefix() => BattleState.SetActive();
}
```

### Exit Hook (Return to Field)

Hook `SubSceneManagerMainGame.ChangeState` for reliable scene transition detection:
```csharp
private const int STATE_FIELD_READY = 2;
private const int STATE_PLAYER = 3;
private const int STATE_CHANGE_MAP = 1;

public static void ChangeState_Postfix(SubSceneManagerMainGame.State state)
{
    int stateValue = (int)state;
    if (stateValue == STATE_FIELD_READY || stateValue == STATE_PLAYER || stateValue == STATE_CHANGE_MAP)
    {
        if (BattleState.IsInBattle)
            BattleState.Reset();
    }
}
```

**Why not victory screen?** Victory screen is still in Battle scene. Resetting there causes audio features to restart while battle animations play.

### Hotkey Blocking Pattern

In `InputManager.cs`, block field-only hotkeys during battle:

**Method 1: Early return with feedback** (for toggle keys):
```csharp
if (Input.GetKeyDown(KeyCode.Semicolon))  // Wall tones
{
    if (BattleState.IsInBattle)
    {
        Mod.SpeakText("Not available in battle", interrupt: true);
        return;
    }
    mod.ToggleWallTones();
}
```

**Method 2: Grouped feedback block** (for navigation keys):
```csharp
// Entity navigation keys - blocked during battle
if (BattleState.IsInBattle)
{
    if (entityKeyPressed || Input.GetKeyDown(KeyCode.Alpha0) || ...)
    {
        Mod.SpeakText("Not available in battle", interrupt: true);
    }
    return;
}
```

### Keys Blocked During Battle (FF4)

| Key | Function | Blocking Method |
|-----|----------|-----------------|
| J, [ | Cycle entities backward | Grouped feedback |
| K | Repeat current entity | Grouped feedback |
| L, ] | Cycle entities forward | Grouped feedback |
| P, \ | Pathfind to entity | Grouped feedback |
| Shift+J/L | Cycle categories | Grouped feedback |
| 0 | Dump entity names | Grouped feedback |
| Shift+K | Reset to All category | Grouped feedback |
| =, - | Cycle categories | Grouped feedback |
| ; | Toggle wall tones | Early return |
| ' | Toggle footsteps | Early return |
| 9 | Toggle audio beacons | Silent (auto-suppressed) |

### Feature Auto-Suppression

These features are automatically disabled during battle via `SuppressNavigationForBattle()`:
- Wall tone loop (stops playing, coroutine continues but skips logic)
- Footstep sounds
- Audio beacons
- Pathfinding filter

State is stored before suppression and restored by `RestoreNavigationAfterBattle()` when battle ends.

## Message Window Offsets (MessageWindowManager)
| Field | Offset |
|-------|--------|
| messageList | 0x88 |
| newPageLineList | 0xA0 |
| spekerValue | 0xA8 |
| messageLineIndex | 0xB0 |
| currentPageNumber | 0xF8 |

## Multi-Line Dialogue Support (2026-01-24)
**Problem:** Old `DialogueTracker` read from `SetContent` parameter, treating each `BaseContent` as one page. Missed multi-line pages (paragraphs).
**Fix:** Ported pointer-based approach from FF1/FF2/FF3.

**Architecture:**
```
SetContent → Read messageList + newPageLineList from instance via pointer access
SetSpeker → Store speaker in DialogueTracker.currentSpeaker
PlayingInit → Get currentPageNumber, combine lines for page, announce
```

**Key classes:**
- `MessageWindowHelper` - Pointer-based field readers (offsets 0x88, 0xA0, 0xA8, 0xF8)
- `DialogueTracker` - Stores `currentMessageList`, `currentPageBreaks`, `GetPageText()`

**How multi-line works:**
- `messageList` contains all dialogue lines (one per visual line)
- `newPageLineList` contains ending line index per page
- `GetPageText(pageIndex)` combines lines within page boundaries
- Example: `["This is", "a long", "sentence."]` with break at 2 → "This is a long sentence."

## Entity Name Translation (2026-01-28)
Translates Japanese entity names to English via JSON dictionary. Ported from FF3.

**Files:**
- `Utils/EntityTranslator.cs` - Translation logic, JSON parsing, dump functionality
- `Field/NavigableEntity.cs:41` - Name property passes through `EntityTranslator.Translate()`

**Translation lookup (3-tier):**
1. Exact match in dictionary
2. Strip numeric/SC prefix (e.g., "6:" or "SC01:"), lookup base name, prepend prefix to result
3. Return original (track untranslated Japanese names per map)

**Hotkeys:**
| Key | Action |
|-----|--------|
| `0` | Dump untranslated entity names for current map to `EntityNames.json` |
| `Shift+K` | Reset to All category (moved from `0`) |

**File paths:**
- Dictionary: `UserData/FFIV_ScreenReader/FF4_translations.json`
- Dump output: `UserData/FFIV_ScreenReader/EntityNames.json`

**JSON format (FF4_translations.json):**
```json
{
  "Japanese Name": "English Translation",
  "もう一つ": "Another One"
}
```

**Dump format (EntityNames.json):**
```json
{
  "Map Name": {
    "Japanese Entity 1": "",
    "Japanese Entity 2": ""
  }
}
```

## Sound System Architecture (2026-01-29)

**16-bit Audio:**
- WAVEFORMATEX: nAvgBytesPerSec=88200, nBlockAlign=4, wBitsPerSample=16
- Buffer allocation: 32KB per channel
- Eliminates quantization noise/static at low volumes

**Volume Control System:**
```csharp
// Preferences (0-100, default 50)
prefWallBumpVolume, prefFootstepVolume, prefWallToneVolume, prefBeaconVolume

// Accessors (used by SoundPlayer)
FFIV_ScreenReaderMod.WallBumpVolume  // returns 0-100

// Setters (used by ModMenu)
FFIV_ScreenReaderMod.SetWallBumpVolume(value)  // clamps 0-100, saves prefs
```

**Wall Tone Volume Baking:**
Volume is applied during tone generation (`GenerateStereoToneSustainWithVolume`) instead of post-scaling. This preserves dynamic range at low volumes, avoiding quantization distortion.

**ModMenu (F8):**
- Audio-only virtual menu with Windows API input (GetAsyncKeyState)
- Works even when game doesn't have focus
- Focus blocker window steals game focus while open
- Navigation: Up/Down arrows, Left/Right adjust values, Enter/Space toggle

**IL2CPP-Compatible Loops:**
```csharp
// Manual time-based waiting instead of WaitForSeconds
float nextCheckTime = Time.time + 0.3f;
while (enableWallTones)
{
    if (Time.time < nextCheckTime) { yield return null; continue; }
    nextCheckTime = Time.time + WALL_TONE_LOOP_INTERVAL;
    // ... logic ...
}
```

**CoroutineManager (StopManaged Fix):**
```csharp
// Problem: Start used manager, stop bypassed it
CoroutineManager.StartManaged(wallToneCoroutine);  // Wraps coroutine
MelonCoroutines.Stop(wallToneCoroutine);           // WRONG: stops original, not wrapper

// Fix: StopManaged() with wrapper tracking
private static Dictionary<IEnumerator, IEnumerator> originalToWrapper;
private static Dictionary<IEnumerator, IEnumerator> wrapperToOriginal;

public static void StopManaged(IEnumerator original)
{
    if (originalToWrapper.TryGetValue(original, out var wrapper))
    {
        MelonCoroutines.Stop(wrapper);  // Stops the actual running wrapper
        // Cleanup dictionaries...
    }
}
```
- `maxConcurrentCoroutines`: 3 → 20 (prevents premature eviction)
- Scene load: `GameObjectCache.ClearAll()` before audio restart (clears stale refs)

**Pre-cached Direction Vectors:**
```csharp
private static readonly Vector3 DirNorth = new Vector3(0, 16, 0);
// Avoids per-cycle Vector3 allocations in wall tone loop
```

**Files:**
| File | Purpose |
|------|---------|
| `Utils/SoundPlayer.cs` | 16-bit audio, volume scaling, tone generation |
| `Core/ModMenu.cs` | Audio settings menu with Windows API input |
| `Core/FFIV_ScreenReaderMod.cs` | Volume preferences, loop implementations |
| `Core/InputManager.cs` | F8 hotkey for ModMenu |

## Vehicle Name Enhancement (2026-01-29)

**Problem:** Vehicles with same `TransportationType` (type 8 = SpecialPlane) announced as "Special Airship" instead of specific names like "Falcon" or "Lunar Whale".

**Solution:** Use `TransportationInfo.MessageId` to get localized vehicle names via `MessageManager.GetMessage()`.

**VehicleTypeMap:**
```csharp
// Was: Dictionary<FieldEntity, int>
// Now: Dictionary<FieldEntity, (int Type, string MessageId)>
VehicleTypeMap[mapObject] = (transportType, messageId);
```

**GetVehicleName() Resolution Order:**
1. Try `MessageManager.Instance.GetMessage(messageId)` for specific name
2. Fall back to type-based generic name if MessageId empty or lookup fails

**TransportationType Fallbacks:**
| Type | Name |
|------|------|
| 2 | Ship |
| 3 | Enterprise |
| 6 | Submarine |
| 7 | Hovercraft |
| 8 | Special Airship |
| 9 | Yellow Chocobo |
| 10 | Black Chocobo |
| 11 | Boko |
| 12 | Magical Armor |

**Debug Logging:** `[Vehicle Debug]` messages show Transport ID, Type, Enable, and MessageId when scanning vehicles.

**Files:** `Field/FieldNavigationHelper.cs`, `Field/NavigableEntity.cs`, `Field/EntityFactory.cs`

## Performance Optimizations (2026-01-29)

**Memory Leak Fixes:**
| Issue | Solution |
|-------|----------|
| Event handler leak | Store delegate in `_onSceneLoadedHandler` field for proper unsubscription |
| AnnouncementDeduplicator unbounded growth | `PruneIfNeeded()` clears cache when > 100 entries |
| ConfigMenuPatches dictionary leak | `ConditionalWeakTable` for Touch controller tracking |

**Performance Optimizations:**
| Issue | Solution |
|-------|----------|
| EntityNavigator sorts on every cycle | `_navigationListDirty` flag skips sort if no entities added/removed |
| GroupEntity 5x GetRepresentative per announcement | Per-frame caching (`_lastCacheFrame`, `_cachedRepresentative`) |
| IsScreenFading polling overhead | 0.1s throttling (`_lastFadeCheckTime`, `FADE_CHECK_INTERVAL`) |
| SoundPlayer tone regeneration | `_toneCache` dictionary (max 16 entries) for wall tone buffers |

**ConditionalWeakTable Pattern:**
```csharp
// StringHolder wrapper (ConditionalWeakTable requires reference type value)
internal class StringHolder { public string Value; }

// Weak reference prevents memory leak when controller is destroyed
private static readonly ConditionalWeakTable<Controller, StringHolder> lastValues = new();

// Usage
if (lastValues.TryGetValue(controller, out StringHolder holder))
    holder.Value = newValue;  // Update existing
else
    lastValues.Add(controller, new StringHolder(value));  // First time
```

**Cache Pruning Strategy:**
Simple clear-on-overflow: When cache exceeds max size, clear all entries. Context keys are reused frequently so entries are quickly repopulated. Avoids complexity of LRU tracking.

**Exception: FootstepPatches WaitForSeconds:**
`FootstepPatches.cs` uses `WaitForSeconds(0.1f)` as a buffer between tile-based footstep triggers. This is acceptable per CLAUDE.md because it hooks the game's own timing system (FieldPlayer.walkMode change) rather than polling.

## Game Over Popup Architecture (2026-01-31)

**Flow:** Party wipe → "The party was defeated" → Game Over popup → Load/Title choice → (if Load) "Start from recent save?" → Yes/No

**Classes (KeyInput namespace, lines 459177-459815 in dump.cs):**
| Class | Purpose | Key Fields |
|-------|---------|------------|
| GameOverSelectPopup | Load/Title selection | selectCursor (0x38), commandList (0x40) |
| GameOverLoadPopup | Yes/No confirmation | messageText (0x40), selectCursor (0x58), commandList (0x60) |
| GameOverPopupController | Orchestrates flow | view (0x30) |
| GameOverPopupView | Container | loadPopup (0x18) |

**Patches:**
| Hook | Method | Purpose |
|------|--------|---------|
| Defeat message | BattleCommandMessageController.SetMessage | Announce "The party was defeated" |
| Load/Title buttons | GameOverSelectPopup.UpdateCommand | Announce button text on navigation |
| Yes/No buttons | GameOverLoadPopup.UpdateCommand | Announce button text on navigation |
| Confirmation popup | GameOverPopupController.InitSaveLoadPopup | Read and announce popup message |

**Pointer Navigation (InitSaveLoadPopup → message text):**
```
controller (GameOverPopupController)
    ↓ +0x30
view (GameOverPopupView)
    ↓ +0x18
loadPopup (GameOverLoadPopup)
    ↓ +0x40
messageText (Text) → read via ReadTextFromPointer
```

## Compilation Notes
`param.Level` → `param.ConfirmedLevel()` | `FirstSlotSelect` → `SlotSelect` | Private methods → `AccessTools.Method()`
