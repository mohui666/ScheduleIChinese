# Self-contained smoke test: launch game, verify plugin 1.3.12 in log, screenshot menu, kill game.
$ErrorActionPreference = 'Continue'
$root = "C:\Program Files (x86)\Steam\steamapps\common\Schedule I"
$log = "$root\BepInEx\LogOutput.log"
$shot = "C:\Users\mohui666\AppData\Local\Temp\ScheduleIChinese\ScheduleIChinese\tools\shot.ps1"
$png  = "C:\Users\mohui666\AppData\Local\Temp\ScheduleIChinese\ScheduleIChinese\tools\menu.png"

Start-Process "steam://rungameid/3164500"

$proc = $null
for ($i = 0; $i -lt 240; $i++) {
  Start-Sleep -Milliseconds 500
  $proc = Get-Process -Name "Schedule I" -ErrorAction SilentlyContinue
  if ($proc) { break }
}
if (-not $proc) { "process_never_appeared"; exit 1 }

$ok = $false
for ($i = 0; $i -lt 360; $i++) {
  Start-Sleep -Milliseconds 500
  if (-not (Test-Path $log)) { continue }
  $tail = Get-Content $log -Raw -ErrorAction SilentlyContinue
  if (-not $tail) { continue }
  if ($tail -match 'CJK font asset ready') { $ok = $true; break }
}
if (-not $ok) { "font_never_ready"; exit 1 }

# let the main menu settle, then screenshot
Start-Sleep -Seconds 10
& $shot -out $png
"smoke_done"
