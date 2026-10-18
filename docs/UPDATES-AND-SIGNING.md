# App updates and code signing

## Install once, upgrade the same app

Starting with **v3.5.0**, the recommended download is `KinshipStudio-v3.5.2-Setup.exe`. Run it once to create or adopt the installed copy. It uses the existing per-user `local application-data/Programs/RidersToolkit` folder and the existing **Kinship Studio (Stories 2)** desktop shortcut. Administrator access is not requested.

The installer uses the permanent identity `KinshipStudio.GhaithSaadJawad`, the same installation mode and one unversioned Windows installed-app entry. Future installers reuse that identity and folder, update the existing executable and shortcut, and reject downgrades. Do not put a version number in `AppId` or change the install mode in later releases.

In the app, open **Profiles & hotkeys → Check for updates**. This checks the latest stable release in this project's GitHub repository. If a newer installer is ready, choose **Download update**, then **Restart & update**. The app restores its live controls before handing over to Setup. Finish game-data operations and close Egg Studio first. Setup upgrades the installed app and opens it again.

The updater requires an exact installer URL within this repository's release, a valid SHA-256 digest from GitHub release metadata, a bounded file size, and a matching complete download. Partial/corrupt downloads do not become runnable installers. The file is checked again immediately before launch. GitHub repository/release access remains part of the update trust boundary. A checksum verifies integrity against that metadata; it is not an independent publisher signature.

The separate portable ZIP remains available. Extracting ZIPs into different folders naturally creates separate portable copies. A portable copy's update dialog explains that Setup installs/upgrades the main copy; it does not delete the portable folder.

Profiles, theme preferences, backups, editor journals and restored-game-data copies remain in the separate `local application-data/RidersToolkit` folder. Installer upgrades do not overwrite or remove that folder. Uninstalling the app also keeps this user data. Remove the app through Windows Settings → Apps if needed.

## Current code signing policy

**The v3.5.2 public release is currently unsigned.** No publicly trusted publisher certificate is provisioned for this project. The update feature works independently of code signing; it does not remove Windows publisher/reputation checks.

Creating a self-signed certificate does not make Windows trust the publisher on other computers. This project does not install a custom trusted root or ask users to disable Windows protections. A publicly trusted signature requires an identity-validated certificate/service or acceptance by an approved open-source signing program.

[SignPath Foundation](https://signpath.org/) offers a free open-source route, subject to [its conditions and project review](https://signpath.org/terms.html). The certificate publisher in that program is SignPath Foundation. This project has not been accepted or sponsored by it. A commercial code-signing provider is another route if a certificate in the maintainer's verified legal identity is preferred. Provider identity checks, eligibility and any purchase must be completed by the maintainer.

Maintainer, release author and signing approver: **Ghaith Saad Jawad** (`Karthus3560`). Contributions and release scripts must remain reviewable in this repository. Private signing keys, certificate passwords, hardware-token credentials and provider tokens must never be committed or included in release assets.

## Prepared signing support

For an issuer-provisioned certificate in the current user's Windows personal certificate store, with its private key accessible through its provider/token:

```powershell
$env:KINSHIP_SIGNING_THUMBPRINT = 'YOUR_40_HEX_CHARACTER_CERTIFICATE_THUMBPRINT'
pwsh ./scripts/build.ps1 -Installer
```

This invokes the Windows SDK SignTool with SHA-256 file and timestamp digests. Inno Setup signs both Setup and its uninstaller, and the build signs the app before packaging. Every signing operation must return a **Valid** Windows Authenticode status. A missing, expired or untrusted certificate stops the signed build; it never silently substitutes an unsigned file. `SIGNTOOL_PATH` and `ISCC_PATH` may point to installed official build tools. No private-key export is required.

A hosted signing service needs its own provider integration once approved. Do not add a sponsorship badge or claim a signed release until the actual published files validate successfully. A signature identifies the publisher and protects file integrity; it does not guarantee a particular SmartScreen reputation or antivirus result.

## Network behavior

Update requests happen only when the user opens update checking or requests a download. The app sends ordinary HTTPS requests, including a product/version user-agent, to GitHub's release API and download/CDN infrastructure. GitHub receives normal connection information such as IP address under its own [privacy policy](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement). The updater sends no save files, game memory, profiles or Steam-account information. It does not install updates in the background.
