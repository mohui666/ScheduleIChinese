param([string]$k = "esc")
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Kbd2 {
  [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public KEYBDINPUT ki; public long pad; }
  [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
  [DllImport("user32.dll", SetLastError=true)] public static extern uint SendInput(uint n, INPUT[] inputs, int size);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  public static void Tap(int scan, bool ext) {
    var inputs = new INPUT[2];
    uint extra = ext ? 1u : 0u;
    inputs[0].type = 1; inputs[0].ki.wScan = (ushort)scan; inputs[0].ki.dwFlags = 0x0008 | extra;
    inputs[1].type = 1; inputs[1].ki.wScan = (ushort)scan; inputs[1].ki.dwFlags = 0x0008 | 0x0002 | extra;
    SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
  }
}
"@
$p = Get-Process -Name "Schedule I" -ErrorAction SilentlyContinue
if ($p -and $p.MainWindowHandle -ne 0) {
  [Kbd2]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
  Start-Sleep -Milliseconds 400
}
$scan = 0x01; $ext = $false
switch ($k) {
  "esc"   { $scan = 0x01 }
  "enter" { $scan = 0x1C }
  "space" { $scan = 0x39 }
  "tab"   { $scan = 0x0F }
  "e"     { $scan = 0x12 }
}
[Kbd2]::Tap($scan, $ext)
Write-Output "sent $k"
