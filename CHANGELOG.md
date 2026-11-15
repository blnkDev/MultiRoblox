# Changelog

Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versioning follows [SemVer](https://semver.org/).

## [1.3.0] - 2026-11-11

### Added
- **Appearance** window (gear button in the title bar, or the tray menu): dark or light theme, eight accent colors, and switches for animations and the Mica background. Changes apply instantly and are remembered.
- Mica background: the window shows your desktop wallpaper, blurred and tinted with the theme, like Windows 11 apps (works on Windows 10 too). Off by default.
- Animations: rounded buttons with a soft hover and press, account rows that fade when hovered or selected, a "ping" around the green dot of accounts that are in a game, and status messages that fade in. They can be turned off.
- **Open in browser** (replaces the Nickname button): opens roblox.com signed in as the selected account, in a temporary browser window of your default browser, without touching your own browser profile. The temporary profile, which holds the login, is deleted when that window is closed (leftovers are cleaned up the next time MultiRoblox opens). Also in the right-click menu and on `Ctrl`+`B`.
- Each open client shows how long it has been open (`open · 1h 05m`).
- Keyboard shortcuts: `Enter` plays the selected accounts, `Del` removes them, `F2` edits the nickname, `Ctrl`+`A` selects all, `Ctrl`+`N` adds an account, `Ctrl`+`T` tiles the windows, `Ctrl`+`B` opens the account in the browser, `F5` refreshes the online status. Buttons show their shortcut in a tooltip.
- The window remembers its size and position (and whether it was maximized). A position that is no longer on any screen is ignored.
- A hint in the empty account list explains how to add the first account.

### Changed
- Sharp at any display scale: MultiRoblox is now DPI-aware, so text and layout are crisp at 125 %, 150 % and above instead of a blurry stretched bitmap.
- Selected accounts get an accent marker on the left, and the MULTI ON / WAITING badge is rounded.
- The Nickname button is gone: nicknames are edited with a double-click, `F2` or the right-click menu.
- Settings live in `settings.txt` in the data folder.

## [1.2.1] - 2026-11-10

### Fixed
- Accounts are never lost to a bad file: saving is atomic and keeps the previous file as `accounts.dat.bak`; an unreadable `accounts.dat` is kept aside as `accounts.dat.broken-<date>`, the backup is restored when possible, and the app tells you.
- A failed save no longer crashes the program (it is written to the log).
- An unexpected error no longer closes the program: it is written to the log and shown in the status line.
- Tabs or line breaks in a name or cookie can no longer corrupt the accounts file.
- Closing the program can no longer fail while releasing the multi-client lock.
- Network calls time out after 20 seconds instead of 100, and a Roblox outage or rate limit (HTTP 429/5xx) is no longer reported as "game not found" or "user not found".
- Unexpected or empty replies from Roblox no longer break the game lookup, avatars or Follow user.
- Closing the window while an account is being added or launched no longer causes errors.
- `log.txt` keeps the previous megabyte as `log-old.txt` instead of deleting it.

## [1.2.0] - 2026-11-09

### Added
- Profile pictures: each account shows its Roblox avatar (public thumbnail, cached in `avatars\` and refreshed every 12 hours; removing an account deletes its picture), with a status dot on it: green in game, blue online, orange in Studio, gray offline. The colored initial stays as the fallback.
- Optional password lock: MultiRoblox asks for the password when it opens. Set, change or remove it from the tray icon menu, where **Lock now** hides the window until the password is typed again. It only locks the window; accounts stay protected by Windows DPAPI.
- **Open log** button (and tray menu entries for the log and the data folder).
- Right-click menu on accounts with **Close client**, which closes just that account's Roblox client (only clients opened by MultiRoblox).
- Clearer errors: each account now says why launching failed (expired login, rate limit, locked or restricted account, no connection), and a failed game lookup tells a connection problem apart from a wrong Place ID.
- `MULTIROBLOX_DATA` environment variable to use another data folder (for testing).

### Changed
- Minimizing now keeps the window in the taskbar like any other app, instead of hiding it in the system tray. The tray icon stays for its menu.
- Nicknames are now edited in place, right on the account row (Enter or clicking away saves, Esc cancels), instead of in a separate window.
- The hint text under the account list was removed.
- `log.txt` is trimmed automatically when it grows past 1 MB.

## [1.1.0] - 2026-11-09

### Added
- Reorder accounts by dragging them, or with `Alt`+`Up`/`Down` (moves all selected). The order is saved.
- Update notice: checks the latest GitHub release on startup and every 6 hours and shows an **Update** button in the title bar. Tray menu: "Check for updates now" and "Check for updates automatically" (on by default). Nothing is downloaded or installed automatically.

### Fixed
- The window no longer shows a classic Windows title bar over the custom one when it loses focus.

## [1.0.0] - 2026-11-08

First public release.

### Added
- Multi-client: holds `ROBLOX_singletonMutex` and `ROBLOX_singletonEvent` so several Roblox clients can run at once.
- Account manager with sign-in through the default browser (Firefox, Edge, Chrome, Brave, Vivaldi), no stored passwords; cookie encrypted with DPAPI.
- Per-account nicknames.
- Launch several accounts at once, confirming that each client started.
- Game by Place ID with name, icon and live player count; favorite games; last game remembered.
- Follow user (a friend or one of your other accounts).
- Online status for each account.
- Tile the Roblox windows in a grid.
- Login validation of the accounts on startup.
- Dark UI with custom title bar, custom icon and system tray.
- Development options: `--demo`, `--selftest`, `--logintest`, `--makeico`.

[1.3.0]: https://github.com/blnkDev/MultiRoblox/releases/tag/v1.3.0
[1.2.1]: https://github.com/blnkDev/MultiRoblox/releases/tag/v1.2.1
[1.2.0]: https://github.com/blnkDev/MultiRoblox/releases/tag/v1.2.0
[1.1.0]: https://github.com/blnkDev/MultiRoblox/releases/tag/v1.1.0
[1.0.0]: https://github.com/blnkDev/MultiRoblox/releases/tag/v1.0.0
