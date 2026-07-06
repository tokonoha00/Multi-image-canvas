# Build the per-user MSI installer (no admin rights needed to build or install).
# Requires: .NET 8 SDK, wix (dotnet tool install --global wix)
# Output: <repo>\installer\out\MultiImageCanvas-<version>.msi
param([string]$Version = "1.0.0")
$ErrorActionPreference = "Stop"

$repo = Split-Path $PSScriptRoot -Parent
$srcProj = Join-Path $repo "src\MultiImageCanvas.csproj"
$publishDir = Join-Path $repo "installer\publish"
$outDir = Join-Path $repo "installer\out"
$wxs = Join-Path $repo "installer\Product.wxs"
$icon = Join-Path $repo "src\app.ico"
$msi = Join-Path $outDir "MultiImageCanvas-$Version.msi"

# 1. self-contained single exe (no .NET install required on target PCs)
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish $srcProj -c Release -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:Version=$Version `
    --nologo -v minimal -o $publishDir
if ($LASTEXITCODE -ne 0) { Write-Output "publish failed"; exit 1 }
Get-ChildItem $publishDir -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force

# 2. build MSI
New-Item -ItemType Directory -Force $outDir | Out-Null
if (Test-Path $msi) { Remove-Item $msi -Force }
wix build $wxs `
    -d "PublishDir=$publishDir" `
    -d "IconPath=$icon" `
    -d "Version=$Version" `
    -o $msi
if ($LASTEXITCODE -ne 0) { Write-Output "wix build failed"; exit 1 }

$item = Get-Item $msi
Write-Output ("msi: {0} ({1} MB)" -f $item.FullName, [math]::Round($item.Length / 1MB, 1))
Write-Output "install : msiexec /i <msi>       (per-user, no UAC)"
Write-Output "silent  : msiexec /i <msi> /qn"
Write-Output "remove  : msiexec /x <msi> /qn"
