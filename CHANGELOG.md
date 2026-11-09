# Changelog

Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versioning follows [SemVer](https://semver.org/).

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

[1.2.0]: https://github.com/blnkDev/MultiRoblox/releases/tag/v1.2.0
[1.1.0]: https://github.com/blnkDev/MultiRoblox/releases/tag/v1.1.0
[1.0.0]: https://github.com/blnkDev/MultiRoblox/releases/tag/v1.0.0
