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

## Install

```powershell
powershell -ExecutionPolicy Bypass -File scripts\install.ps1
```

The script closes a running FocusTracker, publishes a Release build to
`%LOCALAPPDATA%\Programs\FocusTracker` and starts it. On start the installed copy adds itself
to `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. Run the script again to update.

## Data

Everything is stored in `%LOCALAPPDATA%\FocusTracker`:

- `focus_tracker.db` — the SQLite database;
- `settings.json` — settings from the «Налаштування» page (idle threshold);
- `logs\app-yyyyMMdd.log` — application log;
- `backups\focus_tracker-yyyyMMdd.db` — daily database backup made on start, the last 14 are kept;
- `focus_tracker.legacy-*.db` — databases from old incompatible versions, archived on upgrade.

To restore a backup, close FocusTracker, delete `focus_tracker.db`, `focus_tracker.db-wal` and
`focus_tracker.db-shm`, and copy the backup in place as `focus_tracker.db`.
