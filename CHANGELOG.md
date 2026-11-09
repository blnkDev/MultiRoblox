# Changelog

Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versioning follows [SemVer](https://semver.org/).

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

[1.1.0]: https://github.com/blnkDev/MultiRoblox/releases/tag/v1.1.0
[1.0.0]: https://github.com/blnkDev/MultiRoblox/releases/tag/v1.0.0
