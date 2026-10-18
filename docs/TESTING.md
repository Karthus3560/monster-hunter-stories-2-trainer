# Testing Kinship Studio

## Public CI / portable checks

`pwsh ./scripts/build.ps1` publishes the app, then invokes `RidersToolkit.exe --self-test --portable`. This runs **95 fixture-based engine checks** and explicitly skips five installed-game checks. It does not open the game or require an uploaded game archive. Reports are stored in local application-data/RidersToolkit and copied into the build output.

Portable coverage includes guarded memory maintenance/restoration, movement, reward scaling, native routine rejection, retreat/radar/gathering logic, input-owner/dialogue handling, synthetic save encryption/checksums, empty-carton additions, backups, failed/concurrent writes, exact Undo and window-mode fixtures.

The five excluded checks concern workshop/archive rebuilding, all-entry archive/schema preservation, rainbow/rare-only generation settings, luck install/update/recovery on copies and installed item metadata. They need data from a legitimate local game installation. The `SKIP` lines remain in the report; skipped checks are not counted as passes.

The workflow is included under `.github/workflows/build.yml`. At first publication, the hosted Windows job did not start; a successful hosted run is not claimed. See [Actions](https://github.com/Karthus3560/monster-hunter-stories-2-trainer/actions) for current status. The local results below are separate from hosted CI.

## Full local validation

With the supported game installed and closed:

```powershell
pwsh ./scripts/build.ps1 -FullTests -UiTests
```

For v3.4.0, the author ran **100/100 engine checks and 120/120 UI checks**. UI checks use preview windows, not live writes to the game. They cover editor opening/reopening, backup gates, light/night updates, assets/font loading, caption-color API acceptance, responsive card layout, control bounds, overlay hotkeys/focus logic and native topmost/borderless test windows.

Automated checks are not a substitute for gameplay observation on another game build. The author's gameplay confirmation and exact supported version are documented separately from synthetic/native-window checks.

## Preview captures

The `--screenshot` CLI renders the trainer's own UI with game connection disabled. `--frame` includes its own native Windows caption. `--studio` opens an unedited offline editor; `--add-egg` uses a disconnected preview. It does not open a real save automatically.

Never add private save files or game binaries as test fixtures. Use generated temporary data and preserve backup/refusal behavior when adding tests.
