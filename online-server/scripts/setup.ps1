# Low Strike online server: first-time setup on the server PC (Windows).
# Makes the Python environment (.venv), installs the requirements, and writes .env from .env.example with two new
# random secrets, the game version, and the path of the game build next to it (the server package's game folder).
# Run it again any time: an existing .env is kept.
#
#   powershell -ExecutionPolicy Bypass -File scripts\setup.ps1

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# Python 3.10 or newer that really runs (Windows has a "python" that only opens the Microsoft Store).
function Test-Python([string[]]$command) {
    try {
        $version = & $command[0] $command[1..9] -c "import sys; print('%d.%d' % sys.version_info[:2])" 2>$null
        return ($LASTEXITCODE -eq 0 -and $version -and [version]$version -ge [version]"3.10")
    }
    catch { return $false }
}
$python = $null
foreach ($candidate in @(@("py", "-3"), @("python"), @("python3"))) {
    if ((Get-Command $candidate[0] -ErrorAction SilentlyContinue) -and (Test-Python $candidate)) { $python = $candidate; break }
}
if ($null -eq $python) {
    Write-Host "Python 3.10 or newer is not installed. Install it, then run this again:"
    Write-Host "  winget install -e --id Python.Python.3.13"
    Write-Host "  (or from https://www.python.org/downloads/ with 'Add python.exe to PATH' ticked)"
    exit 1
}

# Python's packages have long paths inside; Windows (without "long paths" on) stops at 260 characters.
if ($root.Length -gt 90) {
    Write-Host "This folder's path is too long for Python's packages: move the server to a short one, e.g. C:\LowStrike-Server"
    exit 1
}

if (-not (Test-Path ".venv\Scripts\python.exe")) {
    Write-Host "Making the Python environment (.venv)..."
    & $python[0] $python[1..9] -m venv .venv
    if ($LASTEXITCODE -ne 0) { exit 1 }
}
Write-Host "Installing the requirements..."
& ".venv\Scripts\python.exe" -m pip install --upgrade --quiet --disable-pip-version-check -r requirements.txt
if ($LASTEXITCODE -ne 0) { exit 1 }

function New-Secret {
    $bytes = New-Object byte[] 48
    $random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $random.GetBytes($bytes)
    $random.Dispose()
    return [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

if (Test-Path ".env") {
    Write-Host ".env exists already: kept as it is."
}
else {
    $text = Get-Content ".env.example" -Raw
    $text = $text.Replace("AUTH_SECRET=REPLACE_WITH_STRONG_RANDOM_SECRET", "AUTH_SECRET=" + (New-Secret))
    $text = $text.Replace("GAME_JOIN_SECRET=REPLACE_WITH_DIFFERENT_STRONG_RANDOM_SECRET", "GAME_JOIN_SECRET=" + (New-Secret))
    # The server package: the game build in game\ and its version in VERSION.txt, next to this folder.
    $game = Join-Path (Split-Path -Parent $root) "game\LowStrike.exe"
    if (Test-Path $game) { $text = $text.Replace("UNITY_SERVER_EXECUTABLE=PATH_TO_UNITY_BUILD", "UNITY_SERVER_EXECUTABLE=" + (Resolve-Path $game)) }
    $versionFile = Join-Path (Split-Path -Parent $root) "VERSION.txt"
    if (Test-Path $versionFile) { $text = $text.Replace("GAME_BUILD_VERSION=REPLACE_ME", "GAME_BUILD_VERSION=" + (Get-Content $versionFile -TotalCount 1).Trim()) }
    [System.IO.File]::WriteAllText((Join-Path $root ".env"), $text, (New-Object System.Text.UTF8Encoding $false))
    Write-Host "Wrote .env with two new random secrets (keep this file private)."
}

New-Item -ItemType Directory -Force "data", "logs" | Out-Null
$missing = @()
$current = Get-Content ".env" -Raw
if ($current -match "YOUR_PLAYIT_HOST") { $missing += "  ROOM_SLOTS_JSON           the address playit.gg gives your UDP tunnel (README step 2)" }
if ($current -match "PUBLIC_API_URL=https://api.example.com") { $missing += "  PUBLIC_API_URL            the tunnel's https address for the API (README step 3)" }
if ($current -match "UNITY_SERVER_EXECUTABLE=PATH_TO") { $missing += "  UNITY_SERVER_EXECUTABLE   the Windows build of Low Strike (LowStrike.exe)" }
if ($current -match "GAME_BUILD_VERSION=REPLACE") { $missing += "  GAME_BUILD_VERSION        the game's version, e.g. 1.19" }
Write-Host ""
if ($missing.Count -gt 0) {
    Write-Host "Still to fill in, in online-server\.env:"
    $missing | ForEach-Object { Write-Host $_ }
}
Write-Host "Setup done. Next: README.md, steps 2 to 5."
