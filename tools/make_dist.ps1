# Build the portable distribution package (self-contained; no .NET install required).
# Output: <repo>\dist\MultiImageCanvas-<version>-portable-win-x64.zip
param(
    [string]$Version = "1.0.3",
    [string]$ShareDir = ""
)
$ErrorActionPreference = "Stop"

$repo = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($ShareDir)) {
    $ShareDir = "E:\" + [char]0x5171 + [char]0x6709
}
$srcProj = Join-Path $repo "src\MultiImageCanvas.csproj"
$outDir = Join-Path $repo "dist\MultiImageCanvas"
$zipPath = Join-Path $repo "dist\MultiImageCanvas-$Version-portable-win-x64.zip"

if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Force $outDir | Out-Null

dotnet publish $srcProj -c Release -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:Version=$Version `
    --nologo -v minimal -o $outDir
if ($LASTEXITCODE -ne 0) { Write-Output "publish failed"; exit 1 }

# remove debug symbols from the package
Get-ChildItem $outDir -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force
Copy-Item -LiteralPath (Join-Path $repo "README.md") -Destination (Join-Path $outDir "README.md") -Force
Copy-Item -LiteralPath (Join-Path $repo "THIRD_PARTY_NOTICES.md") -Destination (Join-Path $outDir "THIRD_PARTY_NOTICES.md") -Force

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path $outDir -DestinationPath $zipPath

$exe = Get-Item (Join-Path $outDir "MultiImageCanvas.exe")
$zip = Get-Item $zipPath
if (Test-Path -LiteralPath $ShareDir) {
    Copy-Item -LiteralPath $zipPath -Destination (Join-Path $ShareDir $zip.Name) -Force
}
Write-Output ("exe: {0} ({1} MB)" -f $exe.FullName, [math]::Round($exe.Length / 1MB, 1))
Write-Output ("zip: {0} ({1} MB)" -f $zip.FullName, [math]::Round($zip.Length / 1MB, 1))
if (Test-Path -LiteralPath $ShareDir) {
    Write-Output ("shared: {0}" -f (Join-Path $ShareDir $zip.Name))
}
Write-Output "self-contained: no .NET runtime installation required on target PCs"
