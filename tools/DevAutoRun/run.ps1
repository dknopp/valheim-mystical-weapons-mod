<#
.SYNOPSIS
  Hands-off test run: builds the mod and DevAutoRun, launches Valheim from the Gale test profile the way
  Gale does, waits for DevAutoRun to load the test world, probe and quit, then prints DevAutoRun's lines
  and every error or warning. -FullLog adds all of this mod's setup lines.
  Exit code 0 when every probe passed, 1 otherwise. Steam must be running.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\DevAutoRun\run.ps1
#>
param(
    [switch]$SkipBuild,
    [switch]$FullLog,
    # Longer than DevAutoRun's own two step timeouts, so it can log why it gave up
    [int]$TimeoutMinutes = 8,
    [string]$ValheimDir = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim'
)
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path "$PSScriptRoot\..\.."

if (-not $env:BepInExDir) { $env:BepInExDir = [Environment]::GetEnvironmentVariable('BepInExDir', 'User') }
$bepinex = $env:BepInExDir
if (-not $bepinex -or -not (Test-Path "$bepinex\core\BepInEx.Preloader.dll")) {
    throw 'Set the BepInExDir user environment variable to the Gale test profile''s BepInEx folder.'
}
$profileDir = Split-Path $bepinex -Parent
if (Get-Process valheim -ErrorAction SilentlyContinue) { throw 'Valheim is already running; close it first.' }

if (-not $SkipBuild) {
    foreach ($proj in "$repo\MysticalWeapons.csproj", "$repo\tools\DevAutoRun\DevAutoRun.csproj") {
        dotnet build $proj -c Release -v q -nologo
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $proj" }
    }
}

# Gale copies the doorstop loader from the profile into the game folder before each launch; do the same.
foreach ($f in '.doorstop_version', 'doorstop_config.ini', 'winhttp.dll', 'doorstop_libs') {
    if (Test-Path "$profileDir\$f") { Copy-Item "$profileDir\$f" $ValheimDir -Recurse -Force }
}

$steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam').SteamExe
$log = "$bepinex\LogOutput.log"
$started = Get-Date
Write-Host "Launching Valheim from $profileDir"
& $steam -applaunch 892970 --doorstop-enabled true --doorstop-target-assembly "$bepinex\core\BepInEx.Preloader.dll" -devautorun

$deadline = $started.AddMinutes($TimeoutMinutes)
$game = $null
while (-not $game -and (Get-Date) -lt $started.AddMinutes(1)) {
    Start-Sleep -Seconds 2
    $game = Get-Process valheim -ErrorAction SilentlyContinue | Select-Object -First 1
}
if (-not $game) { throw 'Valheim did not start within a minute. Is Steam running?' }
while (-not $game.HasExited) {
    if ((Get-Date) -gt $deadline) {
        Write-Warning "Timed out after $TimeoutMinutes minutes; killing Valheim."
        $game.Kill()
        break
    }
    Start-Sleep -Seconds 2
}

if (-not (Test-Path $log) -or (Get-Item $log).LastWriteTime -lt $started) { throw "No new log at $log" }
$lines = Get-Content $log
# Our entries, plus the lines that continue them (stack traces don't start with "[")
$inOurs = $false
$ours = $lines | Where-Object {
    if ($_ -match '^\[') { $inOurs = $_ -match ':Mystical Weapons' }
    $inOurs
}
$others = $lines | Where-Object { $_ -match '^\[(Error|Warning|Fatal)' -and $_ -notmatch ':Mystical Weapons' }
$shown = if ($FullLog) { $ours } else {
    $ours | Where-Object { $_ -match 'DevAutoRun:|Loaded \d+ mystical items|^\[(Error|Warning|Fatal)' }
}

Write-Host "`n--- Mystical Weapons and DevAutoRun ---"
$shown | ForEach-Object { Write-Host $_ }
if ($others) {
    Write-Host "`n--- Other errors and warnings ($(@($others).Count), first 20) ---"
    $others | Select-Object -First 20 | ForEach-Object { Write-Host $_ }
}

$result = $ours | Where-Object { $_ -match 'DevAutoRun: RESULT' } | Select-Object -Last 1
$ok = ($result -match 'fail 0$') -and -not ($ours -match 'DevAutoRun: FAILED')
Write-Host "`n$(if ($ok) { 'PASS' } else { 'FAIL' }) ($([int]((Get-Date) - $started).TotalSeconds)s)"
exit [int](-not $ok)
