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
if (AnnouncementDeduplicator.ShouldAnnounce("Context.Name", text)) { ... }
AnnouncementDeduplicator.Reset("Context.Name");
```
Context format: `MenuName.ElementType` (e.g., `Shop.Item`, `Battle.Turn`)

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

## Item Equipment Info (I Key)
Press **I** in Items menu to announce who can equip selected item.

**Files:** `MenuState.cs` (LastSelectedItem), `ItemMenuPatches.cs`, `ItemDetailsAnnouncer.cs`, `InputManager.cs`

**APIs:** `UserDataManager.SearchOwnedItem(contentId)`, `EquipUtility.CanEquipped(OwnedItemData, jobId)` (not `JobInfomationProvider.CanEquipped`)

## Config Value Announcements
Announces config changes via any input method (keyboard/mouse/controller/touch).

**KeyInput hooks:** `SetNextSelect()`, `SetPrevSelect()`, `SetSliderValue(float)` - filter with `SelectedCommand == controller`

**Touch hooks:** `SetArrowChangeText(string)`, `SetSliderCurrentValue(float)` - first call = init, subsequent = announce

Slider values read from UI text (`sliderValueText`).

## Compilation Notes
`param.Level` → `param.ConfirmedLevel()` | `FirstSlotSelect` → `SlotSelect` | Private methods → `AccessTools.Method()`
