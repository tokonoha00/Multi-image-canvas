@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo .NET SDK が見つかりません。
  echo https://dotnet.microsoft.com/download から .NET 8 SDK を入れてから再実行してください。
  pause
  exit /b 1
)

dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if errorlevel 1 (
  echo ビルドに失敗しました。
  pause
  exit /b 1
)

echo.
echo 完了: bin\Release\net8.0-windows\win-x64\publish\MultiImageCanvas.exe
echo この publish フォルダを ZIP 化すれば、解凍して exe 起動だけで使えます。
pause
