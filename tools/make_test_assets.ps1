# Create test images and a prepared session.json (ASCII only) for UI verification
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$imgDir = "$PSScriptRoot\testimg"
New-Item -ItemType Directory -Force $imgDir | Out-Null

function New-TestPng([string]$name, [int]$w, [int]$h, [string]$colorName) {
    $path = Join-Path $imgDir $name
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $color = [System.Drawing.Color]::FromName($colorName)
    $g.Clear($color)
    $g.DrawString("$name ($w x $h)", (New-Object System.Drawing.Font("Segoe UI", 16)), [System.Drawing.Brushes]::White, 10, 10)
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return $path
}

$p1 = (New-TestPng "red.png" 400 300 "Firebrick") -replace '\\','\\'
$p2 = (New-TestPng "blue.png" 300 300 "RoyalBlue") -replace '\\','\\'
$p3 = (New-TestPng "green.png" 500 200 "SeaGreen") -replace '\\','\\'

$session = @"
{
  "Version": 1,
  "Theme": "dark",
  "WindowBounds": [80, 60, 1360, 860],
  "Maximized": false,
  "SidebarView": "layers",
  "SnapEnabled": true,
  "InsertNaturalSize": false,
  "OverlayOpacity": 0.9,
  "OverlayClickThrough": false,
  "BgOpacity": 1.0,
  "ActiveTab": 0,
  "Tabs": [
    {
      "Version": 2,
      "Name": "UITest",
      "Zoom": 1.0,
      "Scroll": [-40, -60],
      "Items": [
        { "Path": "$p1", "Dest": [60, 40, 400, 300], "Crop": [0, 0, 400, 300], "Rotation": 0, "FlipH": false, "FlipV": false, "Opacity": 1.0, "Visible": true },
        { "Path": "$p2", "Dest": [500, 80, 300, 300], "Crop": [0, 0, 300, 300], "Rotation": 30, "FlipH": false, "FlipV": false, "Opacity": 0.7, "Visible": true },
        { "Path": "$p3", "Dest": [150, 380, 500, 200], "Crop": [0, 0, 500, 200], "Rotation": 0, "FlipH": true, "FlipV": false, "Opacity": 1.0, "Visible": true }
      ]
    }
  ],
  "TabFilePaths": [null]
}
"@

$sessionDir = Join-Path $env:APPDATA "MultiImageCanvas"
New-Item -ItemType Directory -Force $sessionDir | Out-Null
[System.IO.File]::WriteAllText((Join-Path $sessionDir "session.json"), $session, (New-Object System.Text.UTF8Encoding($false)))
Write-Output "session written"
