; TrayAuth installer (Inno Setup 6)
;
; Per-user install: everything goes under the user's own profile, so this never needs
; administrator rights and never shows a UAC prompt. That also means winget can install it
; without elevation, which is what we want for a tray utility.
;
; Built by build.ps1 - it passes the version and source path in via /D switches.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\..\..\AppData\Local\TrayAuth-build\publish"
#endif

#define AppName "TrayAuth"
#define AppDisplayName "TrayAuth - Authenticator"
#define AppPublisher "Raadhes"
#define AppExeName "TrayAuth.exe"
#define AppUrl "https://github.com/Raadhes/TrayAuth"

[Setup]
; This GUID identifies the application to Windows and to winget. It must never change
; between versions, or upgrades turn into side-by-side installs.
AppId={{8F3C1A64-2D57-4E9B-9A21-6B0E7C5D4F18}
AppName={#AppDisplayName}
AppVersion={#AppVersion}
VersionInfoVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases

; Per-user install - no elevation, no UAC.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppDisplayName}
DisableProgramGroupPage=yes
DisableDirPage=auto
UninstallDisplayName={#AppDisplayName}
UninstallDisplayIcon={app}\{#AppExeName}

OutputDir=..\dist
OutputBaseFilename=TrayAuth-Setup-{#AppVersion}
SetupIconFile=..\assets\icon.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

; A tray app has nothing useful to show after install beyond "it's running".
LicenseFile=..\LICENSE
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
MinVersion=10.0.17763

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Start TrayAuth when I sign in to Windows"; GroupDescription: "Startup"

[Files]
Source: "{#SourceDir}\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion isreadme

[Icons]
Name: "{autoprograms}\{#AppDisplayName}"; Filename: "{app}\{#AppExeName}"

[Registry]
; Start with Windows. Written only if the task is selected; the app's own tray menu
; toggles this same value, so the two agree.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "TrayAuth"; ValueData: """{app}\{#AppExeName}"""; \
    Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Start TrayAuth now"; \
    Flags: nowait postinstall skipifsilent

; A silent install (what winget runs) still needs the app started, otherwise nothing
; appears until the next sign-in and the install looks like it failed.
Filename: "{app}\{#AppExeName}"; Flags: nowait runasoriginaluser skipifnotsilent

[UninstallRun]
; Close the running instance before removing its exe, so uninstall does not leave a
; stale tray icon behind.
Filename: "{cmd}"; Parameters: "/C taskkill /F /IM {#AppExeName}"; \
    Flags: runhidden skipifdoesntexist; RunOnceId: "StopTrayAuth"

[Code]
{ Stop a running copy before overwriting files, otherwise the exe is locked. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{cmd}'), '/C taskkill /F /IM {#AppExeName}', '',
       SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;

{ The vault lives in %APPDATA%\TrayAuth and is deliberately left alone on uninstall:
  it is DPAPI-encrypted and irreplaceable, so deleting it is the user's call, not ours. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  VaultDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    VaultDir := ExpandConstant('{userappdata}\TrayAuth');

    if DirExists(VaultDir) then
    begin
      if not UninstallSilent then
      begin
        if MsgBox('Your accounts are still stored on this PC:' + #13#10#13#10
                  + VaultDir + #13#10#13#10
                  + 'Delete them as well?' + #13#10#13#10
                  + 'They cannot be recovered afterwards. Without an export you would have to '
                  + 'set up two-factor authentication again at every site.',
                  mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        begin
          DelTree(VaultDir, True, True, True);
        end;
      end;
    end;
  end;
end;
