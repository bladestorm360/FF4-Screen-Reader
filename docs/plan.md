# FF4 Screen Reader - Status

**Done (2026-09-23):** Multi-phase victory screen (gil + EXP, level-ups, items, learned spells each on their own page) — see Recent Changes.

## Features
Field Navigation, Pathfinding, Moon Pathfinding, Wall Bump | Menu System (focus announced on open and back-out) | Battle (actions, damage, status added and removed — removal not yet verified in game, two-part abilities, defeat, pause menu) | Shops | Phased Victory Screen | Vehicles | Status Screen | Bestiary (detail navigation, minimap) | Story/Dialogue (R repeat) | Popups | Game Over | Save/Load | Title | Namingway | Details (I) / Usable-by (U) keys | Auto Detail | Config (controls pop-up navigation) | Mod menu descriptions | Entity Translation | F1/F3 Toggles | **Waypoint System**

## Hotkeys

### Mod Hotkeys
| Key | Function | Context |
|-----|----------|---------|
| F8 | ModMenu (I describes the focused setting) | Field |
| F5 | Cycle enemy HP display | Battle |
| F7 | Toggle Auto Detail | Global |
| I | Details: description of the focused item / spell / equipment / battle entry / shop entry, else the config tooltip | Menus, battle |
| U | Which party members can equip the focused item | Items menu, shops |
| Shift+I | Read on-screen controls | Global |
| R | Repeat the current dialogue page | Dialogue |
| G / H | Gil / character status | Global |
| M / Shift+M | Current map / toggle map exit filter | Global |
| V | Vehicle / movement state | Global |
| T / Shift+T | Active timers / freeze timers | Global |
| Tab | Clears a battle flag left set outside the battle scene (fallback) | Global |
| Arrows or W/S (Shift = group, Ctrl = top/bottom) | Navigate status details, bestiary detail, controls pop-up | Those screens |
| ` (backtick) | Rescan entities | Field |
| J, [ | Cycle entities backward | Field |
| K | Repeat current entity | Field |
| L, ] | Cycle entities forward | Field |
| P, \ | Pathfind to entity | Field |
| Ctrl+\, Ctrl+P | Toggle layer transition filter | Field |
| Shift+\, Shift+P | Toggle pathfinding filter | Field |
| Shift+J/L | Cycle categories | Field |
| Shift+K | Reset to All category | Field |
| =, - | Cycle categories | Field |
| ; | Toggle wall tones | Field |
| ' | Toggle footsteps | Field |
| 9, F6 | Toggle beacon navigation | Field |
| Ctrl+Arrows | Teleport one tile | Field |
| , | Previous waypoint | Field |
| . | Next waypoint | Field |
| / | Pathfind to waypoint | Field |
| Shift+, | Previous waypoint category | Field |
| Shift+. | Next waypoint category | Field |
| Shift+/ | Add new waypoint | Field |
| Ctrl+/ | Delete waypoint | Field |
| Ctrl+. | Rename waypoint | Field |
| Ctrl+Shift+/ | Clear all map waypoints | Field |

### Controller
| Input | Function |
|-------|----------|
| Start | Mod menu (field) |
| Back/Select | Mod mode on/off |
| Mod mode + X | Gil (field), party HP (battle), repeat dialogue (message window open) |
| Mod mode + Y / A | Location / vehicle state (field) |
| Mod mode + right stick | Teleport (field) |
| A/B/X/Y | Stop speech |
| D-pad | Waypoints (field); status / bestiary / controls-pop-up navigation |
| Right stick | Entities (field); off-field up = details (I), down = controls (Shift+I), left = usable by (U) |
| LT | Pathfind to / restart beacon at the last target |
| L3 / R3 | Beacon navigation / pathfinding filter (in mod mode when Stick Click Normalization is on); on the field a lone click acts on release |
| L3 + R3 | Stick Click Normalization on/off (field, either setting) |

### Game Hotkeys (mod announces state)
| Key | Game Function | Mod Announcement |
|-----|---------------|------------------|
| F1 (or controller) | Walk/Run toggle | "Run" or "Walk", from the game's setter on the field, whatever the input (not yet verified in game) |
| F3 (or controller) | Encounters toggle | "Encounters on/off", from the game's setter on the field, whatever the input (not yet verified in game) |
| Q | Shop description panel | (panel content) |

## Recent Changes

| Date | Feature | Summary | Files |
|------|---------|---------|-------|
| 2026-09-24 | Round 2 — **not yet verified in game** | Status removal: "X: Poison removed" for cures, wear-off, conflicts and revive ("X: KO removed"), from `BattleConditionController.RemoveFunction` (silent at battle end, for statuses cleared by KO, for unnamed conditions); "{0}: cured" branch/key and the value-0 log removed. Multi-hit Damage setting removed (FF4 records no hit count): damage always reads the total. Per-frame/polling/timer sweep: CommonPopup, game-over and save/load Yes/No, config rows, footsteps, bestiary formation, gallery/music entry, EXP counter end, audio restart and the waypoint dialogs are now event-driven (no `WaitForSeconds` left). Game-over Load/Title moves no longer spoken twice; dead F1 dash-flag hook removed. Follow-up: Config reads the focused row on every open and on return from its sub-screens (in game and title options); "On {0}" vehicle words, the remap prompts and the title fallback go through `T()` (3 new keys). See debug.md "Round 2 (2026-09-24)". | `Patches/BattleMessagePatches.cs`, `Patches/PopupPatches.cs`, `Patches/SaveLoadPatches.cs`, `Patches/CursorNavigationPatches.cs`, `Patches/ConfigMenuPatches.cs`, `Patches/FootstepPatches.cs`, `Patches/BattleResultPatches.cs`, `Patches/BestiaryPatches.cs`, `Patches/GalleryPatches.cs`, `Patches/MusicPlayerPatches.cs`, `Patches/MovementSpeechPatches.cs`, `Core/AudioLoopManager.cs`, `Core/FFIV_ScreenReaderMod.cs`, `Core/ConfirmationDialog.cs`, `Core/TextInputWindow.cs`, `Core/ModMenu.cs`, `Core/PreferencesManager.cs`, `Utils/MoveStateHelper.cs`, `mod_text.json` |
| 2026-09-23 | Open-issues pass (session 2) — **not yet verified in game** | F1/F3 announced from `CheatSettingsClient.SetIsEnableEncount` / `ConfigClient.SetIsAutoDash` prefixes, gated on a real change and the game's Player state (keypress coroutines removed); "{0} attacks" by command/ability id (no English compare); U says "Equipment info unavailable" when the party can't be read; save-popup Yes/No read from `SavePopup.SetCommandSelectCursor` (fixes the silent save-overwrite Yes/No; open read appends the focused button; overwrite popup offset fixed); value-0 views: Zero → "0 damage", RecoveryCondition → "{0}: cured" (new key), others logged; status rows/level localized; controller unplug / F8 router sync; Tab keeps the battle flag while a BattleController is live. Multi-hit: FF4's calc stores hit count 0, so damage reads the total only (documented). See debug.md "Open-issues pass (2026-09-23, session 2)". | `Patches/GameTogglePatches.cs` (new), `Core/InputManager.cs`, `Core/FFIV_ScreenReaderMod.cs`, `Core/ControllerRouter.cs`, `Patches/BattleMessagePatches.cs`, `Patches/ItemDetailsAnnouncer.cs`, `Patches/SaveLoadPatches.cs`, `Patches/PopupPatches.cs`, `Menus/CharacterSelectionReader.cs`, `mod_text.json` |
| 2026-09-23 | Parity Pass Review Fixes | Victory-screen guards also reset on a new result-data object; equipment-slot description read isolated; equipment candidate descriptions gated on Auto Detail; empty sell slots no longer clobber I/U targets; shop U says "Equipment info unavailable" when lookup fails (new key, 12 languages); controls pop-up closed on `NoneInit`; stuck dialogue state reset on scene load; battle pause controller cached; volume description corrected (12 languages). See debug.md "FF1 Parity Pass" → Review fixes. | `Patches/BattleResultPatches.cs`, `Patches/ItemMenuPatches.cs`, `Patches/ShopPatches.cs`, `Patches/ConfigMenuPatches.cs`, `Patches/BattlePausePatches.cs`, `Menus/UsableByAnnouncer.cs`, `Core/FFIV_ScreenReaderMod.cs`, `Core/ModMenu.cs`, `mod_text.json` |
| 2026-09-23 | FF1 Parity Pass | Dialogue-close reset restored (`MessageWindowManager.Close` → `DialogueTracker.Reset`; audio/wall bumps no longer muted after the first conversation); controller speech interrupt via `Tolk.Silence`; stale dedup contexts reset at battle start / per turn / on list and menu entry; phased victory screen; audio loops gated on `IsFieldActive`/`SuppressGameInput`; cache self-heal; focus read on field menu, title menus, item/equipment/ability command bars, item list and item target; R / mod-mode X dialogue repeat; bestiary detail navigation + single announcer + minimap; I/U split (description vs usable-by, shop usable-by from master data), Auto Detail default on and gating descriptions; controls pop-up navigation; battle pause menu; shop state gate, active-entry count, empty sell slots, trade-window quantity; mod-menu descriptions (I) and "Beacon Navigation" label; F6/F7/Ctrl+P/backtick/Tab/WASD; E-minor-7 wall tones, quieter footsteps, music `playTime`, "Empty" equip slots, CommonPopup focus (overwrite Yes/No); language-independent defeat interrupt; 141 new mod_text keys (localization sweep). Unverified in game: see debug.md "FF1 Parity Pass". | ~45 files, `mod_text.json` |
| 2026-09-23 | Offline Entity Extraction | `tools/extract_entities.py` sweeps all map bundles against a mirror of the translator's lookup; 192 missing labels translated into 11 languages (559→751 entries, 660/660 labels covered); 82 existing entries aligned with the game's official names. See debug.md "Entity Translation". | `translation.json`, `tools/` |
| 2026-07-05 | EXP Counter Sound | Ported from FF5. Rising/ticking beep while the EXP tally animates on the victory screen; `ExpCounter` toggle (default on) + `ExpCounterVolume` in a "Battle Results" ModMenu section. START on `ResultMenuController.ShowPointsInit` (`GetExp>0`); completion via `MonitorExpCounterAnimation` pointer walk (`pointController→characterListConteroller→contentList`/`perormanceEndCount`, offsets identical to FF5); STOP safety nets on ShowStatusUp/GetAbilitys/GetItems/EndWaitInit. Character EXP only (FF4 has no ABP). Menu labels in 12 languages. | `Patches/BattleResultPatches.cs`, `Utils/SoundPlayer.cs`, `Utils/ToneGenerator.cs`, `Utils/SoundConstants.cs`, `Utils/AudioEngine.cs`, `Core/PreferencesManager.cs`, `Core/ModMenu.cs`, `Core/FFIV_ScreenReaderMod.cs`, `mod_text.json` |
| 2026-03-20 | Overwrite Popup Fix v3 | Patched `CommonPopup.UpdateCommand` (gated on `SaveLoadMenuState.IsActive`) for overwrite Yes/No button navigation. `OverwriteConfirmInit_Postfix` keeps SaveLoadMenuState active, resets dedup, reads popup text via coroutine. Generic popup system still handles non-save CommonPopups. | `Patches/SaveLoadPatches.cs` |
| 2026-03-20 | 4 Bug Fixes + Overwrite Fix | Save overwrite Yes/No now works (hook OverwriteConfirmUpdate instead of CommonPopup.UpdateCommand which game never calls), Status/Formation row order (GetCorpsListCloneWithApparentOrder), Cure target fix (targetContents display order), Entity refresh on event/cutscene end. Opening map fix reverted (caused "map 0" bugs). | `Patches/SaveLoadPatches.cs`, `Menus/CharacterSelectionReader.cs`, `Patches/FormationRowPatches.cs`, `Patches/AbilityMenuPatches.cs`, `Patches/GameStatePatches.cs`, `Utils/AnnouncementContexts.cs` |
| 2026-02-08 | Code Audit Round 2 | 8-phase deep refactoring: Constants/dead code, TextUtils/MessageHelper, PatchHelper dedup, CursorNav DRY, GetPlayerPosition consolidation, AnnouncementDeduplicator migration, facade extraction (EntityNavigationFacade, WaypointFacade, NavigationStateManager, GameAnnouncementHelper), Newtonsoft.Json for waypoints, BaseEntityFilter, NPCEntity/VehicleEntity file split, EntityTypeName delegation. FFIV_ScreenReaderMod.cs 1180→362 lines. | ~40 files |
| 2026-02-07 | Code Audit & Release Prep | Removed ~65 debug log calls, ~340 lines dead code (disabled patches, EntityTranslator dump tooling, redundant status method), extracted AudioFeedbackManager (~450 lines), consolidated direction/distance/VK constants/WAV headers/dialog callbacks, simplified MenuState wrappers with SimpleMenuState, data-driven TextInputWindow key handling | ~30 files |
| 2026-02-06 | Speech Redundancy Fixes | Map name dedup (replaced LocationMessageTracker), save/load popup dedup (ported SavePopupUpdateCommand, early SaveLoadMenuState return) | `Patches/MessagePatches.cs`, `Patches/GameStatePatches.cs`, `Core/FFIV_ScreenReaderMod.cs`, `Patches/CursorNavigationPatches.cs`, `Patches/SaveLoadPatches.cs` |
| 2026-02-05 | Layer Transition Filter | Unfiltered ToLayer entities (underworld entrance), added toggleable filter (Ctrl+\, ModMenu) | `Field/EntityFactory.cs`, `Core/Filters/ToLayerFilter.cs`, `Core/EntityNavigator.cs`, `Core/FFIV_ScreenReaderMod.cs`, `Core/InputManager.cs`, `Core/ModMenu.cs` |
| 2026-02-03 | Waypoint System | User-defined map markers with CRUD, categories, pathfinding, JSON persistence | `Core/WaypointManager.cs`, `Core/WaypointNavigator.cs`, `Field/WaypointEntity.cs`, `Core/TextInputWindow.cs`, `Core/ConfirmationDialog.cs` |
| 2026-02-02 | Placeholder Filter | Filters Unknown, spawn defaults, generic prefixes, effects from scanner | `Field/EntityFactory.cs` |
| 2026-01-31 | Game Over | Defeat message + Load/Title/Yes/No popup navigation | `Patches/BattleMessagePatches.cs`, `Patches/PopupPatches.cs` |
| 2026-01-29 | Sound System | 16-bit audio, volume controls, ModMenu (F8), IL2CPP-safe loops | `Utils/SoundPlayer.cs`, `Core/ModMenu.cs` |
| 2026-01-29 | Performance | Memory leak fixes, caching (EntityNavigator, GroupEntity, SoundPlayer) | Various |
| 2026-01-29 | Vehicles | MessageId lookup for specific names (Falcon, Lunar Whale) | `Field/NavigableEntity.cs` |
| 2026-01-29 | F1/F3 Toggles | Walk/Run and Encounters state announcements | `Core/InputManager.cs` |
| 2026-02-21 | Entity Translation Expansion | Added 180 new translations (items, weapons, armor, NPCs, events, vehicles) from JSON captures, 379→559 total entries | `Utils/EntityTranslator.cs` |
| 2026-01-28 | Entity Translation | Japanese→English via JSON dictionary, 0 key dump | `Utils/EntityTranslator.cs` |
| 2026-01-20 | Code Quality | Event-driven refresh, AnnouncementDeduplicator, CharacterStatusHelper | `Utils/` |

## Exclusions
Esper/Magicite, Airship Navigation (FF6-specific)
