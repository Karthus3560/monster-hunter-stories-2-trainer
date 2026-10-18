# Source map

Kinship Studio is a C# / .NET 10 WPF desktop application, published as a self-contained Windows x64 executable. The internal assembly/storage name remains `RidersToolkit`.

| Area | Main files |
| --- | --- |
| Controls and view models | `Models.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs` |
| Automatic connection and layout proofs | `AutomaticTrainerEngine.cs`, `AutomaticLayout.cs`, `Memory.cs` |
| Gameplay maintenance/restoration | `MovementController.cs`, `ExplorationController.cs`, `AdventureController.cs`, `ExtraController.cs` |
| Steam discovery, profiles and verified backups | `SteamInstallation.cs`, `Storage.cs` |
| Egg/den/workshop archive presets | `GameDataMods.cs`, `GameArchive.cs` |
| Offline save container and editor | `SteamSaveCodec.cs`, `OfflineEggEditor.cs`, `StableEditor.cs` |
| Egg/gene UI | `StableEditorWindow.cs`, `AddEggWindow.cs` |
| Quick controls and native window helper | `OverlayController.cs`, `OverlayWindow.xaml`, `BorderlessGameWindow.cs` |
| Appearance and preview rendering | `ThemeManager.cs`, `WindowLayout.cs`, `FeatureCardWidthConverter.cs`, `OwnWindowCapture.cs` |
| App installation and updates | `AppUpdates.cs`, `AppUpdateWindow.cs`, `installer/KinshipStudio.iss`, `scripts/build.ps1`, `scripts/sign.ps1` |
| Tests | `*Tests.cs`, `MainWindowAppearanceChecks.cs`, `MainWindowOverlayChecks.cs` |

The production egg editor uses disk saves with the game closed. Legacy/internal live editor code remains for fixture coverage and shared planning; the production UI routes through `OfflineEggEditor`.

Game mutations are scoped to supported structures and expected bytes. Restoration checks that the trainer still owns the values. Save writes validate the existing encrypted file, create a fresh backup, stage and verify the new encrypted bytes, then replace atomically. Native window operations are separate from game memory and save editing.

Third-party cryptography, save-format research, factual labels and the display font are credited in `THIRD-PARTY-NOTICES.md`. No installed-game data, decrypted saves or external trainer executable is part of this repository.
