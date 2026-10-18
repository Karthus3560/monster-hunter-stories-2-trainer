# Changelog

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
