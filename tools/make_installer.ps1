# Build the per-user Inno Setup installer.
# Requires: .NET 8 SDK, Inno Setup 6 (ISCC.exe)
# Output: <repo>\installer\out\MultiImageCanvas-<version>-Setup.exe
param(
    [string]$Version = "1.0.5",
    [string]$ShareDir = ""
)
$ErrorActionPreference = "Stop"

$repo = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($ShareDir)) {
    $ShareDir = "E:\" + [char]0x5171 + [char]0x6709
}
$srcProj = Join-Path $repo "src\MultiImageCanvas.csproj"
$publishDir = Join-Path $repo "installer\publish"
$outDir = Join-Path $repo "installer\out"
$iss = Join-Path $repo "installer\MultiImageCanvas.iss"
$setup = Join-Path $outDir "MultiImageCanvas-$Version-Setup.exe"

function Find-Iscc {
    $cmd = Get-Command "iscc.exe" -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $repoLocal = Join-Path $repo "tools\.inno6\ISCC.exe"
    $paths = @(
        $repoLocal,
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )
    foreach ($path in $paths) {
        if ($path -and (Test-Path -LiteralPath $path)) { return $path }
    }

    throw "ISCC.exe was not found. Install Inno Setup 6, for example: winget install --id JRSoftware.InnoSetup -e"
}

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

# 2. build installer
New-Item -ItemType Directory -Force $outDir | Out-Null
if (Test-Path $setup) { Remove-Item $setup -Force }
$iscc = Find-Iscc
& $iscc $iss `
    "/DAppVersion=$Version" `
    "/DPublishDir=$publishDir" `
    "/DOutputDir=$outDir"
if ($LASTEXITCODE -ne 0) { Write-Output "inno build failed"; exit 1 }

# 3. copy to shared folder for handoff
if (Test-Path -LiteralPath $ShareDir) {
    Copy-Item -LiteralPath $setup -Destination (Join-Path $ShareDir (Split-Path $setup -Leaf)) -Force
}
else {
    Write-Output "share folder not found: $ShareDir"
}

$item = Get-Item $setup
Write-Output ("installer: {0} ({1} MB)" -f $item.FullName, [math]::Round($item.Length / 1MB, 1))
if (Test-Path -LiteralPath $ShareDir) {
    Write-Output ("shared   : {0}" -f (Join-Path $ShareDir $item.Name))
}
