# FF4 Screen Reader Mod

## Overview
Screen-reader/accessibility mod for FF4 Pixel Remaster. MelonLoader + Harmony patches hook game. Output via Tolk to NVDA.

## Features
- Menu/cursor navigation, character vitals (HP/MP), stats
- World navigation/pathfinding with obstacle detection
- Moon pathfinding with step-by-step path validation
- Battle: turn order, targeting, damage/heals, status effects
- Battle: two-part abilities (Pray, Steal, Flee) with proper action/result separation
- Victory screen: gil, items, XP, level-ups with stat growth
- Multiple vehicle support (Hovercraft, Enterprise, Falcon, Lunar Whale)

## TODO
- **Multi-phase Victory Screen**: Break into phases (gil/items first, then per-character XP/level-ups) for better pacing

## Documentation
Update: `CLAUDE.md`, `docs/debug.md`, `docs/plan.md`. **NEVER edit `readme.md`** (user-maintained).

## Directory Structure
```
D:\games\dev\unity\ffpr\FF4\
├── ff4-screen-reader\                    # Mod source code (this directory)
│   ├── build_and_deploy.bat              # Build script - ALWAYS USE
│   ├── CLAUDE.md                         # This file
│   ├── Core\                             # Core mod classes
│   ├── Patches\                          # Harmony patches
│   ├── Menus\                            # Menu reading logic
│   ├── Field\                            # Field navigation
│   └── Utils\                            # Utility classes
├── dump.cs                               # Il2CPP class dump (18MB, 493K lines)
├── il2cpp.h                              # Il2CPP header (29MB, 914K lines)
├── script.json                           # Method metadata
├── stringliteral.json                    # String literals
├── DummyDll\                             # Reference assemblies
└── ghidra_scripts\                       # Reverse engineering scripts
```

## Build/Deploy
```
powershell -Command "& cmd /c 'D:\Games\Dev\Unity\FFPR\FF4\ff4-screen-reader\build_and_deploy.bat'"
```
**Only use this command.** Never use `dotnet build`, `cd && ...`, or `cmd //c` syntax.

## Rules
- Never load large files directly (use Grep for dump.cs, max 50 lines)
- Check logs first: `D:\Games\steamlibrary\steamapps\common\final fantasy iv pr\MelonLoader\Logs\`
- Reference FF6 mod source (`ff6/ff6ScreenReader/FFVI_MOD`) for patterns
- Never edit game or reference mod folders
- Prefix all game classes with `Il2Cpp`

## CRITICAL RULES

| Rule | Requirement |
|------|-------------|
| 0: User Approval | STOP after presenting plans. Wait for explicit "yes/approved/proceed" before implementing. |
| 1: No Doc Overwrites | Use Edit tool only for *.md files. Never use Write to replace documentation. |
| 2: No Polling | Never use per-frame checks, OnUpdate, or continuous coroutines. Find the exact Harmony hook. |
| 3: No Timers | Never use `WaitForSeconds` or hardcoded delays. Hook the precise moment instead. Exception: game's own timing systems. |
| 4: Update Docs | After completing a feature or during debugging, update `docs/plan.md` (feature status) and `docs/debug.md` (architecture/troubleshooting). |
| 5: No PowerShell Edits | Never use PowerShell scripts to edit files containing non-ASCII characters (e.g., arrows →, Japanese text). They corrupt the encoding. Use the Edit tool instead. |

## dump.cs Class Discovery
`D:\Games\Dev\Unity\FFPR\FF4\dump.cs` (~18MB, 493K lines). **Never load entirely** - use Grep:
```
Grep pattern="class StatusDetailsController" path="D:/Games/Dev/Unity/FFPR/FF4/dump.cs"
```

## Boundaries
- **Never access** FF6 game directory (steamapps/common/FINAL FANTASY VI PR)
- **FF6-only** (don't port): Esper/Magicite, Opera, Blitz, Tools, Trance

## FF4-Specific
- **Party**: 3-slot system with story-locked members
- **Vehicles**: Hovercraft, Enterprise, Falcon, Lunar Whale, Submarine
- **Moon**: MapId=3 uses step-by-step path validation (ledges not detected as obstacles when going down)

## Key Namespaces
**Il2CppLast.\***: Battle (`BattleCommandSelectController`, `BattlePlayerData`), Map (`FieldController`, `MapRouteSearcher`), Entity (`FieldEntity`, `FieldPlayer`), UI (`Cursor`, `MessageManager`), Data.Master (`Command`, `Ability`, `Monster`), Data.User (`OwnedCharacterData`, `OwnedAbility`)

**Il2CppSerial.FF4.\***: UI.KeyInput (menu controllers, prefer over Touch), Map (`TransportationEvent`, `SubmarineController`)

## dump.cs Line References

| Category | Class | Line |
|----------|-------|------|
| Battle | BattleCommandSelectController | 397968 |
| Battle | BattleCommandSelectContentController | 397777 |
| Battle | BattleTargetSelectController | 399800 |
| Battle | BattlePlayerData / EnemyData / UnitData | 257675 / 473086 / 473223 |
| Battle | BattleResultData | 313247 |
| Battle | DamageCalcExecuteController | 491775 |
| Menu | Cursor | 387095 |
| Menu | MainMenuController | 413505, 445067 |
| Menu | ItemListController | 417584, 451948 |
| Menu | AbilityContentListController | 276112, 280480 |
| Menu | EquipmentWindowController | 416292, 450006 |
| Menu | ShopController | 427080, 463448 |
| Menu | PartySettingMenuBaseController | 396006 |
| Field | FieldController / FieldMap | 337664 / 262156 |
| Field | FieldPlayer / FieldEntity | 300515 / 299367 |
| Field | MapRouteSearcher / MapManager | 259163 / 342687 |
| Field | TransportationController | 349479 |
| Field | SubmarineController | 349268 |
| Data | OwnedCharacterData / CharacterParameterBase | 332119 / 310760 |
| Data | Command / Ability / Monster | 317248 / 314023 / 325613 |
| Message | MessageManager / MessageWindow | 368307 / 294662 |

## Common Patterns

```csharp
// Read character stats
var hp = $"{character.Parameter.CurrentHP}/{character.Parameter.ConfirmedMaxHp()}";
var mp = $"{character.Parameter.CurrentMP}/{character.Parameter.ConfirmedMaxMp()}";

// Read localized text
var message = IL2CppLast.Management.MessageManager.GetMessage(mesIdName);

// Harmony patch template
[HarmonyPatch(typeof(IL2CppSerial.FF4.UI.KeyInput.SomeController), nameof(SomeController.SelectContent))]
public static class SomeControllerPatch {
    public static void Postfix(SomeController __instance, int index) {
        FFV_ScreenReaderMod.SpeakText(text, interrupt: true);
    }
}

// One-frame delay (critical for UI state to update)
public static void Postfix() => CoroutineManager.StartManaged(DelayedAnnounce());
private static IEnumerator DelayedAnnounce() {
    yield return null;  // Wait one frame
    // Now read UI state
}
```

## System-Specific APIs

**Party** (PartySettingMenuBaseController): `defaultMembers`, `slot1/2/3Members`, `slotCount`

**Vehicles** (TransportationController): `currentTransport.messageId`, `currentTransport.type`, `TransportationInfo.OkList/LandingList`

**Submarine**: `IsWaitDiving`, `IsWaitSurface`, `ElapsedRatio`

## Debugging
Find logs: `Glob pattern="*.log" path="D:\Games\steamlibrary\steamapps\common\final fantasy iv pr\MelonLoader\Logs"` (pick most recent timestamped file). Use Read tool directly - terminal commands have path escaping issues.
