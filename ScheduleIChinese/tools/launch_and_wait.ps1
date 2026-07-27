# Launches Schedule I via Steam, waits for BepInEx startup, records timing, screenshots the menu.
$ErrorActionPreference = 'Continue'
$root = "C:\Program Files (x86)\Steam\steamapps\common\Schedule I"
$log = "$root\BepInEx\LogOutput.log"
$outDir = "$root\ModSource\ScheduleIChinese\tools"
$timing = "$outDir\launch_timing.txt"

$t0 = Get-Date
"launch_start=$($t0.ToString('HH:mm:ss.f'))" | Out-File $timing -Encoding utf8

Start-Process "steam://rungameid/3164500"

# wait for process
$proc = $null
for ($i = 0; $i -lt 240; $i++) {
  Start-Sleep -Milliseconds 500
  $proc = Get-Process -Name "Schedule I" -ErrorAction SilentlyContinue
  if ($proc) { break }
}
if (-not $proc) { "process_never_appeared" | Out-File $timing -Append -Encoding utf8; exit 1 }
$t1 = Get-Date
"process_start=$($t1.ToString('HH:mm:ss.f')) (+$([int]($t1-$t0).TotalSeconds)s)" | Out-File $timing -Append -Encoding utf8

# wait for chainloader + font ready in the fresh log
$sawChain = $false; $sawFont = $false
for ($i = 0; $i -lt 360; $i++) {
  Start-Sleep -Milliseconds 500
  if (-not (Test-Path $log)) { continue }
  $tail = Get-Content $log -Raw -ErrorAction SilentlyContinue
  if (-not $tail) { continue }
  if (-not $sawChain -and $tail -match 'Chainloader startup complete') {
    $sawChain = $true
    $t2 = Get-Date
    "chainloader_complete=$($t2.ToString('HH:mm:ss.f')) (+$([int]($t2-$t0).TotalSeconds)s)" | Out-File $timing -Append -Encoding utf8
  }
  if (-not $sawFont -and $tail -match 'CJK font asset ready') {
    $sawFont = $true
    $t3 = Get-Date
    "font_ready=$($t3.ToString('HH:mm:ss.f')) (+$([int]($t3-$t0).TotalSeconds)s)" | Out-File $timing -Append -Encoding utf8
    break
  }
}
# give the menu a moment to settle, then screenshot
Start-Sleep -Seconds 8
& "$outDir\shot.ps1" -out "$outDir\menu.png"
$t4 = Get-Date
"menu_screenshot=$($t4.ToString('HH:mm:ss.f')) (+$([int]($t4-$t0).TotalSeconds)s)" | Out-File $timing -Append -Encoding utf8
Get-Content $timing
