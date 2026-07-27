param([string]$out = "C:\Program Files (x86)\Steam\steamapps\common\Schedule I\ModSource\ScheduleIChinese\tools\screen.png")
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class FG2 {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
"@
$p = Get-Process -Name "Schedule I" -ErrorAction SilentlyContinue
if ($p -and $p.MainWindowHandle -ne 0) {
  [FG2]::ShowWindow($p.MainWindowHandle, 9) | Out-Null
  [FG2]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
  Start-Sleep -Milliseconds 1200
}
$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
$bmp.Save($out)
$g.Dispose(); $bmp.Dispose()
Write-Output "saved $out"
