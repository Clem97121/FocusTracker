# Встановлює Release-збірку FocusTracker у постійну папку і запускає її.
# Запущений застосунок сам пропише себе в автозапуск (HKCU\...\Run).
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\FocusTracker')
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'FocusTracker.App\FocusTracker.App.csproj'

# Запущена копія тримає файли, тож спершу закриваємо її
$running = Get-Process -Name 'FocusTracker.App' -ErrorAction SilentlyContinue
if ($running) {
    Write-Host 'Закриваю запущений FocusTracker...'
    $running | Stop-Process -Force
    $running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
}

Write-Host "Збираю Release у $InstallDir..."
dotnet publish $project -c Release -o $InstallDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish завершився з кодом $LASTEXITCODE" }

$exe = Join-Path $InstallDir 'FocusTracker.App.exe'
Write-Host "Запускаю $exe"
Start-Process -FilePath $exe -WorkingDirectory $InstallDir
