# FF4 Screen Reader - Architecture

## Commands
```bash
powershell -Command "& cmd /c 'D:\Games\Dev\Unity\FFPR\FF4\ff4-screen-reader\build_and_deploy.bat'"
Glob pattern="*.log" path="D:\Games\steamlibrary\steamapps\common\final fantasy iv pr\MelonLoader\Logs"
```

## Namespaces
`Il2CppLast.UI.KeyInput` (keyboard/controller), `Il2CppLast.Management` (MenuManager), `Il2CppSerial.FF4.UI.KeyInput` (FF4-specific)

## Core Patterns

### State Management (`Core/MenuState.cs`)
`MenuStateRegistry` manages named boolean states. `SimpleMenuState` (reusable) for Ability/Config/Status/Party/Title. Custom classes for Battle/Shop/Item/Equipment (unique logic).
- `MenuStates.Ability.SetActive()`, `MenuStateRegistry.IsAnyActive()` for suppression
- `ClearAllMenuStates()` on scene load
- Hooks: `SetActive(false)`, `*Init` methods, `SetFocus(true)`

### Deduplication (`Utils/AnnouncementDeduplicator.cs`)
```csharp
if (AnnouncementDeduplicator.ShouldAnnounce("Context.Name", text)) { ... }
AnnouncementDeduplicator.Reset("Context.Name");
```
Use object-based for battle (different enemies with same name). Context format: `MenuName.ElementType`

### Character Status (`Utils/CharacterStatusHelper.cs`)
```csharp
CharacterStatusHelper.GetVitalsString(param);     // "HP 100/200, MP 50/100"
CharacterStatusHelper.GetStatusConditions(param); // "Poison, Blind"
```

### Battle Message Tracker (`GlobalBattleMessageTracker`)
For two-part abilities: `RecordAction()`, `HasRecentActionForActor()`, `IsRedundantActionMessage()`, `IsFleeInProgress`

## Patching Rules
1. No polling - hook exact state change | 2. No timers - find precise hooks | 3. `AccessTools.Method()` for private | 4. One-frame delay: `yield return null`

## Bug Fixes
| Issue | Solution |
|-------|----------|
| Map name twice | `MapNameResolver.GetCurrentMapName()` check in SystemMessage patch |
| State flags stuck | `ClearAllMenuStates()` on Show() |
| Silent command bars | Hook `*Init` methods |
| Entity timing | Hook `MainGame.set_FieldReady` |
| Moon exits | Step-by-step validation (MapId=3) |
| Two-part ability dupes | `GlobalBattleMessageTracker` |
| Same-name enemy attacks | Object-based deduplication with `BattleActData` |
| Map transition polling | `GameStatePatches` hooks `ChangeState` |
| Wall tones on victory | Reset battle state on scene transition only |
| Defeat message silent | Patch `BattleCommandMessageController.SetMessage` |
| Game Over popup silent | Patch `GameOverSelectPopup/LoadPopup.UpdateCommand` |
| Title "Press any button" silent | Patch `SystemIndicator.Hide` with guard flag (not private `SetEnableStartObject`) |

## System Architecture

### Event-Driven Entity Refresh
| Hook | Trigger |
|------|---------|
| `FieldTresureBox.Open()` | Chest opened |
| `MessageWindowManager.Close()` | Dialogue ends |
| `MainGame.set_FieldReady` | Map loaded |

Pattern: `ScheduleEntityRefresh()` → one-frame delay → `ForceScan()`

### Map Transitions (`GameStatePatches`)
Hook `SubSceneManagerMainGame.ChangeState`. States: ChangeMap=1, FieldReady=2, Player=3, Battle=13

### Battle State System
**Entry:** `BattleController.StartBattle` → `BattleState.SetActive()` (stores nav state, suppresses features)
**Exit:** `ChangeState` to field states → `BattleState.Reset()` (restores nav state)
**Not victory screen** - still Battle scene, audio would restart early.

Blocked keys during battle: J/K/L/P/;/'/9 and category keys. Returns "Not available in battle".

### Multi-Line Dialogue
Pointer-based: `SetContent` reads messageList (0x88) + newPageLineList (0xA0), `PlayingInit` announces combined page text.

### Entity Filter System (`Core/Filters/`)
`BaseEntityFilter` provides `IsEnabled` property with change detection + virtual `OnEnabled`/`OnDisabled` hooks.
Concrete filters: `CategoryFilter` (OnAdd), `PathfindingFilter` (OnCycle), `ToLayerFilter` (OnAdd).

### Layer Transition Filter (`Core/Filters/ToLayerFilter.cs`)
OnAdd filter that hides `ToLayer` entities (e.g., underworld entrance). Checks `EventEntity.EventType == MapConstants.ObjectType.ToLayer`.
Default: disabled (entities shown). Toggle: Ctrl+\ or ModMenu. Pref: `ToLayerFilter`.

### Entity Classes (`Field/`)
Base class `NavigableEntity` in `NavigableEntity.cs` with `EntityTypeName` public accessor.
Subclasses: `TreasureChestEntity`, `MapExitEntity`, `SavePointEntity`, `DoorTriggerEntity`, `EventEntity` (in NavigableEntity.cs), `NPCEntity` (NPCEntity.cs), `VehicleEntity` (VehicleEntity.cs), `WaypointEntity` (WaypointEntity.cs), `GroupEntity` (GroupEntity.cs).

### Entity Translation (`Utils/EntityTranslator.cs`)
4-tier lookup: exact match → strip prefix + lookup + reattach → strip suffix + lookup + reattach → prefix+suffix combo. All translations embedded at compile time.

### Facade Architecture (Phase 7 refactor)
`FFIV_ScreenReaderMod` (~362 lines) delegates to facades:
- `EntityNavigationFacade` - Entity cycling, categories, filters, teleport
- `WaypointFacade` - Waypoint CRUD, cycling, pathfinding
- `NavigationStateManager` - Battle/dialogue audio suppression
- `GameAnnouncementHelper` - Character status, gil, map announcements (static)
- `AudioFeedbackManager` - Wall tones, footsteps, beacons, volume prefs

`InputManager` holds facade references directly (not the mod class).

### Audio Feedback (`Core/AudioFeedbackManager.cs`)
Manages wall tones, footsteps, audio beacons, volume preferences, and battle/dialogue suppression.
Extracted from main mod class. Dependencies: `EntityNavigator` (beacon targeting), `EntityCache` (wall tone map exits).

### Sound System (`Utils/SoundPlayer.cs`)
16-bit audio (32KB buffers), volume-baked tone generation, shared `WriteWavHeader()` helper, IL2CPP-safe loops (`Time.time` vs `WaitForSeconds`).
`CoroutineManager.StopManaged()` with wrapper tracking. Max 20 concurrent.

### Vehicle Names
`TransportationInfo.MessageId` → `MessageManager.GetMessage()` for specific names. Falls back to type-based generic.

### Waypoint System
User-defined map markers independent of entity scanner. Ported from FF5.

**Architecture:**
- `WaypointEntity` - Standalone class (not NavigableEntity), has own category system
- `WaypointManager` - CRUD operations, Newtonsoft.Json persistence to `UserData/waypoints.json`
- `WaypointNavigator` - Cycling, category filtering, distance sorting
- `TextInputWindow` / `ConfirmationDialog` - Windows API focus stealing for modal dialogs

**Categories:** All, Docks, Landmarks, Airship Landings, Miscellaneous

**Key Files:**
| File | Purpose |
|------|---------|
| `Core/WaypointManager.cs` | CRUD + JSON serialization |
| `Core/WaypointNavigator.cs` | Cycling + category filtering |
| `Field/WaypointEntity.cs` | Data model + formatting |
| `Core/TextInputWindow.cs` | Text input with focus stealing |
| `Core/ConfirmationDialog.cs` | Yes/No confirmation dialogs |
| `Utils/CollectionHelper.cs` | Distance sorting utilities |
| `Utils/PlayerPositionHelper.cs` | Player position retrieval |

**Dialog Input Flow:** `InputManager.Update()` checks dialogs first (before `Input.anyKeyDown` early exit) since they use Windows API polling.

**Dialog Close Pattern:** Uses `CloseWithDelayedAnnouncement()` to restore focus first, then announce after 0.3s delay (lets NVDA finish window title), then invoke callback after 0.15s pause. Prevents speech interruption from window focus change.

### Game Over Popup
Flow: Defeat message → Load/Title → Yes/No confirmation
Patches: `BattleCommandMessageController.SetMessage`, `GameOverSelectPopup.UpdateCommand`, `GameOverLoadPopup.UpdateCommand`

### Title Screen
Two-phase approach: `SplashController.InitializeTitle` captures text + sets `isTitleScreenTextPending`, `SystemIndicator.Hide` announces when loading completes.
Guard flag prevents false triggers from other loading sequences. Runtime assembly lookup for internal `Il2CppLast.Systems.Indicator.SystemIndicator` class.

## Reference

### Message Window Offsets
messageList=0x88, newPageLineList=0xA0, spekerValue=0xA8, messageLineIndex=0xB0, currentPageNumber=0xF8

### Game Over Offsets
GameOverSelectPopup: selectCursor=0x38, commandList=0x40
GameOverLoadPopup: messageText=0x40, selectCursor=0x58, commandList=0x60
GameOverPopupController: view=0x30 | GameOverPopupView: loadPopup=0x18

### Save/Load Popup Button Navigation
`SavePopup.UpdateCommand` patch reads cursor index from `selectCursor` (0x58), deduplicates via `AnnouncementDeduplicator.ShouldAnnounce("SaveLoadPopupButton", index)`, reads button text from `commandList` (0x60) → `CommonCommand.text` (0x18). Single patch covers ALL save/load popups since all controllers use the same `SavePopup` class. `CursorNavigationPatches` has early `SaveLoadMenuState.IsActive` return before `PopupState.ShouldSuppress()` to prevent generic popup system from double-reading buttons.

### Utility Classes

| File | Purpose |
|------|---------|
| `Utils/PatchHelper.cs` | Shared `FindType()` and `TryPatchPostfix()` for Harmony patch boilerplate |
| `Core/Constants.cs` | Shared magic numbers (CellSize, SampleRate, WavHeaderSize) |
| `Utils/TextUtils.cs` | `NormalizeWhitespace()`, `StripRichTextTags()` |
| `Utils/MessageHelper.cs` | `GetLocalizedMessage()` for common MessageManager lookup pattern |

### Compilation Notes
`param.Level` → `param.ConfirmedLevel()` | `FirstSlotSelect` → `SlotSelect` | Private → `AccessTools.Method()`
