param([int]$x, [int]$y, [int]$double = 0)
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Inp {
  [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public MOUSEINPUT mi; public long pad; }
  [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
  [DllImport("user32.dll", SetLastError=true)] public static extern uint SendInput(uint n, INPUT[] inputs, int size);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
  public const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004;
  public static void Click(int x, int y) {
    SetCursorPos(x, y);
    System.Threading.Thread.Sleep(120);
    var inputs = new INPUT[2];
    inputs[0].type = 0; inputs[0].mi.dwFlags = LEFTDOWN;
    inputs[1].type = 0; inputs[1].mi.dwFlags = LEFTUP;
    SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
  }
}
"@
$p = Get-Process -Name "Schedule I" -ErrorAction SilentlyContinue
if ($p -and $p.MainWindowHandle -ne 0) {
  [Inp]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
  Start-Sleep -Milliseconds 400
}
[Inp]::Click($x, $y)
if ($double -eq 1) { Start-Sleep -Milliseconds 150; [Inp]::Click($x, $y) }
Write-Output "clicked $x $y"
