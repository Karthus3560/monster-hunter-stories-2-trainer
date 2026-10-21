# Native Studio Launcher 1.1.0 � 21 October 2026

- New self-contained Windows launcher with Steam detection, Browse and normal game startup.
- Automatically waits for verified menu readiness and loads native Studio plus the custom Camp icon.
- Native Camp integration with eight centered tabs, 29 gameplay controls and five Main shortcuts.
- Reset all with confirmation; settings restoration, five profiles and verified save-backup controls.
- Separate gathering, battle loot, ordinary chest, EXP and earned Zenny multipliers.
- Egg Hunt presets and crafting-material preservation, within the limits in the main guide.
- New per-user installer, update/repair and owned-file uninstall; personal settings and backups preserved.
- A supported already running session can be adopted without duplicate hooks.
- No game-local input DLL replacement or permanent Windows-startup watcher.

This is the native launcher product line; the desktop trainer's 3.x version numbers are separate. Existing desktop releases remain available.

---

# Changelog

## 3.5.2 — public documentation cleanup

- Simplify artwork notices while preserving ownership and third-party license notices.
- Keep private authoring material out of source packages.
- Gameplay features and the installed app identity stay unchanged.


## 3.5.1 — release packaging

- Remove developer debug paths and symbols from published Release binaries.
- Keep the same installer identity, app folder, settings and update flow.


## 3.5.0 — install once, update in place

- Per-user Setup installer adopts the existing app folder and shortcut, reuses one permanent Windows app identity and refuses downgrades.
- Profiles & hotkeys → Check for updates offers explicit download and restart, using exact project assets, bounded downloads and SHA-256 verification.
- Profiles, theme and save backups stay separate from installed program files.
- Windows SDK signing support is prepared for an issuer-provisioned certificate, covering app, installer and uninstaller. Public files are still unsigned until that certificate/service exists.
- 110 engine checks, 123 UI checks and 11 isolated installer checks passed locally; portable engine coverage is now 105 checks.


## 3.4.0 — first public release

- Public MIT-licensed C# / WPF source, Windows x64 download and build instructions.
- 31 live battle, movement, exploration and inventory controls.
- Alt+W quick controls and F12 stop-all.
- Rainbow egg, den-generation and workshop presets, with original-archive restoration.
- Offline Egg & Gene Studio: add eggs to an empty carton, species/genes/stars/locks, Rainbow Genes and board rearrangement.
- Fresh verified backups before save edits, checksum/encryption validation, atomic replacement and guarded Undo.
- Stories light and Moonlit hunt themes, original coastal pixel illustration, Navirou mascot and embedded Silkscreen display font.
- Matching native Windows caption, integrated sidebar credit/theme controls and responsive control deck.
- 100 engine checks and 120 interface checks passed locally. Public CI runs the 95 portable engine checks; five archive-based checks require the game.

This is the first public GitHub release of a project developed and tested locally. It does not claim to be the first trainer or save editor made for the game.
