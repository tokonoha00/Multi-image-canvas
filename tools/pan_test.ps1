# Middle-button drag pan test via PostMessage (does not move the real cursor)
param(
    [string]$ExePath = "F:\dev\multi_image_canvas\src\bin\Debug\net8.0-windows\MultiImageCanvas.exe",
    [int]$WaitSec = 8
)
$ErrorActionPreference = "Stop"

Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class Win32Pan {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr lp);
    public delegate bool EnumProc(IntPtr hwnd, IntPtr lp);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    public static List<IntPtr> Children(IntPtr parent) {
        var list = new List<IntPtr>();
        EnumChildWindows(parent, (h, l) => { list.Add(h); return true; }, IntPtr.Zero);
        return list;
    }
}
"@

Add-Type -AssemblyName System.Drawing

function Save-Shot([IntPtr]$hwnd, [string]$path) {
    $rect = New-Object Win32Pan+RECT
    [Win32Pan]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    [Win32Pan]::PrintWindow($hwnd, $hdc, 2) | Out-Null
    $g.ReleaseHdc($hdc); $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

$proc = Start-Process -FilePath $ExePath -PassThru
Start-Sleep -Seconds $WaitSec
if ($proc.HasExited) { Write-Output "FAIL: exited early"; exit 1 }

$main = $proc.MainWindowHandle

# Find the canvas: the child window with the largest area
$best = [IntPtr]::Zero; $bestArea = 0
foreach ($h in [Win32Pan]::Children($main)) {
    $r = New-Object Win32Pan+RECT
    [Win32Pan]::GetWindowRect($h, [ref]$r) | Out-Null
    $area = ($r.Right - $r.Left) * ($r.Bottom - $r.Top)
    if ($area -gt $bestArea) { $bestArea = $area; $best = $h }
}
Write-Output ("Canvas hwnd: " + $best + " area " + $bestArea)

Save-Shot $main "$PSScriptRoot\pan_before.png"

function Get-LParam([int]$x, [int]$y) { return [IntPtr](($y -shl 16) -bor ($x -band 0xffff)) }

$WM_MOUSEMOVE = 0x0200; $WM_MBUTTONDOWN = 0x0207; $WM_MBUTTONUP = 0x0208
$MK_MBUTTON = [IntPtr]0x10

# middle-drag from (500,400) to (340,260)
[Win32Pan]::PostMessage($best, $WM_MOUSEMOVE, [IntPtr]0, (Get-LParam 500 400)) | Out-Null
Start-Sleep -Milliseconds 100
[Win32Pan]::PostMessage($best, $WM_MBUTTONDOWN, $MK_MBUTTON, (Get-LParam 500 400)) | Out-Null
Start-Sleep -Milliseconds 100
for ($i = 1; $i -le 8; $i++) {
    $x = 500 - (20 * $i); $y = 400 - (17 * $i)
    [Win32Pan]::PostMessage($best, $WM_MOUSEMOVE, $MK_MBUTTON, (Get-LParam $x $y)) | Out-Null
    Start-Sleep -Milliseconds 40
}
[Win32Pan]::PostMessage($best, $WM_MBUTTONUP, [IntPtr]0, (Get-LParam 340 264)) | Out-Null
Start-Sleep -Milliseconds 600

Save-Shot $main "$PSScriptRoot\pan_after.png"
Write-Output "shots saved"

$null = $proc.CloseMainWindow()
if (-not $proc.WaitForExit(8000)) { $proc.Kill(); Write-Output "WARN killed" } else { Write-Output "Exited $($proc.ExitCode)" }
