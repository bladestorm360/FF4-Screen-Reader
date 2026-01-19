# FF4 Screen Reader - Status

**COMPLETE** (ported from `ff6/ff6ScreenReader/FFVI_MOD`)

**TODO:** Multi-phase Victory Screen - break into phases (gil/items, then per-character XP/level-ups)

## Features ✅
Field Navigation, Pathfinding, Moon Pathfinding, Wall Bump | Menu System | Battle (actions, damage, status, messages, two-part abilities) | Shops | Victory Screen | Vehicles | Status Screen (17 stats) | Story Text, Dialogue | Popups | Save/Load | Title | Namingway | Item Equipment Info (I key) | Config Announcements

## Code Quality (2026-01-18)

**Deduplication Consolidation:** 18 independent `lastAnnounced*` variables → centralized utilities:
- `Utils/AnnouncementDeduplicator.cs` - Context-keyed deduplication
- `Utils/CharacterStatusHelper.cs` - HP/MP/status reading

**Contexts:** AbilityMenu (Command/Content/UseTarget), ItemMenu (List/UseTarget), EquipmentMenu (Select/Tracker), Battle (Target.Player/Enemy, ConditionAdd, Turn), BattleCommand/Item/Ability.Select, ConfigMenu (Command/KeysSetting), PartySetting.Select, Shop.Item, Naming.Select

**Not converted:** MovementSpeechPatches (value comparison), MessagePatches (formatting decisions)

**Removed:** All timer-based deduplication (MESSAGE_THROTTLE_SECONDS, etc.) - now uses simple equality

## Exclusions
Esper/Magicite, Airship Navigation (FF6-specific)
