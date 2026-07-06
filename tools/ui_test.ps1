# Launch app and capture ITS WINDOW ONLY via PrintWindow (does not need window on top)
param(
    [string]$ExePath = "F:\dev\multi_image_canvas\src\bin\Debug\net8.0-windows\MultiImageCanvas.exe",
    [string]$ShotPath = "$PSScriptRoot\ui_shot.png",
    [int]$WaitSec = 6
)

$ErrorActionPreference = "Stop"

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win32Cap {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@ -ReferencedAssemblies System.Drawing

Add-Type -AssemblyName System.Drawing

$proc = Start-Process -FilePath $ExePath -PassThru
Start-Sleep -Seconds $WaitSec

if ($proc.HasExited) { Write-Output "FAIL: exited early code $($proc.ExitCode)"; exit 1 }
$proc.Refresh()
Write-Output ("Responding: " + $proc.Responding)
Write-Output ("WorkingSet(MB): " + [math]::Round($proc.WorkingSet64 / 1MB, 1))

$hwnd = $proc.MainWindowHandle
if ($hwnd -eq [IntPtr]::Zero) { Write-Output "FAIL: no main window"; $proc.Kill(); exit 1 }

$rect = New-Object Win32Cap+RECT
[Win32Cap]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
$w = $rect.Right - $rect.Left
$h = $rect.Bottom - $rect.Top
Write-Output "WindowRect: $($rect.Left),$($rect.Top) ${w}x${h}"

$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
# PW_RENDERFULLCONTENT = 2
$ok = [Win32Cap]::PrintWindow($hwnd, $hdc, 2)
$g.ReleaseHdc($hdc)
$g.Dispose()
$bmp.Save($ShotPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output "PrintWindow ok: $ok -> $ShotPath"

$null = $proc.CloseMainWindow()
if (-not $proc.WaitForExit(8000)) { $proc.Kill(); Write-Output "WARN: killed" } else { Write-Output "Exited code $($proc.ExitCode)" }
