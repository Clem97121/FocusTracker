# FocusTracker

Desktop app for Windows (WPF, .NET 8) that tracks which program is in focus, shows usage
statistics, links tasks and skills to programs, and enforces time restrictions. It lives in
the system tray and starts with Windows.

## Build and test

Requires the .NET 8 SDK.

```powershell
dotnet build FocusTracker.sln -c Debug
dotnet test FocusTracker.Tests
```

Debug builds never register themselves for autostart, so running the app from the clone
does not affect the installed copy. Only one copy runs at a time, so close the installed
one (tray icon → «Вийти») before starting a Debug build.

## Install and update

Download `FocusTrackerApp-win-Setup.exe` from the latest
[GitHub release](https://github.com/Clem97121/FocusTracker/releases/latest) and run it.
The exe is not signed, so on first run Windows SmartScreen asks to confirm
(«Докладніше» → «Однаково запустити»). Setup installs the .NET 8 Desktop Runtime if it is
missing, installs the app to `%LOCALAPPDATA%\FocusTrackerApp`, adds Start menu and desktop
shortcuts and starts it. On start the app adds itself to
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`; uninstalling removes that entry.

The installed app checks GitHub Releases 30 s after start and then every 6 hours. When a new
version is found it shows a banner with an «Оновити» button and a tray notification; the
update can also be checked manually on the «Налаштування» page.

## Release

Push a tag `vX.Y.Z` on `main`:

```powershell
git tag v1.0.0
git push origin v1.0.0
```

The `release` workflow runs the tests, packs the app with [Velopack](https://velopack.io)
and publishes the GitHub release. Installed copies pick it up on their next check.

## Data

Everything is stored in `%LOCALAPPDATA%\FocusTracker`:

- `focus_tracker.db` — the SQLite database;
- `settings.json` — settings from the «Налаштування» page (idle threshold);
- `logs\app-yyyyMMdd.log` — application log;
- `backups\focus_tracker-yyyyMMdd.db` — daily database backup made on start, the last 14 are kept;
- `focus_tracker.legacy-*.db` — databases from old incompatible versions, archived on upgrade.

To restore a backup, close FocusTracker, delete `focus_tracker.db`, `focus_tracker.db-wal` and
`focus_tracker.db-shm`, and copy the backup in place as `focus_tracker.db`.
