# Performance test: 4K images + real webp, idle CPU, pan-storm CPU, memory
param(
    [string]$ExePath = "F:\dev\multi_image_canvas\src\bin\Debug\net8.0-windows\MultiImageCanvas.exe"
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class Win32Perf {
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

$imgDir = "$PSScriptRoot\testimg"
New-Item -ItemType Directory -Force $imgDir | Out-Null

function New-BigPng([string]$name, [int]$w, [int]$h) {
    $path = Join-Path $imgDir $name
    if (Test-Path $path) { return $path }
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $rnd = New-Object System.Random(42)
    for ($i = 0; $i -lt 400; $i++) {
        $c = [System.Drawing.Color]::FromArgb($rnd.Next(60,255), $rnd.Next(255), $rnd.Next(255), $rnd.Next(255))
        $b = New-Object System.Drawing.SolidBrush $c
        $g.FillEllipse($b, $rnd.Next($w), $rnd.Next($h), $rnd.Next(80,600), $rnd.Next(80,600))
        $b.Dispose()
    }
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return $path
}

$b1 = (New-BigPng "big1.png" 3840 2160) -replace '\\','\\'
$b2 = (New-BigPng "big2.png" 3840 2160) -replace '\\','\\'
$b3 = (New-BigPng "big3.png" 2560 1440) -replace '\\','\\'
$webp = (Join-Path $imgDir "tarkov_map.webp") -replace '\\','\\'

$session = @"
{
  "Version": 1, "Theme": "dark", "WindowBounds": [80, 60, 1360, 860], "Maximized": false,
  "SidebarView": "thumbs", "SnapEnabled": true, "InsertNaturalSize": false,
  "OverlayOpacity": 0.9, "OverlayClickThrough": false, "BgOpacity": 1.0, "ActiveTab": 0,
  "Tabs": [
    { "Version": 2, "Name": "PerfTest", "Zoom": 0.18, "Scroll": [-60, -60],
      "Items": [
        { "Path": "$b1", "Dest": [0, 0, 3840, 2160], "Crop": [0, 0, 3840, 2160], "Rotation": 0, "FlipH": false, "FlipV": false, "Opacity": 1.0, "Visible": true },
        { "Path": "$b2", "Dest": [3900, 0, 3840, 2160], "Crop": [0, 0, 3840, 2160], "Rotation": 15, "FlipH": false, "FlipV": false, "Opacity": 0.8, "Visible": true },
        { "Path": "$b3", "Dest": [0, 2300, 2560, 1440], "Crop": [0, 0, 2560, 1440], "Rotation": 0, "FlipH": false, "FlipV": false, "Opacity": 1.0, "Visible": true },
        { "Path": "$webp", "Dest": [2700, 2300, 2000, 1400], "Crop": [0, 0, 1, 1], "Rotation": 0, "FlipH": false, "FlipV": false, "Opacity": 1.0, "Visible": true }
      ]
    }
  ],
  "TabFilePaths": [null]
}
"@
# webp crop [0,0,1,1] is invalid on purpose? -> No: use full crop unknown size; loader keeps crop as-is.
# Better: omit crop so loader defaults to full image. Rewrite with null crop:
$session = $session -replace '"Crop": \[0, 0, 1, 1\], ', '"Crop": null, '

$sessionDir = Join-Path $env:APPDATA "MultiImageCanvas"
[System.IO.File]::WriteAllText((Join-Path $sessionDir "session.json"), $session, (New-Object System.Text.UTF8Encoding($false)))

# --- launch ---
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$proc = Start-Process -FilePath $ExePath -PassThru
while (-not $proc.HasExited -and $proc.MainWindowHandle -eq [IntPtr]::Zero) {
    Start-Sleep -Milliseconds 100
    $proc.Refresh()
}
Write-Output ("StartupToWindow(ms): " + $sw.ElapsedMilliseconds)
Start-Sleep -Seconds 4
$proc.Refresh()
Write-Output ("WorkingSet(MB) after load: " + [math]::Round($proc.WorkingSet64 / 1MB, 1))

# --- idle CPU over 10s ---
$cpu0 = $proc.TotalProcessorTime
Start-Sleep -Seconds 10
$proc.Refresh()
$cpu1 = $proc.TotalProcessorTime
$idlePct = ($cpu1 - $cpu0).TotalMilliseconds / 10000 * 100 / [Environment]::ProcessorCount
Write-Output ("Idle CPU avg (all-core %): " + [math]::Round($idlePct, 2))

# --- pan storm 5s ---
$main = $proc.MainWindowHandle
$best = [IntPtr]::Zero; $bestArea = 0
foreach ($h in [Win32Perf]::Children($main)) {
    $r = New-Object Win32Perf+RECT
    [Win32Perf]::GetWindowRect($h, [ref]$r) | Out-Null
    $area = ($r.Right - $r.Left) * ($r.Bottom - $r.Top)
    if ($area -gt $bestArea) { $bestArea = $area; $best = $h }
}
function Get-LParam([int]$x, [int]$y) { return [IntPtr](($y -shl 16) -bor ($x -band 0xffff)) }
$MK_MBUTTON = [IntPtr]0x10

$cpu0 = $proc.TotalProcessorTime
$storm = [System.Diagnostics.Stopwatch]::StartNew()
[Win32Perf]::PostMessage($best, 0x0207, $MK_MBUTTON, (Get-LParam 500 400)) | Out-Null
$x = 500; $y = 400; $dx = -3; $dy = -2
while ($storm.ElapsedMilliseconds -lt 5000) {
    $x += $dx; $y += $dy
    if ($x -lt 100 -or $x -gt 800) { $dx = -$dx }
    if ($y -lt 100 -or $y -gt 600) { $dy = -$dy }
    [Win32Perf]::PostMessage($best, 0x0200, $MK_MBUTTON, (Get-LParam $x $y)) | Out-Null
    Start-Sleep -Milliseconds 8
}
[Win32Perf]::PostMessage($best, 0x0208, [IntPtr]0, (Get-LParam $x $y)) | Out-Null
$proc.Refresh()
$cpu1 = $proc.TotalProcessorTime
$panPct = ($cpu1 - $cpu0).TotalMilliseconds / 5000 * 100 / [Environment]::ProcessorCount
Write-Output ("Pan-storm CPU avg (all-core %): " + [math]::Round($panPct, 2))
Write-Output ("Pan-storm CPU (single-core equivalent %): " + [math]::Round($panPct * [Environment]::ProcessorCount, 1))
Write-Output ("CPU cores: " + [Environment]::ProcessorCount)

$proc.Refresh()
Write-Output ("WorkingSet(MB) after pan: " + [math]::Round($proc.WorkingSet64 / 1MB, 1))

# --- screenshot ---
$rect = New-Object Win32Perf+RECT
[Win32Perf]::GetWindowRect($main, [ref]$rect) | Out-Null
$w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
[Win32Perf]::PrintWindow($main, $hdc, 2) | Out-Null
$g.ReleaseHdc($hdc); $g.Dispose()
$bmp.Save("$PSScriptRoot\perf_shot.png", [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output "shot saved"

$null = $proc.CloseMainWindow()
if (-not $proc.WaitForExit(8000)) { $proc.Kill(); Write-Output "WARN killed" } else { Write-Output "Exited $($proc.ExitCode)" }
