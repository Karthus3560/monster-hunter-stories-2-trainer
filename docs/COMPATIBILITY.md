# Compatibility and behavior

## Supported target

- Monster Hunter Stories 2: Wings of Ruin, Steam Windows x64, inspected executable version **1.6.1.0**.
- Inspected executable SHA-256: `AC73BBA1019F09DE83CA8466C4856B3937E9D63972F5FBCA2BCBC670437B7DA9`.
- Native Windows WPF desktop app. The downloadable build includes its .NET runtime; building requires the .NET 10 SDK. Matching native caption colors use Windows 11 support; older systems keep their supported frame behavior.
- Use in single-player. Multiplayer, console, emulated and altered-executable configurations are not supported targets.

## Live controls

Controls start **off**. The app resolves structures automatically and checks expected native routines and owners. Armed controls apply only when their required context exists. Incompatible game layouts stop the operation.

Health values are maintained within the supported game's native fields. Movement sliders restore normal values when switched off or Reset is used. Damage changes allied attack values; defense and skill calculations still affect the final hit. EXP and loot apply to future rewards rather than retroactively multiplying past gains.

**100% retreat** applies when the game allows retreat; bosses, story encounters and other restrictions can prevent it. Radar reveals loaded markers. Increased gathering range and auto-collect concern ordinary material points; eggs/chests remain manual. Flight/swimming speed does not unlock riding abilities.

Item controls maintain owned stacks within natural capacities. They do not create every item, unlock all equipment or remove quest restrictions. Ordinary battle and field item preservation use different paths; shops/crafting are separate.

## Overlay

Alt+W opens/closes quick controls over a **windowed or borderless** game. The trainer can convert a normal windowed game into borderless and restore its former window. True exclusive fullscreen is not supported by this desktop overlay. The panel remains pinned if the game takes focus and dismisses for unrelated apps. Esc closes it while it has keyboard focus.

## Egg, den and workshop settings

Apply/restore with the game closed. New generation settings affect fresh nests, pulls and dens. An already-generated area or existing egg can retain its old state. Rainbow egg quality and a Rainbow Gene are separate features. Free crafting/forging does not bypass every recipe, progression or story condition. Restoring original game data is separate from F12's live-control stop.

## Offline Egg & Gene Studio

The game must be closed. Manual slots 1–3 are supported. The entire file is checked before and after backup and immediately before replacement. Steam Cloud or another process changing it blocks the write. Every Apply/Add/Undo backs up the saves first.

Adding an egg works in an empty carton; capacity is 12 saved eggs. New eggs use the species' starter data and normal quality. The editor preserves encryption identity and unrelated records. Story-only Monsties absent from the hatchable-species catalog are preserved and omitted. Unsupported structures are refused. Undo is available only while the file still matches the preceding edit.

## Scope

The current release is based on the author's tested installation and user-confirmed gameplay. Different game builds, ongoing Steam Cloud changes, mods or unusual encounter contexts can alter behavior. Report exact versions and reproducible steps rather than assuming compatibility across versions.
