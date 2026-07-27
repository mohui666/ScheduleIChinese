param([string]$k)
# Sends a key by scan code so Unity's raw-input path sees it.
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Kbd {
  [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public KEYBDINPUT ki; public long pad; }
  [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
  [DllImport("user32.dll", SetLastError=true)] public static extern uint SendInput(uint n, INPUT[] inputs, int size);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  public const uint SCANCODE = 0x0008, KEYUP = 0x0002, EXTENDED = 0x0001;
  public static void Tap(ushort scan, bool ext) {
    var inputs = new INPUT[2];
    uint extra = ext ? EXTENDED : 0;
    inputs[0].type = 1; inputs[0].ki.wScan = scan; inputs[0].ki.dwFlags = SCANCODE | extra;
    inputs[1].type = 1; inputs[1].ki.wScan = scan; inputs[1].ki.dwFlags = SCANCODE | KEYUP | extra;
    SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
  }
}
"@
$p = Get-Process -Name "Schedule I" -ErrorAction SilentlyContinue
if ($p -and $p.MainWindowHandle -ne 0) {
  [Kbd]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
  Start-Sleep -Milliseconds 400
}
$map = @{
  'tab' = @(0x0F, $false); 'esc' = @(0x01, $false); 'enter' = @(0x1C, $false)
  'e' = @(0x12, $false); 'm' = @(0x32, $false); 'w' = @(0x11, $false)
  'up' = @(0x48, $true); 'down' = @(0x50, $true); 'left' = @(0x4B, $true); 'right' = @(0x4D, $true)
}
if (-not $map.ContainsKey($k)) { Write-Output "unknown key $k"; exit 1 }
[Kbd]::Tap([ushort]$map[$k][0], [bool]$map[$k][1])
Write-Output "sent $k"
