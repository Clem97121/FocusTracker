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

## Sync between devices

Usage statistics of all devices can be shared through a Supabase (Postgres) database:

1. Create a Supabase project. In the project press **Connect** and copy the **Session pooler**
   connection string in the .NET format (the direct connection is IPv6-only).
2. In FocusTracker open «Налаштування» → «Синхронізація між пристроями», set the device name,
   paste the connection string, tick «Синхронізувати» and press «Зберегти».

On the first sync the app creates its `ft_*` tables (Row Level Security enabled, so they are
not exposed through the Supabase Data API). Every minute it:

- uploads hourly usage rows changed on this device and downloads rows of the other devices
  into the local `RemoteHourlyUsages` table, so statistics also work offline. The statistics
  page gets a device selector; daily limits count the time of all devices;
- syncs program settings (category, tracked, hidden, ignore idle) in `ft_programs` and program
  icons in `ft_program_icons`;
- syncs skill categories, skills, tasks (with their programs), restrictions and XP history as
  JSON documents in `ft_entities`, including deletions.

Conflicts are resolved per item: the most recent change wins. Task progress (`TaskProgramUsages`)
is counted on the device where the task is worked on. If the same skill gets XP on two devices
while one of them is offline, only the later change is kept.

The connection string is stored in `settings.json` encrypted with Windows DPAPI for the current
user.

## Data

Everything is stored in `%LOCALAPPDATA%\FocusTracker`:

- `focus_tracker.db` — the SQLite database;
- `settings.json` — settings from the «Налаштування» page (idle threshold);
- `logs\app-yyyyMMdd.log` — application log;
- `backups\focus_tracker-yyyyMMdd.db` — daily database backup made on start, the last 14 are kept;
- `focus_tracker.legacy-*.db` — databases from old incompatible versions, archived on upgrade.

To restore a backup, close FocusTracker, delete `focus_tracker.db`, `focus_tracker.db-wal` and
`focus_tracker.db-shm`, and copy the backup in place as `focus_tracker.db`.
