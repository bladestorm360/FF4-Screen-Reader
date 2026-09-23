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
- `MenuStates.Ability.SetActive()`, `MenuStateRegistry.AnyActive()` for suppression
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
| Corps ordering mismatch | `GetCorpsListCloneWithApparentOrder()` for display-order row info |
| Overwrite popup buttons silent | The overwrite confirmation is a KeyInput `SavePopup`, not a CommonPopup. The main-menu save's `PopupUpdate` calls `SavePopup.UpdateSelect` directly, so the old `SavePopup.UpdateCommand` hook never saw it. Buttons are now read by the `SavePopup.SetCommandSelectCursor` postfix (2026-09-23, session 2) |
| Save overwrite silent | Open read (title + message + focused button) from `SetPopupActive(true)` / `OverwriteConfirmInit`; Yes/No moves from `SavePopup.SetCommandSelectCursor` |
| Cure wrong target | `targetContents` param (display order) instead of `contentList` (data order) |
| Opening map silent | Known issue — first-run announce causes "map 0" bugs, reverted |
| Stale entities after events | Delta scan on every navigation key (`EnsureFieldContextAndScan` → `RefreshIfNeeded`) |
| Wall tones / beacons / wall bumps silent after first dialogue | `MessageWindowManager.Close` postfix → `DialogueTracker.Reset()` (the hook was lost when `EntityInteractionPatches` was deleted) |
| Controller never interrupted speech | `SpeakText("")` is dropped by `TolkWrapper`; use `FFIV_ScreenReaderMod.InterruptSpeech()` (`Tolk.Silence`) |
| Wall tones on victory | Reset battle state on scene transition only |
| Defeat message silent | Patch `BattleCommandMessageController.SetMessage` |
| Game Over popup silent | Patch `GameOverSelectPopup/LoadPopup.UpdateCommand` |
| Title "Press any button" silent | Patch `SystemIndicator.Hide` with guard flag (not private `SetEnableStartObject`) |

## System Architecture

### Entity Refresh
| Trigger | Action |
|---------|--------|
| Any entity navigation key | `EnsureFieldContextAndScan` → `EntityNavigator.RefreshIfNeeded` → delta `Scan()` (adds/removes, prunes deactivated objects) |
| `MainGame.set_FieldReady` | `ForceScan()` (map loaded) |
| Map change (`GameStatePatches`) | `ForceEntityRescan()` |
| ` (backtick) | Manual rescan, speaks "Entity scan complete" |

Chest opened/unopened is read live from `FieldTresureBox.isOpen`, so no chest hook is needed. `MessageWindowManager.Close` only resets `DialogueTracker` (dialogue end).

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

**Offline extraction (2026-09-23).** `tools/extract_entities.py` (Python + UnityPy, generalised from the FF5 mod's tool) sweeps every `map_*_assets_all_*.bundle` under `StreamingAssets/aa/StandaloneWindows64`, walks the Tiled entity JSON (`entity_default` + base64 `inline` groups in each map's `package`), and runs each Japanese label through a Python mirror of the 4-tier lookup above, so `missing` lists only what the mod would really fail on, keyed on the most-reduced form. `gamedict` builds Japanese → {lang} from the game's message tables (`story_cha` speaker names + `system`), so proper nouns use the game's own localisation. `tools/apply_translations.py` validates a batch (11 languages, per-language script checks, no kana) and appends it textually, leaving existing bytes untouched. Result: 192 keys added (559 → 751); the sweep reports 660 of 660 unique labels covered. An official-name pass aligned 82 existing entries (451 values) that are themselves game strings with the game's text — e.g. ドグ/マグ/ラグ → Sandy/Cindy/Mindy (Dug/Mug/Rug in German), フースーヤ → FuSoYa, バロン王 → King of Baron, and PR item names (Bacchus's Cider, Siren, Elven Bow). Deliberately left alone: shop words, 回復, でぶチョコボ (official text is a speaker label with a colon), 幻獣神, 四天王, ギル, and German メーガス三姉妹. Re-run after a game update: `extract_entities.py missing <out>` → translate → `apply_translations.py apply <batch>`.

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

### EXP Counter Sound (battle results)
Rapid ticking beep while the EXP tally animates on the victory screen. Toggle `ExpCounter` (default **true**) + `ExpCounterVolume` (50) in `PreferencesManager`; "Battle Results" section in `ModMenu`. Ported from FF5.
- **Audio:** `SoundPlayer.PlayExpCounter/TopUpExpCounter/StopExpCounter` on a dedicated `AudioEngine.Stream.Counter`. Beep = `ToneGenerator.GenerateLandingPing` (2000 Hz, 50 ms beep + 50 ms silence) fed to a looping SDL stream, topped up each 100 ms tick.
- **Hooks (`Patches/BattleResultPatches.cs`):** START on `ResultMenuController.ShowPointsInit` when `ExpCounterEnabled && data.GetExp>0`. Completion via `MonitorExpCounterAnimation` coroutine walking the KeyInput result graph (identical offsets to FF5): `instance +0x20 pointController → +0x30 characterListConteroller → +0x20 contentList (count @+0x18); perormanceEndCount @+0x30`; done when `perormanceEndCount >= contentList.Count`. STOP safety nets on `ShowStatusUpInit` / `ShowGetAbilitysInit` / `ShowGetItemsInit` / `EndWaitInit` (guaranteed backstop — results always dismissed through `EndWaitInit`). `BattleResultState.ExpCounterPlaying` guards the single-fire stop. Toggle OFF = stream never touched.
- FF4 has fixed jobs / no ABP, so FF5's `JobExp`/ABP branches were dropped — character EXP only.
- `ShowLevelUpAbilitysInit` is also a stop safety net (2026-09-23).

### Phased Victory Screen (`Patches/BattleResultPatches.cs`, 2026-09-23)
Each result page announces its own content when it is shown (KeyInput `ResultMenuController`, `targetData` @0x58):
`ShowPointsInit` → "Gained {0} gil" + "{0} gained {1} XP" per character (and starts the EXP counter) · `ShowStatusUpInit` → "{0} leveled up to level {1}" + stat gains (HP, MP, Strength, Agility, Stamina, Intellect, Spirit; `Base*` diff with `Confirmed*()` fallback) · `ShowGetItemsInit` → "Found {0}" / "Found {0} x{1}" · `ShowGetAbilitysInit` → "{0} learned {1}" from `BattleResultCharacterData.LearningList` (`ResultSkillController.Show` → `SetLearningList`; `ShowLevelUpAbilitys` is the ability-level page FF4 doesn't use). Each *Init can re-fire while its page is up, so `BattleResultState` one-shots (`PointsAnnounced`, `ItemsAnnounced`, `AbilitiesAnnounced`, per-character `AnnouncedLevelUps`) guard them; `BattleResultState.ResetState()` clears them at `BattleController.StartBattle`, and every phase postfix first calls `ResetIfNewResult`, which clears them whenever `targetData`'s pointer differs from the last one seen (so a battle that skips that `StartBattle` overload still gets its results read). All speech is queued (`interrupt: false`) so the last battle message isn't cut off.

### FF1 Parity Pass (2026-09-23)
- **Dialogue lifecycle:** `MessageWindowManager.Close` postfix → `DialogueTracker.Reset()` (restores navigation audio). `DialogueTracker.RepeatLastDialogue()` backs R and controller mod mode + X (dialogue takes precedence over battle/field in mod mode).
- **Dedup resets:** `BattleController.StartBattle` prefix resets turn/command/item/ability/condition/set-command-message contexts, `BattleCommandMessageManualPatches.ResetState()` and `BattleResultState`. `SetCommandSelectTarget` prefix resets the command context (first command queues behind "X's turn"); `CreateActFunction` resets the turn context when the announced turn-holder's action executes (same actor twice). `ShowUseSelect` prefixes reset the battle item/ability contexts. Menu entry resets: title Init hooks (`TITLE_MENU_COMMAND`, now an `AnnouncementDeduplicator` context), `ItemListController.*SelectInit` (`ITEM_LIST`), `ItemUseController.Single/AllInit` (`ITEM_USE_TARGET`), `AbilityWindowController.CommandInit/UseListInit/UseTargetInit`, `EquipmentWindowController.InfoInit/SelectInit`.
- **Focus on entry / back-out (no per-frame hooks):** pattern = Init **prefix** resets the guard (so a SelectContent inside the Init body still speaks) + **postfix** one-frame deferred read through the same deduplicated announcer (so the two paths never double up). Field menu: `MainMenuController.Show` + `InitNone` with a generation latch → `MenuTextDiscovery.WaitAndReadCursor(commandMenuController.selectCursor)`. Title: `TitleWindowController.InitSelect/InitializeOption/InitializeExtra`. Command bars: `ItemWindowController.CommandSelectInit`, `EquipmentWindowController.CommandInit` (generic reader on the bar's `selectCursor`), `AbilityWindowController.CommandInit` (`AbilityCommandController_SelectContent_Patch.Announce`). FF1's per-frame `UpdateController` hooks were not ported (Rule 2).
- **Details / usable-by:** I = description (`InputManager.HandleItemDetailsKey`: shop → equipment → spell → item → battle list → config tooltip; descriptions cached as they are announced). U = who can equip (`Menus/UsableByAnnouncer.cs`: item menu via `EquipUtility.CanEquipped(OwnedItemData, jobId)`; shop via Content master name lookup → Weapon/Armor `EquipJobGroupId` → `EquipUtility.CanEquipped(JobGroup, jobId)`), party in apparent order. Auto Detail (default **on**, F7) appends descriptions in item/spell/battle lists; battle and field spells read MP from `Ability.UseValue`.
- **Bestiary:** `KeyContext.BestiaryDetail` + arrows/WASD/D-pad; `LibraryInfoController.SetData` is the sole detail announcer (reads the new "Name" entry 0); page-button hooks removed, `OnChangedMonster` only refreshes data; list minimap via `LibraryMenuController.ChangeState` (reads `selectState`).
- **Controls pop-up:** `ConfigKeysSettingController.GamePadHelpInit/KeyboardHelpInit` render `HelpContentList`/`KeyboardHelpContentList` through `BuildCommandAnnouncement` → `KeyHelpReader.OpenControlsHelp`; `*SelectInit`/`Close` tear it down; `KeyContext.KeyHelp` while its owner is active.
- **Battle pause menu:** `BattlePauseController.SetEnablePauseMenu(true)` announces the focused command; `CursorNavigationHandler` routes cursor moves on `selectCommandCursor` (before battle suppression) → names from `commandMessageIdList`.
- **Shops:** command SetCursor gated on `ShopController` state `SelectCommand(1)`; position counts active pool entries; sell-list empty slots → "Empty"; `ShopTradeWindowController.Show` + Add/TakeCount → "Quantity: {0}, Total: {1}" (`selectedCount`).
- **Popups:** `CommonPopup.UpdateFocus` postfix reads the focused button for every KeyInput CommonPopup (the dead `UpdateCommand` hook was removed); the open read speaks message + focused button, and the button guard resets on close.
- **Audio:** wall-tone/beacon loops also stay silent while `!ControllerRouter.IsFieldActive || SuppressGameInput` (field menu, shops, mod menu, naming dialogs). `IsOnValidMap`, `GetAllFieldEntities` and `EnsureFieldContextAndScan` refresh the cache on a miss. Status context requires `ValidateState()`.
- **Mod menu:** item names/options/descriptions are English keys translated when read (the menu is built before the game language is known); I reads the focused setting's description.
- **Review fixes (2026-09-23):**
  - Victory screen: one-shot guards also reset by `BattleResultState.ResetIfNewResult` (new `targetData` pointer), called first in every phase postfix; the `StartBattle` reset stays.
  - Equipment slots: the `Deiscription` read for I has its own try/catch, so a throwing getter on an empty-slot stub can't cost "Head: Empty".
  - Equipment candidate list: the description is appended only with Auto Detail; always stored for I (`EquipmentDetails.LastDescription`).
  - Shops: an empty sell slot no longer overwrites `LastItemDescription`/`LastItemMpCost`, so I and U both keep referring to the last real item (was ". 5 MP" / previous item).
  - U in a shop: "Equipment info unavailable" (new key, 12 languages) when the row can't be resolved to master data (no `MasterManager`, Content name lookup miss, weapon/armor with no readable `EquipJobGroupId`/`JobGroup`). Non-equipment stays silent.
  - Controls pop-up: `ConfigKeysSettingController.NoneInit` (unique RVA 0x461EC0) also tears down `KeyHelpReader`.
  - Dialogue: `OnSceneLoaded` calls `DialogueTracker.Reset()` when `IsInDialogue` is stuck and `MessageWindowManager.IsOpen()` is false (IsOpen = `currentWindowController` non-null && `isPlaying` && window active, per disassembly), before `OnSceneTransition` so the loops Reset starts are stopped and restarted normally. The open-window check keeps an additive scene load mid-dialogue from wiping the pages.
  - Battle pause menu: controller cached by `SetEnablePauseMenu` (true sets, false clears; also cleared in `OnSceneLoaded`) instead of `FindObjectOfType` on every in-battle cursor move.
  - Mod menu: volume description corrected to "Enter mutes it, or sets it to 50 percent when muted." (`Toggle` sets 50%, not the previous level).
- **Multi-hit damage (2026-09-23):** "Target: NxTotal damage" on weapon attacks now works in FF4. It never could before: the game draws its ×N (`BattleBasicFunction.CreateHitCount` → `DamageViewUIManager.CreateHitCount`) only when `SystemConfigData.GetBattleType()` is Command, and FF4's returns 0 (ATB; FF1–FF3 return 1), so the `CreateHitCount` hook never fired. The `CreateDamageView` postfix now reads the attack's own count from `__instance.ICalcResultDic[target].GetHitCount()` for weapon abilities (`Ability.TypeId` 4 — the Fight command's ability 1; the same rule the ×N display uses). `battleActData` is protected, read at offset 0x28. Default is now "With hit count", stored as `MultiHitDamage`. *Unverified in game:* that `GetHitCount` is hits landed.
- **Not done (superseded 2026-09-23, session 2):** GameToggleAnnouncer (F1/F3 from any source). Now done by `Patches/GameTogglePatches.cs`; see "Open-issues pass (2026-09-23, session 2)".
- **Unverified in game:** everything above that depends on call order — per-turn command reset/queueing, same-actor "X's turn" reset, item/ability list re-entry reset, deferred focus reads, bestiary page changes via SetData, minimap ChangeState, pause-menu hooks, CommonPopup.UpdateFocus on open and navigation, shop state gate/empty slots, `ExtraSoundListContentInfo.playTime` units (seconds), learned-spell page.

### Open-issues pass (2026-09-23, session 2)
Everything here is **unverified in game**. RVAs are from `dump.cs`; callers from `tools/hitscan.py ff4`; bodies from capstone (`tools/callees.py` plus a scratch disassembler that resolves string literals and metadata through `script.json`).

- **F1/F3 from any source (`Patches/GameTogglePatches.cs`).** Prefixes on `CheatSettingsClient.SetIsEnableEncount` (RVA 0x9179E0, count 1) and `ConfigClient.SetIsAutoDash` (RVA 0x91ABC0, count 1), as in FF5.
  - Callers: encounters ← `FieldMap.UpdatePlayerStatePlay` (the field toggle), `ConfigActualDetailsControllerBase.SetEnableEncount` ×2 (config), `SaveSlotManager.<GotoLoadSaveData>d__50.MoveNext` (load). Auto-dash ← `FieldMap.UpdatePlayerStatePlay`, `ConfigActualDetailsControllerBase.SetIsAutoDash` and `SwitchArrowSelectTypeProcess` (config).
  - Real change only: the prefix compares with `CheatSettingsData.IsEnableEncount` / `Config.IsAutoDash`. The load path passes the value it just deserialized (`UserDataManager+0xA8` → `CheatSettingsData.isEnableEncount` @0x10), so a load is never a change.
  - Field gate: the game's own state at call time. `Il2CppLast.Management.SceneManager.Instance.GetCurrentSubSceneManager()` cast to `SubSceneManagerMainGame`, `GetCurrentState() == Player (3)`. `UpdatePlayerStatePlay` runs only in Player; the config menu runs in Menu (5); the title screen has no MainGame sub-scene. Fallback if the state can't be read: `IsOnValidMap() && !MenuStateRegistry.AnyActive()` (FF5's gate). FF4's `MenuStateRegistry` has no field-menu state and only sets `Config` once a row is announced, which is why the game state is the primary gate.
  - Walk/Run is `__0 != 0` (auto-dash on = Run), as in FF1/FF5. The old keypress path XOR-ed a `FieldKeyController.SetDashFlag` cache, but that class is `Last.OutGame.Library` (the extras map viewer) and the hook has no direct callers, so it never affected the field.
  - Removed: the F1/F3 keypress coroutines in `InputManager` (they spoke on any F1/F3, and never for stick clicks). `MoveStateHelper.GetDashFlag` is now unused.
- **"{0} attacks" by id (`BattleMessagePatches.IsPlainAttack`).** FF4 master data: command 1 = Fight (`MSG_SYSTEM_085`, ability_id 1, type 1); command 2 Defend (type 8), 3 Items, 5 Row (type 7), 6 Skip. Ability 1 is type 4 (weapon) and named `MSG_SYSTEM_085`. Two more type-4 abilities, 209 and 442, carry the same name message, so a plain attack is: first ability id 1, or an ability whose localized name equals the Fight command's localized name (master `Command` 1 → `MesIdName` → `MessageManager`), or no item and no ability under command id 1. Items and named abilities keep "uses X". The English "Attack" compare is gone. Unnamed type-4 abilities (243, 245, 284, 465) are still skipped as before.
- **U fallback.** `ItemDetailsAnnouncer.BuildAnnouncement` returns "Equipment info unavailable" (existing key) when the party can't be read (null or empty). The item-menu path also says it when the owned item can't be found or on an exception. Non-equipment stays silent by design (as in FF1).
- **Multi-hit count: offline answer.** `GetHitCount()` on a Fight calc result is always **0** in FF4, so `ReadWeaponHitCount` returns 1 and FF4 reads the total only (never "3x152").
  - `ClacExecuteFF4.PhysicalExecution` (RVA 0xA097F0) returns `ValueTuple<int, HitType, int, List<Condition>>`. Item3 is hits **landed**: the first loop counts attacker hit rolls (`r13d`, rand 0–98 < hit rate), the second counts target evasion rolls (`r15d`, logged as 今回のターゲット防御回数), and Item3 = `r13d - r15d` (below 1 means Miss).
  - `CalcControllerProvider.GetFightStatus` (RVA 0x429080, the only `ICalcControllerProvider`) passes Item1/Item2/Item4 to `ICalcResult.SetStatus` but hitCount = `edi`, zeroed at +0x25B and never written again. `FuncitonNormalAttack.Calc` stores that result. `FunctionBase.GetCalcResult` and `BattleBasicFunction.SetupValueToDisplay` only copy or sum it.
  - The game's own ×N (`BattleBasicFunction.CreateHitCount`) reads the same `GetHitCount`, but only in Command battle mode.
  - Getting the landed count would need a postfix on `PhysicalExecution`, which returns a 24-byte generic struct through a hidden return buffer. That is untested under Il2CppInterop, so it was not done (decision for the user).
- **Value-0 battle events.** The `CreateDamageView` postfix fires even when the game returns early. The game's body returns without a view for value 0 with Non/Hit/MPHit, and always for RecoveryCondition.
  - Buff/debuff (`GetAddConditionStatus` → `ClacExecuteFF4.AddConditionExection`, which returns 0 when anything landed, else 2) = **Hit**, or **Miss**.
  - Status cure (`RecoveryConditionFunction.Calc` → `GetRecoveryCondition`, slot 12) = **Non (-1)**, value 0, conditions = [cured condition]. Its fail path calls `GetFixedStatus(Miss)`; a special SortId-5 path uses `GetFixedStatus(Hit or Miss)`. `CalcResult..ctor` also defaults hitType to Non.
  - **RecoveryCondition (7) is never written.** A byte scan of all 49 `ICalcResult.SetStatus` call sites in GameAssembly.dll found hit-type constants -1, 2, 3, 4 and 5 only; the register-sourced ones come from Physical (Hit/Critical/Miss), Magic (Hit/Miss), Unique (Hit/Miss) and Steal tuples.
  - **Zero (3)** is written only by `DamageAggregater.CheckUndead` (a recovery reversed on an undead target that comes to 0) and `MagicAbsorptionFunction.Calc`: genuine 0 results, never buffs or debuffs.
  - So, per the shared spec: Zero now speaks "{0}: 0 damage" (existing key) and RecoveryCondition "{0}: cured" (new key, 12 languages; effectively dead in FF4). A status cure stays silent. One line per unspoken value-0 view is logged: `[Battle] value-0 view: hitType=N isRecovery=B target=X conditions=[ids]`. A single Antidote/Esuna test confirms the Non + conditions shape, and "value 0 + Non + non-empty conditions → cured" would then be a safe rule (decision). No condition-removal hook exists, so nothing else announces cures.
- **Save overwrite Yes/No (BugReports FF4 #1): still broken before this pass.** See "Save/Load Popup Button Navigation". The fix is the `SavePopup.SetCommandSelectCursor` postfix (RVA 0x99BFA0, callers `ResetCursor`, the `UpdateSelect` index callback, `UpdateSelect`, `KeyInput.SaveWindowController.PopupInit`, the `InitializeButton` mouse lambda). The per-frame `SavePopup.UpdateCommand` hook is removed. The open read now appends the focused button. `OverwriteConfirmInit` reads the right popup and has a prefix.
- **Status rows (BugReports #2).** Row order was already display order (`GetCorpsListCloneWithApparentOrder`). The "Level", "Front Row" and "Back Row" words in `CharacterSelectionReader` were English literals; they now go through `T()` with the existing keys (English output unchanged).
- **BugReports #3/#5 verified, no change.** Cure targeting reads `targetContents` (display order). Every entity navigation key delta-scans (`EnsureFieldContextAndScan` → `RefreshIfNeeded` → `Scan`). #4 (opening map timing) is by design.
- **Menu entry reads (one frame) verified from code, no change.** The item list's `UseSelectInit` builds `dataList` synchronously (`CreateDataList`, `UpdateList`) and places the cursor in a `WaitEndFrame` coroutine that finishes before the next frame's read. `ItemUseController.SingleInit` calls `SelectContent` inside the Init. `AbilityWindowController.UseListInit` calls `ShowUseList` and `FocusSelectCursor` synchronously. The field menu uses the same one-frame `MenuManager.IsOpen` gate as FF1. No menu was found whose content isn't ready one frame after its Init.
- **FF1 cross-checks.**
  - Unplugging the controller in mod mode, or with a keyboard-closed mod menu, now resets the router (`SuppressGameInput` could stay set).
  - A mod menu opened with F8 now puts the router in its ModMenu state, so the D-pad and stick drive the menu instead of field functions.
  - Tab no longer clears the battle flag while an active `BattleController` exists. The old check only looked at the active scene's name, but the battle is an additive sub-scene.
  - `IsOnValidMap` throttling (30 frames) was verified.
- **Official-name substring pass (`translation.json`).** The first alignment pass (`tools/official_fix.py`) only fixed entries whose Japanese key *exactly* matched a string in the game's own message tables. This pass also fixed keys that *contain* an official proper noun: characters, places, key items, vehicles, monsters, weapons and jobs. Only that noun was replaced with the game's form for that language, taken from `tools/extract_entities.py gamedict` (FF4's own tables only).
  - **Also covered:**
    - kana spellings of official items: クリスタルのたて = クリスタルの盾, メデューサのや, フェニックスのお, クアールのひげ. This also fixed garbled ko/zht/ru values.
    - name stems that several of the game's official strings share: Baron 巴隆; Korean 크리스탈 → 크리스털.
    - further examples: ko Edward → 길버트 in ギルバートと再会; es Namingway → Don Registro.
  - **Totals:** 140 entries / 612 values. Russian and German case forms were kept.
  - **Left for review:**
    - 白魔道士 NPCs in it/es/pt stay masculine; the official job name is feminine because it names Rosa's and Porom's job.
    - 地上 in fr/es keeps "surface" / "mundo superior"; the official Mappemonde / Mundo are world-map labels.
    - 幻獣王妃 has no official string, and its values mix Esper, Eikon and Eidolon. Suggested fix: follow 幻獣王.
    - ボムのたましい: unclear whether it is the official "Bomb Core".
    - 孤島:ギサールの野菜 keeps an untranslated "Island:" prefix in 8 languages.
    - Italian uses "Eikon" for 幻獣.
  - **Where the rules live:** the rules, the full old→new list and the skipped items are in `D:\Games\Dev\Unity\FFPR\tools\official_substring\`.

### Vehicle Names
`TransportationInfo.MessageId` → `MessageManager.GetMessage()` for specific names. Falls back to type-based generic.

### Waypoint System
User-defined map markers independent of entity scanner. Ported from FF5.

**Architecture:**
- `WaypointEntity` - Standalone class (not NavigableEntity), has own category system
- `WaypointManager` - CRUD operations, Newtonsoft.Json persistence to `UserData/waypoints.json`
- `WaypointNavigator` - Cycling, category filtering, distance sorting
- `TextInputWindow` / `ConfirmationDialog` - Virtual modal dialogs (keys via GamepadManager; game input suppressed; no focus stealing)

**Categories:** All, Docks, Landmarks, Airship Landings, Miscellaneous

**Key Files:**
| File | Purpose |
|------|---------|
| `Core/WaypointManager.cs` | CRUD + JSON serialization |
| `Core/WaypointNavigator.cs` | Cycling + category filtering |
| `Field/WaypointEntity.cs` | Data model + formatting |
| `Core/TextInputWindow.cs` | Virtual text input (keys via GamepadManager; no focus stealing) |
| `Core/ConfirmationDialog.cs` | Virtual Yes/No confirmation dialogs (chained prompts; no focus stealing) |
| `Utils/CollectionHelper.cs` | Distance sorting utilities |
| `Utils/PlayerPositionHelper.cs` | Player position retrieval |

**Dialog Input Flow:** `InputManager.Update()` checks the modals first (`ConfirmationDialog`/`TextInputWindow`/`ModMenu`, each consuming all input when open) before the window-focus gate, so the virtual dialogs keep working even when the game window isn't foreground. Keys are read via `GamepadManager` (SDL3 + GetAsyncKeyState); game input is suppressed via `ControllerRouter.SuppressGameInput` + `InputPassthroughPatches` + `Input.ResetInputAxes` (no window focus stealing).

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
`SavePopup.SetCommandSelectCursor` postfix (private, unique RVA 0x99BFA0) reads the cursor index from `selectCursor` (0x58), deduplicates via `AnnouncementDeduplicator.ShouldAnnounce("SaveLoadPopupButton", index)`, and reads the button text from `commandList` (0x60) → `CommonCommand.text` (0x18). Every SavePopup focus change goes through that method: `UpdateSelect`'s up/down lambdas pass it to `Cursor.NextIndex/PrevIndex` as the index callback (always invoked), and `ResetCursor`, the mouse handler and the main-menu save's `PopupInit` call it directly. It replaced the per-frame `SavePopup.UpdateCommand` hook (2026-09-23, session 2), which the main-menu save never calls: `KeyInput.SaveWindowController.PopupUpdate` calls `SavePopup.UpdateSelect` directly, so the save overwrite Yes/No was silent (BugReports FF4 #1). `CursorNavigationPatches` has an early `SaveLoadMenuState.IsActive` return before `PopupState.ShouldSuppress()` so the generic reader never double-reads the buttons.

The open read (`ReadSavePopupAt`, from every `SetPopupActive(true)` / `SetEnablePopup(true)` and from `OverwriteConfirmInit`) sets `savePopupOpenReadPending` (the focus postfix stays quiet), resets the button guard, and two frames later speaks "title. message. focused button" and claims the guard for that index.

**Overwrite confirmation** (`Last.UI.Save.KeyInput.SaveWindowController.OverwriteConfirmInit`) drives the view's `SavePopup` (view @0x30 → `SaveWindowView.savePopup` @0x28; every popup call in the body goes through it). The controller's `CommonPopup` @0x38 belongs to `SaveConfirm`; the old postfix read its message by mistake. A prefix sets the pending flag because the body's `ResetCursor` fires the focus postfix. Other KeyInput CommonPopups: `PopupOpen_Postfix` reads message + focused button, and `CommonPopup.UpdateFocus` reads their moves.

### Utility Classes

| File | Purpose |
|------|---------|
| `Utils/PatchHelper.cs` | Shared `FindType()` and `TryPatchPostfix()` for Harmony patch boilerplate |
| `Core/Constants.cs` | Shared magic numbers (CellSize, SampleRate, WavHeaderSize) |
| `Utils/TextUtils.cs` | `NormalizeWhitespace()`, `StripRichTextTags()` |
| `Utils/MessageHelper.cs` | `GetLocalizedMessage()` for common MessageManager lookup pattern |

### Compilation Notes
`param.Level` → `param.ConfirmedLevel()` | `FirstSlotSelect` → `SlotSelect` | Private → `AccessTools.Method()`
