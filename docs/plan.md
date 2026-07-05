# FF4 Screen Reader - Status

**TODO:** Multi-phase Victory Screen - break into phases (gil/items, then per-character XP/level-ups)

## Features
Field Navigation, Pathfinding, Moon Pathfinding, Wall Bump | Menu System | Battle (actions, damage, status, two-part abilities, defeat) | Shops | Victory Screen | Vehicles | Status Screen | Story/Dialogue | Popups | Game Over | Save/Load | Title | Namingway | Item Equipment Info (I key) | Config | Entity Translation | F1/F3 Toggles | **Waypoint System**

## Hotkeys

### Mod Hotkeys
| Key | Function | Context |
|-----|----------|---------|
| F8 | ModMenu (audio settings) | Global |
| I | Item equipment info | Items menu |
| J, [ | Cycle entities backward | Field |
| K | Repeat current entity | Field |
| L, ] | Cycle entities forward | Field |
| P, \ | Pathfind to entity | Field |
| Ctrl+\ | Toggle layer transition filter | Field |
| Shift+J/L | Cycle categories | Field |
| Shift+K | Reset to All category | Field |
| =, - | Cycle categories | Field |
| ; | Toggle wall tones | Field |
| ' | Toggle footsteps | Field |
| 9 | Toggle audio beacons | Field |
| , | Previous waypoint | Field |
| . | Next waypoint | Field |
| / | Pathfind to waypoint | Field |
| Shift+, | Previous waypoint category | Field |
| Shift+. | Next waypoint category | Field |
| Shift+/ | Add new waypoint | Field |
| Ctrl+/ | Delete waypoint | Field |
| Ctrl+. | Rename waypoint | Field |
| Ctrl+Shift+/ | Clear all map waypoints | Field |

### Game Hotkeys (mod announces state)
| Key | Game Function | Mod Announcement |
|-----|---------------|------------------|
| F1 | Walk/Run toggle | "Run" or "Walk" |
| F3 | Encounters toggle | "Encounters on/off" |
| Q | Shop description panel | (panel content) |

## Recent Changes

| Date | Feature | Summary | Files |
|------|---------|---------|-------|
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
