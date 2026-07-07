#define AppName "Multi Image Canvas"
#ifndef AppVersion
#define AppVersion "1.0.0"
#endif
#ifndef PublishDir
#define PublishDir "publish"
#endif
#ifndef OutputDir
#define OutputDir "out"
#endif

[Setup]
AppId={{7F6B1A52-9C3E-4C8F-9A1D-2B8E4A5C6D70}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=tokonoha00
DefaultDirName={localappdata}\Programs\MultiImageCanvas
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=MultiImageCanvas-{#AppVersion}-Setup
SetupIconFile=..\src\app.ico
UninstallDisplayIcon={app}\MultiImageCanvas.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=classic
PrivilegesRequired=lowest
ChangesAssociations=yes

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Files]
Source: "{#PublishDir}\MultiImageCanvas.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\MultiImageCanvas.exe"; WorkingDir: "{app}"; Check: ShouldCreateStartMenu
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\MultiImageCanvas.exe"; WorkingDir: "{app}"; Check: ShouldCreateDesktopIcon

[Registry]
Root: HKCU; Subkey: "Software\MultiImageCanvas"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletevalue

Root: HKCU; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "{#AppName}"; ValueData: "Software\MultiImageCanvas\Capabilities"; Flags: uninsdeletevalue; Check: ShouldRegisterFileAssociations
Root: HKCU; Subkey: "Software\MultiImageCanvas\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "{#AppName}"; Flags: uninsdeletekey; Check: ShouldRegisterFileAssociations
Root: HKCU; Subkey: "Software\MultiImageCanvas\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "画像のオーバーレイ表示・キャンバス編集ツール"; Check: ShouldRegisterFileAssociations
Root: HKCU; Subkey: "Software\MultiImageCanvas\Capabilities\FileAssociations"; ValueType: string; ValueName: ".png"; ValueData: "MultiImageCanvas.Image"; Check: ShouldRegisterFileAssociations
Root: HKCU; Subkey: "Software\MultiImageCanvas\Capabilities\FileAssociations"; ValueType: string; ValueName: ".jpg"; ValueData: "MultiImageCanvas.Image"; Check: ShouldRegisterFileAssociations
Root: HKCU; Subkey: "Software\MultiImageCanvas\Capabilities\FileAssociations"; ValueType: string; ValueName: ".jpeg"; ValueData: "MultiImageCanvas.Image"; Check: ShouldRegisterFileAssociations
Root: HKCU; Subkey: "Software\MultiImageCanvas\Capabilities\FileAssociations"; ValueType: string; ValueName: ".bmp"; ValueData: "MultiImageCanvas.Image"; Check: ShouldRegisterFileAssociations
Root: HKCU; Subkey: "Software\MultiImageCanvas\Capabilities\FileAssociations"; ValueType: string; ValueName: ".gif"; ValueData: "MultiImageCanvas.Image"; Check: ShouldRegisterFileAssociations
Root: HKCU; Subkey: "Software\MultiImageCanvas\Capabilities\FileAssociations"; ValueType: string; ValueName: ".webp"; ValueData: "MultiImageCanvas.Image"; Check: ShouldRegisterFileAssociations
Root: HKCU; Subkey: "Software\MultiImageCanvas\Capabilities\FileAssociations"; ValueType: string; ValueName: ".tif"; ValueData: "MultiImageCanvas.Image"; Check: ShouldRegisterFileAssociations
Root: HKCU; Subkey: "Software\MultiImageCanvas\Capabilities\FileAssociations"; ValueType: string; ValueName: ".tiff"; ValueData: "MultiImageCanvas.Image"; Check: ShouldRegisterFileAssociations
Root: HKCU; Subkey: "Software\MultiImageCanvas\Capabilities\FileAssociations"; ValueType: string; ValueName: ".micl"; ValueData: "MultiImageCanvas.Canvas"; Check: ShouldRegisterFileAssociations
Root: HKCU; Subkey: "Software\MultiImageCanvas\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mics"; ValueData: "MultiImageCanvas.Session"; Check: ShouldRegisterFileAssociations

Root: HKCU; Subkey: "Software\Classes\MultiImageCanvas.Image"; ValueType: string; ValueData: "Multi Image Canvas 画像"; Flags: uninsdeletekey; Check: ShouldRegisterFileAssociations
Root: HKCU; Subkey: "Software\Classes\MultiImageCanvas.Image\DefaultIcon"; ValueType: string; ValueData: """{app}\MultiImageCanvas.exe"",0"; Check: ShouldRegisterFileAssociations
Root: HKCU; Subkey: "Software\Classes\MultiImageCanvas.Image\shell\open\command"; ValueType: string; ValueData: """{app}\MultiImageCanvas.exe"" ""%1"""; Check: ShouldRegisterFileAssociations
Root: HKCU; Subkey: "Software\Classes\MultiImageCanvas.Canvas"; ValueType: string; ValueData: "Multi Image Canvas キャンバス"; Flags: uninsdeletekey; Check: ShouldRegisterFileAssociations
Root: HKCU; Subkey: "Software\Classes\MultiImageCanvas.Canvas\DefaultIcon"; ValueType: string; ValueData: """{app}\MultiImageCanvas.exe"",0"; Check: ShouldRegisterFileAssociations
Root: HKCU; Subkey: "Software\Classes\MultiImageCanvas.Canvas\shell\open\command"; ValueType: string; ValueData: """{app}\MultiImageCanvas.exe"" ""%1"""; Check: ShouldRegisterFileAssociations
Root: HKCU; Subkey: "Software\Classes\MultiImageCanvas.Session"; ValueType: string; ValueData: "Multi Image Canvas セッション"; Flags: uninsdeletekey; Check: ShouldRegisterFileAssociations
Root: HKCU; Subkey: "Software\Classes\MultiImageCanvas.Session\DefaultIcon"; ValueType: string; ValueData: """{app}\MultiImageCanvas.exe"",0"; Check: ShouldRegisterFileAssociations
Root: HKCU; Subkey: "Software\Classes\MultiImageCanvas.Session\shell\open\command"; ValueType: string; ValueData: """{app}\MultiImageCanvas.exe"" ""%1"""; Check: ShouldRegisterFileAssociations

Root: HKCU; Subkey: "Software\Classes\.png\OpenWithProgids"; ValueType: none; ValueName: "MultiImageCanvas.Image"; Flags: uninsdeletevalue; Check: ShouldRegisterFileAssociations
Root: HKCU; Subkey: "Software\Classes\.jpg\OpenWithProgids"; ValueType: none; ValueName: "MultiImageCanvas.Image"; Flags: uninsdeletevalue; Check: ShouldRegisterFileAssociations
Root: HKCU; Subkey: "Software\Classes\.jpeg\OpenWithProgids"; ValueType: none; ValueName: "MultiImageCanvas.Image"; Flags: uninsdeletevalue; Check: ShouldRegisterFileAssociations
Root: HKCU; Subkey: "Software\Classes\.bmp\OpenWithProgids"; ValueType: none; ValueName: "MultiImageCanvas.Image"; Flags: uninsdeletevalue; Check: ShouldRegisterFileAssociations
Root: HKCU; Subkey: "Software\Classes\.gif\OpenWithProgids"; ValueType: none; ValueName: "MultiImageCanvas.Image"; Flags: uninsdeletevalue; Check: ShouldRegisterFileAssociations
Root: HKCU; Subkey: "Software\Classes\.webp\OpenWithProgids"; ValueType: none; ValueName: "MultiImageCanvas.Image"; Flags: uninsdeletevalue; Check: ShouldRegisterFileAssociations
Root: HKCU; Subkey: "Software\Classes\.tif\OpenWithProgids"; ValueType: none; ValueName: "MultiImageCanvas.Image"; Flags: uninsdeletevalue; Check: ShouldRegisterFileAssociations
Root: HKCU; Subkey: "Software\Classes\.tiff\OpenWithProgids"; ValueType: none; ValueName: "MultiImageCanvas.Image"; Flags: uninsdeletevalue; Check: ShouldRegisterFileAssociations
Root: HKCU; Subkey: "Software\Classes\.micl\OpenWithProgids"; ValueType: none; ValueName: "MultiImageCanvas.Canvas"; Flags: uninsdeletevalue; Check: ShouldRegisterFileAssociations
Root: HKCU; Subkey: "Software\Classes\.mics\OpenWithProgids"; ValueType: none; ValueName: "MultiImageCanvas.Session"; Flags: uninsdeletevalue; Check: ShouldRegisterFileAssociations

[Run]
Filename: "{app}\MultiImageCanvas.exe"; Description: "{#AppName} を起動する"; Flags: nowait postinstall skipifsilent unchecked

[Code]
var
  OptionsPage: TInputOptionWizardPage;

function ShouldCreateStartMenu: Boolean;
begin
  Result := OptionsPage.Values[0];
end;

function ShouldCreateDesktopIcon: Boolean;
begin
  Result := OptionsPage.Values[1];
end;

function ShouldRegisterFileAssociations: Boolean;
begin
  Result := OptionsPage.Values[2];
end;

procedure InitializeWizard;
begin
  OptionsPage := CreateInputOptionPage(
    wpSelectDir,
    '追加オプション',
    '必要な項目を選択してください。',
    'あとからアプリの設定画面でもファイル関連付けを変更できます。',
    False,
    False);
  OptionsPage.Add('スタートメニューに追加');
  OptionsPage.Add('デスクトップに追加');
  OptionsPage.Add('画像、キャンバス、セッションを開く候補に追加');
  OptionsPage.Values[0] := True;
  OptionsPage.Values[1] := False;
  OptionsPage.Values[2] := True;
end;

procedure DeleteOpenWithProgId(Ext: String; ProgId: String);
begin
  RegDeleteValue(HKCU, 'Software\Classes\' + Ext + '\OpenWithProgids', ProgId);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    RegDeleteValue(HKCU, 'Software\RegisteredApplications', '{#AppName}');
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\MultiImageCanvas');
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\MultiImageCanvas.Image');
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\MultiImageCanvas.Canvas');
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\MultiImageCanvas.Session');

    DeleteOpenWithProgId('.png', 'MultiImageCanvas.Image');
    DeleteOpenWithProgId('.jpg', 'MultiImageCanvas.Image');
    DeleteOpenWithProgId('.jpeg', 'MultiImageCanvas.Image');
    DeleteOpenWithProgId('.bmp', 'MultiImageCanvas.Image');
    DeleteOpenWithProgId('.gif', 'MultiImageCanvas.Image');
    DeleteOpenWithProgId('.webp', 'MultiImageCanvas.Image');
    DeleteOpenWithProgId('.tif', 'MultiImageCanvas.Image');
    DeleteOpenWithProgId('.tiff', 'MultiImageCanvas.Image');
    DeleteOpenWithProgId('.micl', 'MultiImageCanvas.Canvas');
    DeleteOpenWithProgId('.mics', 'MultiImageCanvas.Session');
  end;
end;
