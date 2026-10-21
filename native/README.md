# Native Studio and launcher

This directory contains the release's Windows launcher, native gameplay/UI module, custom Camp icon, loader bridge, shared settings/save-backup code, assets and Inno Setup definition. It contains no original game archives, saves or session snapshots.

## Build on Windows x64

Install the .NET 10 SDK, CMake 3.20+, Visual Studio 2022 Build Tools with Desktop development with C++, and optionally Inno Setup 6. Run PowerShell 7 from this repository:

```powershell
./native/build.ps1 -Installer
```

Close the game and launcher before running the installer-policy checks. The script builds the three native modules, runs engine/menu/launcher checks, publishes a self-contained launcher, calculates module and ownership manifests, copies runtime license notices, and optionally builds Setup. Outputs stay under ignored `native/out/`.

The installer is in `native/out/installer/`; the unpackaged payload is in `native/out/payload/`. If CMake or Inno Setup are not on PATH, add CMake to PATH and pass `-InnoCompiler "C:/path/to/ISCC.exe"`.

Builds from source have fresh toolchain timestamps and hashes. The published 1.1.0 installer is the locally validated release artifact, with its SHA-256 in the GitHub Release. Do not mix DLLs or manifests between builds.

## Layout

- `launcher/`: WPF launcher, Steam discovery, session loader and installation ownership checks.
- `battle/`: native Camp integration and 29 gameplay controls, with isolated tests.
- `icon/`: independent custom Camp emblem module.
- `bridge/`: minimal full-width LoadLibrary request bridge.
- `shared/`: executable/session verification, atomic settings/profiles and verified backup service.
- `assets/`: approved UI atlas/emblem and static supported-build verification template.
- `installer/`: per-user installer, update/repair and owned-file uninstall policy.

The launcher starts through Steam, waits for original menu resources, validates executable/routines/callback ownership, creates a backup, then loads both modules. PID plus process-creation guards and one-attempt markers bind each load to one game session. It never replaces the game's input DLL. The backup service runs only for that game session and a matching explicitly queued post-exit save restore.

The exact tested Steam executable is required. Do not disable guards to support another game version. Trace and verify a new version independently before updating the validation template. See `USER-GUIDE.txt` for end-user instructions.
