# FF4 Screen Reader Mod

## Overview
Screen-reader/accessibility mod for FF4 Pixel Remaster. MelonLoader + Harmony patches hook game. Output via Tolk to NVDA.

## Features
- Menu/cursor navigation, character vitals (HP/MP), stats
- World navigation/pathfinding with obstacle detection
- Battle: turn order, targeting, damage/heals, status effects
- Victory screen: gil, items, XP, level-ups with stat growth
- Multiple vehicle support (Hovercraft, Enterprise, Falcon, Lunar Whale)

## TODO
- **Multi-phase Victory Screen**: Current implementation announces all victory info in a single message. Future enhancement should break this into phases (gil/items first, then per-character XP/level-ups) for better pacing and user control.

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
**ALWAYS** use this PowerShell command:
```
powershell -Command "& cmd /c 'D:\Games\Dev\Unity\FFPR\FF4\ff4-screen-reader\build_and_deploy.bat'"
```
- This is the ONLY command to use when building the mod
- The `cmd //c` syntax does NOT work reliably - use the PowerShell wrapper above
- NEVER use `dotnet build` or other build commands directly
- NEVER use `cd` with `&&` chaining
- The batch file handles build configuration, deployment, and logging correctly

## Rules
- **Never load** large files directly - use external search tools
- Max 50 lines from dump.cs unless user permits
- **Always check logs first** at game MelonLoader\Logs\
- **Reference FF6 mod source** (`ff6/ff6ScreenReader/FFVI_MOD`) for code patterns
- **Never edit** game or reference mod folders
- **Prefix all game classes** with `Il2Cpp`
- No duplicates - reference existing code

## Using dump.cs for Class Discovery
The file `D:\Games\Dev\Unity\FFPR\FF4\dump.cs` contains Il2Cpp class definitions (~18MB, 493K lines).
- **NEVER load the entire file** - use Grep tool to search for specific classes
- Search patterns: `class ClassName`, `namespace SomeNamespace`
- Example: `Grep pattern="class StatusDetailsController" path="D:/Games/Dev/Unity/FFPR/FF4/dump.cs"`
- Use line references in CLAUDE.md tables to jump to specific locations with offset/limit

## HARD BOUNDARIES - DO NOT ACCESS
- **NEVER access FF6 game installation directory** (steamapps/common/FINAL FANTASY VI PR)
- Only access FF6 mod SOURCE code at `ff6/ff6ScreenReader/FFVI_MOD` for reference

## DO NOT PORT (FF6-Specific)
- Esper/Magicite system (FF6 exclusive)
- Opera minigame patches
- Blitz command UI (Sabin)
- Tools command UI (Edgar)
- Trance system

## FF4-Specific Features
- **Fixed Party Slots**: 3-slot system with story-locked members
- **Multiple Vehicles**: Hovercraft, Enterprise, Falcon, Lunar Whale
- **Submarine/Diving**: SubmarineController for underwater sections
- **Magnetic Cave**: Metal equipment restrictions
- **Moon Areas**: Lunar Subterrane navigation

## Key Game Namespaces

### Core (Il2CppLast.*)
- **Battle**: `BattleCommandSelectController`, `BattlePlayerData`, `BattleEnemyData`
- **Map**: `FieldController`, `FieldMap`, `MapRouteSearcher`, `MapManager`
- **Entity**: `FieldEntity`, `FieldNonPlayer`, `FieldPlayer`
- **UI**: `Cursor`, `MessageManager`, `MessageWindowManager`
- **Data.Master**: `Command`, `Ability`, `Monster`, `Condition`
- **Data.User**: `OwnedCharacterData`, `OwnedAbility`, `OwnedJobData`

### FF4-Specific (Il2CppSerial.FF4.*)
- **UI.KeyInput**: Menu controllers (prefer over Touch)
- **Map**: `TransportationEvent`, `TelepoCacheLogic`, `SubmarineController`

## Key Class Line References (dump.cs)

### Battle System
| Class | Line |
|-------|------|
| BattleCommandSelectController | 397968 |
| BattleCommandSelectContentController | 397777 |
| BattleInfomationController | 399175 |
| BattlePlayerData | 257675 |
| BattleEnemyData | 473086 |
| BattleUnitData | 473223 |
| DamageCalcExecuteController | 491775 |
| BattleResultData | 313247 |
| BattleResultData.BattleResultCharacterData | 313303 |
| BattleTargetSelectController | 399800 |

### Menu System
| Class | Line |
|-------|------|
| Cursor | 387095 |
| MainMenuController | 413505, 445067 |
| ItemListController | 417584, 451948 |
| AbilityContentListController | 276112, 280480 |
| StatusWindowControllerBase | 284083, 285665 |
| EquipmentWindowController | 416292, 450006 |
| ShopController | 427080, 463448 |
| ConfigController | 414706, 447424 |
| PartySettingMenuBaseController | 396006 |

### Field/Navigation
| Class | Line |
|-------|------|
| FieldController | 337664 |
| FieldMap | 262156 |
| FieldPlayer | 300515 |
| FieldNonPlayer | 300265 |
| FieldEntity | 299367 |
| MapRouteSearcher | 259163 |
| MapManager | 342687 |
| PropertyGotoMap | 346778 |
| TransportationController | 349479 |
| BirdViewController | 348526 |
| SubmarineController | 349268 |

### Data/Character
| Class | Line |
|-------|------|
| OwnedCharacterData | 332119 |
| CharacterParameterBase | 310760 |
| OwnedAbility | 331994 |
| OwnedJobData | 332766 |
| Command | 317248 |
| Ability | 314023 |
| Monster | 325613 |

### Message/Text
| Class | Line |
|-------|------|
| MessageManager | 368307 |
| MessageWindow | 294662 |
| MessageWindowView | 296329 |
| MessageWindowController | 296194 |

## Common Patterns

### Read Character Stats
```csharp
var character = OwnedCharacterData;
var hp = $"{character.Parameter.CurrentHP}/{character.Parameter.ConfirmedMaxHp()}";
var mp = $"{character.Parameter.CurrentMP}/{character.Parameter.ConfirmedMaxMp()}";
var name = character.Name;
```

### Read Localized Text
```csharp
var message = IL2CppLast.Management.MessageManager.GetMessage(mesIdName);
```

### Harmony Patch Template
```csharp
[HarmonyPatch(typeof(IL2CppSerial.FF4.UI.KeyInput.SomeController), nameof(SomeController.SelectContent))]
public static class SomeControllerPatch {
    public static void Postfix(SomeController __instance, int index) {
        // Read from __instance.contentList[index]
        FFV_ScreenReaderMod.SpeakText(text, interrupt: true);
    }
}
```

### One-Frame Delay (Critical for UI)
```csharp
[HarmonyPostfix]
public static void Postfix() {
    CoroutineManager.StartManaged(DelayedAnnounce());
}

private static IEnumerator DelayedAnnounce() {
    yield return null;  // Wait one frame for UI to update
    // Now read UI state
}
```

## Party System Notes

FF4 has fixed party composition at story points:
```csharp
// PartySettingMenuBaseController (line 396006)
controller.defaultMembers  // Dict<int, List<int>> - slot -> required character IDs
controller.slot1Members    // Slot 1 members
controller.slot2Members    // Slot 2 members
controller.slot3Members    // Slot 3 members
controller.slotCount       // Number of available slots
```

## Vehicle System Notes

FF4 vehicles (TransportationController at line 349479):
```csharp
TransportationController.currentTransport.messageId  // Vehicle name
TransportationController.currentTransport.type       // Vehicle type ID
TransportationInfo.OkList        // Valid terrain types
TransportationInfo.LandingList   // Landing terrain types
```

Submarine (line 349268):
```csharp
SubmarineController.IsWaitDiving   // About to dive
SubmarineController.IsWaitSurface  // About to surface
SubmarineController.ElapsedRatio   // Transition progress (0-1)
```

## Log Files & Debugging

**Log directory:**
```
D:\Games\steamlibrary\steamapps\common\final fantasy iv pr\MelonLoader\Logs\
```

**Finding the most recent log:**
```
Glob pattern="*.log" path="D:\Games\steamlibrary\steamapps\common\final fantasy iv pr\MelonLoader\Logs"
```
Glob results are sorted by modification time - pick the most recent timestamped file (e.g., `26-1-6_5-44-33.log`). This is more reliable than trusting a file named "Latest.log".

**Reading logs:**
Use the Read tool directly with the full path. Terminal commands (cmd, PowerShell) have issues with spaces and special characters in paths.

**Build and deploy:**
```
powershell -Command "& cmd /c 'D:\Games\Dev\Unity\FFPR\FF4\ff4-screen-reader\build_and_deploy.bat'"
```

**Terminal limitations:**
- CMD and PowerShell commands often fail due to path escaping issues
- Prefer using Read/Glob tools for file access over terminal commands
- The `cmd //c` syntax does NOT work reliably

## Full Plan Reference
See `docs\ff4plan.md` for comprehensive porting plan with all phases.
