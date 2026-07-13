; winSVG installer script (Inno Setup 6)
; Builds winSVG-Setup-<version>.exe: installs the viewer + Explorer thumbnail
; provider per-user (no admin rights required) and registers every HKCU key
; the app needs. The generated uninstaller reverses all of it.
;
; Build (after `dotnet publish` of both projects into ..\dist):
;   ISCC.exe winSVG.iss

#define MyAppName "winSVG"
#define MyAppVersion "1.1.1"
#define MyAppPublisher "MSI Shamim"
#define MyAppURL "https://github.com/msi-shamim/winSVG"
#define MyProgId "SvgPreview.svg"
#define ThumbnailClsid "{{D8E9B2C4-8F5A-4E1B-9C3D-7A6F2B1E0D5C}"
#define ThumbnailShellExCategory "{{e357fccd-a995-4576-b01f-234630154e96}"

[Setup]
AppId={{7F4A9D63-52B8-4C7E-9A1D-3E8B6C0F2A91}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
; Per-user install: no UAC prompt, everything lives in the user profile.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\SvgPreview
DisableProgramGroupPage=yes
DisableDirPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist\installer
OutputBaseFilename=winSVG-Setup-{#MyAppVersion}
SetupIconFile=..\src\SvgViewer\Assets\app-icon.ico
UninstallDisplayIcon={app}\viewer\SvgViewer.exe
UninstallDisplayName={#MyAppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Files]
Source: "..\dist\viewer\*";    DestDir: "{app}\viewer";    Flags: ignoreversion recursesubdirs
Source: "..\dist\thumbnail\*"; DestDir: "{app}\thumbnail"; Flags: ignoreversion recursesubdirs

[Registry]
; --- COM thumbnail provider ---------------------------------------------
Root: HKCU; Subkey: "Software\Classes\CLSID\{#ThumbnailClsid}"; ValueType: string; ValueData: "winSVG Thumbnail Provider"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\CLSID\{#ThumbnailClsid}\InProcServer32"; ValueType: string; ValueData: "{app}\thumbnail\SvgThumbnailProvider.comhost.dll"
Root: HKCU; Subkey: "Software\Classes\CLSID\{#ThumbnailClsid}\InProcServer32"; ValueName: "ThreadingModel"; ValueType: string; ValueData: "Both"

; Attach the provider to .svg and .svgz through the thumbnail shellex category.
Root: HKCU; Subkey: "Software\Classes\.svg\shellex\{#ThumbnailShellExCategory}"; ValueType: string; ValueData: "{#ThumbnailClsid}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\.svgz\shellex\{#ThumbnailShellExCategory}"; ValueType: string; ValueData: "{#ThumbnailClsid}"; Flags: uninsdeletekey

; --- File type metadata ---------------------------------------------------
Root: HKCU; Subkey: "Software\Classes\.svg"; ValueName: "Content Type"; ValueType: string; ValueData: "image/svg+xml"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.svg"; ValueName: "PerceivedType"; ValueType: string; ValueData: "image"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.svgz"; ValueName: "Content Type"; ValueType: string; ValueData: "image/svg+xml"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.svgz"; ValueName: "PerceivedType"; ValueType: string; ValueData: "image"; Flags: uninsdeletevalue

; --- ProgId + open verb ---------------------------------------------------
Root: HKCU; Subkey: "Software\Classes\{#MyProgId}"; ValueType: string; ValueData: "Scalable Vector Graphics image"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\{#MyProgId}\DefaultIcon"; ValueType: string; ValueData: "{app}\viewer\SvgViewer.exe,0"
Root: HKCU; Subkey: "Software\Classes\{#MyProgId}\shell\open\command"; ValueType: string; ValueData: """{app}\viewer\SvgViewer.exe"" ""%1"""

; ProgId-level default + Open With entry for .svg.
Root: HKCU; Subkey: "Software\Classes\.svg"; ValueType: string; ValueData: "{#MyProgId}"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.svg\OpenWithProgids"; ValueName: "{#MyProgId}"; ValueType: string; ValueData: ""; Flags: uninsdeletevalue

; --- Open With application entry ------------------------------------------
Root: HKCU; Subkey: "Software\Classes\Applications\SvgViewer.exe"; ValueName: "FriendlyAppName"; ValueType: string; ValueData: "{#MyAppName}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Applications\SvgViewer.exe\shell\open\command"; ValueType: string; ValueData: """{app}\viewer\SvgViewer.exe"" ""%1"""
Root: HKCU; Subkey: "Software\Classes\Applications\SvgViewer.exe\SupportedTypes"; ValueName: ".svg"; ValueType: string; ValueData: ""

; --- Windows Default Apps registration --------------------------------------
Root: HKCU; Subkey: "Software\SvgPreview\Capabilities"; ValueName: "ApplicationName"; ValueType: string; ValueData: "{#MyAppName}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\SvgPreview\Capabilities"; ValueName: "ApplicationDescription"; ValueType: string; ValueData: "Lightweight SVG image viewer with Explorer thumbnail support"
Root: HKCU; Subkey: "Software\SvgPreview\Capabilities\FileAssociations"; ValueName: ".svg"; ValueType: string; ValueData: "{#MyProgId}"
Root: HKCU; Subkey: "Software\SvgPreview\Capabilities\FileAssociations"; ValueName: ".svgz"; ValueType: string; ValueData: "{#MyProgId}"
Root: HKCU; Subkey: "Software\RegisteredApplications"; ValueName: "{#MyAppName}"; ValueType: string; ValueData: "Software\SvgPreview\Capabilities"; Flags: uninsdeletevalue

[Run]
; Refresh the per-user icon cache so file icons/thumbnails appear promptly.
Filename: "{sys}\ie4uinit.exe"; Parameters: "-show"; Flags: runhidden nowait

[Code]
// Tells the Shell that file associations changed (SHCNE_ASSOCCHANGED).
procedure SHChangeNotify(wEventId: Integer; uFlags: Cardinal; dwItem1, dwItem2: Integer);
  external 'SHChangeNotify@shell32.dll stdcall';

// Returns True when the .NET 8 Desktop Runtime (required by the app) is present.
function IsDotNet8DesktopRuntimeInstalled(): Boolean;
var
  ExecResultCode: Integer;
begin
  Result := Exec('cmd.exe',
    '/c dotnet --list-runtimes | findstr /C:"Microsoft.WindowsDesktop.App 8." >nul',
    '', SW_HIDE, ewWaitUntilTerminated, ExecResultCode) and (ExecResultCode = 0);
end;

function InitializeSetup(): Boolean;
var
  UnusedErrorCode: Integer;
begin
  Result := True;
  if not IsDotNet8DesktopRuntimeInstalled() then
  begin
    if MsgBox('winSVG needs the free Microsoft .NET 8 Desktop Runtime, which was not found on this PC.' + #13#10#13#10 +
              'Open the download page now? (Setup will continue either way; install the runtime before first use.)',
              mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/8.0',
                '', '', SW_SHOWNORMAL, ewNoWait, UnusedErrorCode);
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    SHChangeNotify($08000000, 0, 0, 0); // SHCNE_ASSOCCHANGED
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    SHChangeNotify($08000000, 0, 0, 0);
end;
