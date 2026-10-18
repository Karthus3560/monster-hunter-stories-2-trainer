# Contributing to Kinship Studio

Bug reports, build fixes, UI improvements and carefully verified game compatibility work are welcome.

## Build

Use Windows x64, PowerShell 7 and the .NET 10 SDK. Run `pwsh ./scripts/build.ps1`. It publishes a self-contained app and runs portable engine checks. Add `-FullTests -UiTests` only with the inspected game installed and closed.

For installer builds, install Inno Setup 6.7+ and add `-Installer`. Run `pwsh ./scripts/test-installer.ps1` for isolated upgrade checks. Preserve the permanent AppId, per-user install mode, unversioned app folder and shortcut. See [updates and signing](docs/UPDATES-AND-SIGNING.md).

## Changes worth reviewing

- Describe the player-visible behavior and the supported game build.
- Preserve compatibility proofs, owner checks, range validation and restoration guards. Do not force writes through failed checks.
- For save editing, keep fresh verified backups, concurrent-change checks, checksum validation, atomic replacement and guarded Undo.
- Keep controls compact, keyboard-accessible and readable in Stories light and Moonlit hunt.
- Include focused verification for changed behavior. Do not claim mock-memory tests prove live gameplay.

## Screenshots

The app can render its own UI without connecting to the game:

```powershell
./artifacts/publish/RidersToolkit.exe --screenshot "$PWD/battle.png" --theme white --page Battle --frame
./artifacts/publish/RidersToolkit.exe --screenshot "$PWD/overlay.png" --theme night-camp --overlay --page Movement
```

Do not commit personal saves, local backup folders, process dumps, Steam-account data, compiled game archives or downloaded third-party trainers/tables. Use temporary fixtures for reproducible tests. Check screenshots and logs for private information before posting.

Pull requests should describe the change, the reason and what was verified. New game layouts need exact version/hash information and independently reviewable evidence, not guessed addresses.
