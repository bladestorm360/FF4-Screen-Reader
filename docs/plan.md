# FF4 Screen Reader - Status

**TODO:** Multi-phase Victory Screen - break into phases (gil/items, then per-character XP/level-ups)

## Features
Field Navigation, Pathfinding, Moon Pathfinding, Wall Bump | Menu System | Battle (actions, damage, status, two-part abilities, defeat) | Shops | Victory Screen | Vehicles | Status Screen | Story/Dialogue | Popups | Game Over | Save/Load | Title | Namingway | Item Equipment Info (I key) | Config | Entity Translation | F1/F3 Toggles

## Hotkeys

### Mod Hotkeys
| Key | Function | Context |
|-----|----------|---------|
| F8 | ModMenu (audio settings) | Global |
| I | Item equipment info | Items menu |
| 0 | Dump untranslated entity names | Field |
| J, [ | Cycle entities backward | Field |
| K | Repeat current entity | Field |
| L, ] | Cycle entities forward | Field |
| P, \ | Pathfind to entity | Field |
| Shift+J/L | Cycle categories | Field |
| Shift+K | Reset to All category | Field |
| =, - | Cycle categories | Field |
| ; | Toggle wall tones | Field |
| ' | Toggle footsteps | Field |
| 9 | Toggle audio beacons | Field |

### Game Hotkeys (mod announces state)
| Key | Game Function | Mod Announcement |
|-----|---------------|------------------|
| F1 | Walk/Run toggle | "Run" or "Walk" |
| F3 | Encounters toggle | "Encounters on/off" |
| Q | Shop description panel | (panel content) |

## Recent Changes

| Date | Feature | Summary | Files |
|------|---------|---------|-------|
| 2026-02-02 | Placeholder Filter | Filters Unknown, spawn defaults, generic prefixes, effects from scanner | `Field/EntityFactory.cs` |
| 2026-01-31 | Game Over | Defeat message + Load/Title/Yes/No popup navigation | `Patches/BattleMessagePatches.cs`, `Patches/PopupPatches.cs` |
| 2026-01-29 | Sound System | 16-bit audio, volume controls, ModMenu (F8), IL2CPP-safe loops | `Utils/SoundPlayer.cs`, `Core/ModMenu.cs` |
| 2026-01-29 | Performance | Memory leak fixes, caching (EntityNavigator, GroupEntity, SoundPlayer) | Various |
| 2026-01-29 | Vehicles | MessageId lookup for specific names (Falcon, Lunar Whale) | `Field/NavigableEntity.cs` |
| 2026-01-29 | F1/F3 Toggles | Walk/Run and Encounters state announcements | `Core/InputManager.cs` |
| 2026-01-28 | Entity Translation | Japanese→English via JSON dictionary, 0 key dump | `Utils/EntityTranslator.cs` |
| 2026-01-20 | Code Quality | Event-driven refresh, AnnouncementDeduplicator, CharacterStatusHelper | `Utils/` |

## Exclusions
Esper/Magicite, Airship Navigation (FF6-specific)
