# Build the distribution package (self-contained single exe; no .NET install required).
# Output: <repo>\dist\MultiImageCanvas\MultiImageCanvas.exe and <repo>\dist\MultiImageCanvas.zip
$ErrorActionPreference = "Stop"

$repo = Split-Path $PSScriptRoot -Parent
$srcProj = Join-Path $repo "src\MultiImageCanvas.csproj"
$outDir = Join-Path $repo "dist\MultiImageCanvas"
$zipPath = Join-Path $repo "dist\MultiImageCanvas.zip"

if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Force $outDir | Out-Null

dotnet publish $srcProj -c Release -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    --nologo -v minimal -o $outDir
if ($LASTEXITCODE -ne 0) { Write-Output "publish failed"; exit 1 }

# remove debug symbols from the package
Get-ChildItem $outDir -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path $outDir -DestinationPath $zipPath

$exe = Get-Item (Join-Path $outDir "MultiImageCanvas.exe")
$zip = Get-Item $zipPath
Write-Output ("exe: {0} ({1} MB)" -f $exe.FullName, [math]::Round($exe.Length / 1MB, 1))
Write-Output ("zip: {0} ({1} MB)" -f $zip.FullName, [math]::Round($zip.Length / 1MB, 1))
Write-Output "self-contained: no .NET runtime installation required on target PCs"
