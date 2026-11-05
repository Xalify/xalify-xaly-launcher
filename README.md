# Xaly Launcher

A vibe-coded game launcher for Windows: a store full of free games from itch.io, a library with play-time tracking, friends with encrypted chat, profiles, an in-game overlay (Shift+Tab) and automatic updates.

## Download

Get the newest `XalyLauncher-x.y.z.zip` from **[Releases](../../releases/latest)**, unzip it anywhere you can write to (for example your Documents folder) and run `Xaly Launcher.exe`. Windows SmartScreen may warn the first time because the app isn't code-signed: click **More info › Run anyway**.

After that the launcher updates itself: when a new release is published here it offers **Update now**, then closes, swaps its files and reopens. Accounts, libraries, friends and the store catalog live in `%AppData%\XalyLauncher` and are never touched by updates.

## What's in this repo

| File | What it is |
|---|---|
| `Xaly.cs` | The Windows app (C#, WinForms). Hosts the UI, downloads, unzipping, overlay, notifications, encryption and the self-updater. |
| `index.html` | The whole interface (HTML/CSS/JavaScript, written for the built-in Internet Explorer 11 engine, so ES5 only). |
| `xaly.ico` | App icon. |
| `Build.bat` | Builds `Xaly Launcher.exe` on your own PC with the C# compiler that ships with Windows. |
| `release-notes.md` | The notes shown in the next release and in the in-app update dialog. |
| `.github/workflows/release.yml` | Builds and publishes a release on GitHub. |

## Building it yourself

Double-click `Build.bat`. It compiles `Xaly Launcher.exe` next to the source and starts it.

Installing games from itch.io uses an itch.io API key. A public key from a throwaway itch.io account is built into `Xaly.cs`, so installs work out of the box. Players can use their own key instead in **Settings › Downloads** (or an `itchkey.txt` file next to the `.exe`).

## Publishing a new version

1. Change `var VER="x.y.z"` near the top of the script in `index.html` (and add an entry to `NOTES` for the in-app patch notes).
2. Write what changed in `release-notes.md`.
3. Commit and push to `main`.
4. On GitHub open **Actions › Release › Run workflow**. Or push a tag such as `v1.4.1`.

The workflow builds the `.exe` on Windows, packs it with `index.html` and `xaly.ico`, publishes the release with a SHA256 checksum, and every running launcher offers the update within a few hours (or straight away via **Settings › About › Check for updates**).

## Data and privacy

- Everything is stored locally in `%AppData%\XalyLauncher`. Accounts (including the email/password sign-in) exist only on that PC.
- Friends, status and chat travel through the public relay [ntfy.sh](https://ntfy.sh). Status, profiles and chat are end-to-end encrypted (ECDH P-256, AES-256, HMAC-SHA256).
- Feedback is emailed to the developer through FormSubmit.
