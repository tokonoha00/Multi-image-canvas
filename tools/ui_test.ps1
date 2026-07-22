# Launch app and capture its largest visible window.
param(
    [string]$ExePath = (Join-Path (Split-Path $PSScriptRoot -Parent) "src\bin\Debug\net8.0-windows\MultiImageCanvas.exe"),
    [string]$ShotPath = "$PSScriptRoot\ui_shot.png",
    [int]$WaitSec = 6
)

$ErrorActionPreference = "Stop"

Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class Win32Cap {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc proc, IntPtr lp);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    public delegate bool EnumProc(IntPtr hwnd, IntPtr lp);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

    public static List<IntPtr> ProcessWindows(uint processId) {
        var windows = new List<IntPtr>();
        EnumWindows((hwnd, lp) => {
            uint owner;
            GetWindowThreadProcessId(hwnd, out owner);
            if (owner == processId && IsWindowVisible(hwnd)) windows.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return windows;
    }
}
"@ -ReferencedAssemblies System.Drawing

Add-Type -AssemblyName System.Drawing

$proc = Start-Process -FilePath $ExePath -PassThru
Start-Sleep -Seconds $WaitSec

if ($proc.HasExited) { Write-Output "FAIL: exited early code $($proc.ExitCode)"; exit 1 }
$proc.Refresh()
Write-Output ("Responding: " + $proc.Responding)
Write-Output ("WorkingSet(MB): " + [math]::Round($proc.WorkingSet64 / 1MB, 1))

$hwnd = [IntPtr]::Zero
$largestArea = 0
foreach ($candidate in [Win32Cap]::ProcessWindows([uint32]$proc.Id)) {
    $candidateRect = New-Object Win32Cap+RECT
    [Win32Cap]::GetWindowRect($candidate, [ref]$candidateRect) | Out-Null
    $area = ($candidateRect.Right - $candidateRect.Left) * ($candidateRect.Bottom - $candidateRect.Top)
    if ($area -gt $largestArea) { $largestArea = $area; $hwnd = $candidate }
}
if ($hwnd -eq [IntPtr]::Zero) { Write-Output "FAIL: no main window"; $proc.Kill(); exit 1 }

$rect = New-Object Win32Cap+RECT
[Win32Cap]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
$w = $rect.Right - $rect.Left
$h = $rect.Bottom - $rect.Top
Write-Output "WindowRect: $($rect.Left),$($rect.Top) ${w}x${h}"

$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
[Win32Cap]::SetForegroundWindow($hwnd) | Out-Null
$oldCursor = New-Object Win32Cap+POINT
[Win32Cap]::GetCursorPos([ref]$oldCursor) | Out-Null
[Win32Cap]::SetCursorPos($rect.Left + [math]::Min(600, $w / 2), $rect.Top + [math]::Min(500, $h / 2)) | Out-Null
Start-Sleep -Milliseconds 250
$g.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size($w, $h)))
$ok = $true
$g.Dispose()
$bmp.Save($ShotPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
[Win32Cap]::SetCursorPos($oldCursor.X, $oldCursor.Y) | Out-Null
Write-Output "Screen capture ok: $ok -> $ShotPath"

$null = $proc.CloseMainWindow()
if (-not $proc.WaitForExit(8000)) { $proc.Kill(); Write-Output "WARN: killed" } else { Write-Output "Exited code $($proc.ExitCode)" }
