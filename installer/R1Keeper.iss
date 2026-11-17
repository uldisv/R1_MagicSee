; Inno Setup 7 script for R1 Keeper.
; Build with installer\build.ps1: it publishes the program to dist\app and then compiles
; this script into dist\R1KeeperSetup-<version>.exe.

#define AppName "R1 Keeper"
#define AppExeName "R1Keeper.exe"
#define PublishDir AddBackslash(SourcePath) + "..\dist\app"

#if !FileExists(PublishDir + "\" + AppExeName)
  #error dist\app\R1Keeper.exe is missing. Build with installer\build.ps1.
#endif

#define VerMajor
#define VerMinor
#define VerRev
#define VerBuild
#expr GetVersionComponents(PublishDir + "\" + AppExeName, VerMajor, VerMinor, VerRev, VerBuild)
#define AppVersion Str(VerMajor) + "." + Str(VerMinor) + "." + Str(VerRev)

; These must match the program: AppPaths in Program.cs, Autostart.cs and LanguagePreference.cs.
#define AppMutex "Local\R1Keeper.SingleInstance"
#define ExitEvent "Local\R1Keeper.Exit"
#define RunKey "Software\Microsoft\Windows\CurrentVersion\Run"
#define RunValue "R1Keeper"
#define PrefsKey "Software\R1Keeper"

[Setup]
AppId={{7A9E1071-E442-4C0A-9789-B40C927236C6}
AppName={#AppName}
AppVersion={#AppVersion}
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
; The program needs no admin rights, so neither does Setup. It installs for the current user
; only (by default into %LOCALAPPDATA%\Programs), and autostart goes into that user's Run
; key, the same place the tray menu's "Start with Windows" uses.
PrivilegesRequired=lowest
SetupArchitecture=x64
MinVersion=10.0.19041
UninstallDisplayIcon={app}\{#AppExeName}
OutputDir=..\dist
OutputBaseFilename=R1KeeperSetup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; English is preselected whatever the Windows language. The language picked here also
; becomes the program's starting language.
ShowLanguageDialog=yes
LanguageDetectionMethod=none
; Inno's Latvian.isl dates from version 5.5: every page of this installer is translated, but
; newer messages (downloads, archive extraction, some error dialogs) fall back to English.
MissingMessagesWarning=no
NotRecognizedMessagesWarning=no
; The autostart checkbox starts from the registry instead (see CurPageChanged), so a change
; made in the tray menu since the last install is kept.
UsePreviousTasks=no
; R1 Keeper has no window to close: Setup asks it to exit through its exit event instead.
CloseApplications=no

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "lv"; MessagesFile: "compiler:Languages\Latvian.isl"

[CustomMessages]
en.AutostartTask=Start {#AppName} with Windows
lv.AutostartTask=Startēt {#AppName} kopā ar Windows
en.CloseAppFailed={#AppName} is running and could not be closed. Exit it from its notification-area icon menu and try again.
lv.CloseAppFailed={#AppName} darbojas, un to neizdevās aizvērt. Aizver to ar paziņojumu joslas ikonas izvēlni un mēģini vēlreiz.

[Tasks]
Name: "autostart"; Description: "{cm:AutostartTask}"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"

[Registry]
; Same quoted form as AutostartPath.Command, so the tray menu sees it as on. Turning the task
; off, and uninstalling, are handled in [Code].
Root: HKCU; Subkey: "{#RunKey}"; ValueType: string; ValueName: "{#RunValue}"; ValueData: """{app}\{#AppExeName}"""; Tasks: autostart
; The interface language: set once from Setup's language, and after that only from the tray
; menu, which is why an existing value is left alone.
Root: HKCU; Subkey: "{#PrefsKey}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "{#PrefsKey}"; ValueType: string; ValueName: "Language"; ValueData: "en"; Languages: en; Flags: createvalueifdoesntexist
Root: HKCU; Subkey: "{#PrefsKey}"; ValueType: string; ValueName: "Language"; ValueData: "lv"; Languages: lv; Flags: createvalueifdoesntexist

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall; Check: ShouldLaunch

[Code]
const
  EVENT_MODIFY_STATE = $0002;
  CloseTimeoutMs = 10000;

var
  AppWasRunning: Boolean;
  TasksInitialized: Boolean;

function OpenEvent(dwDesiredAccess: DWORD; bInheritHandle: BOOL; lpName: String): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(hEvent: THandle): BOOL;
  external 'SetEvent@kernel32.dll stdcall';
function CloseHandle(hObject: THandle): BOOL;
  external 'CloseHandle@kernel32.dll stdcall';

function AppIsRunning: Boolean;
begin
  Result := CheckForMutexes('{#AppMutex}');
end;

{ True while the installed exe is still mapped by a process that has not fully exited. }
function ExeIsLocked: Boolean;
var
  Exe: String;
  Stream: TFileStream;
begin
  Result := False;
  Exe := ExpandConstant('{app}\{#AppExeName}');
  if not FileExists(Exe) then
    Exit;
  try
    Stream := TFileStream.Create(Exe, fmOpenReadWrite or fmShareExclusive);
    Stream.Free;
  except
    Result := True;
  end;
end;

{ Asks a running R1 Keeper to exit, as its Exit menu item would, and waits for it. }
function CloseApp: Boolean;
var
  Event: THandle;
  Waited: Integer;
begin
  Result := True;
  if not AppIsRunning then
    Exit;
  AppWasRunning := True;
  Log('R1 Keeper is running; asking it to exit');
  Event := OpenEvent(EVENT_MODIFY_STATE, False, '{#ExitEvent}');
  if Event <> 0 then
  begin
    SetEvent(Event);
    CloseHandle(Event);
  end;

  Waited := 0;
  while AppIsRunning do
  begin
    if Waited >= CloseTimeoutMs then
    begin
      Log('R1 Keeper did not exit');
      Result := False;
      Exit;
    end;
    Sleep(100);
    Waited := Waited + 100;
  end;

  { The mutex goes a moment before the process has fully exited and let go of its files.
    Best effort: if the exe stays locked, Setup's own file-in-use handling takes over. }
  while ExeIsLocked and (Waited < CloseTimeoutMs) do
  begin
    Sleep(100);
    Waited := Waited + 100;
  end;
  Log('R1 Keeper exited');
end;

function AutostartIsOurs: Boolean;
var
  Command: String;
begin
  Result := RegQueryStringValue(HKCU, '{#RunKey}', '{#RunValue}', Command) and
    (CompareText(RemoveQuotes(Trim(Command)), ExpandConstant('{app}\{#AppExeName}')) = 0);
end;

{ Leaves an entry that points at another copy of the program alone. }
procedure RemoveAutostartIfOurs;
begin
  if AutostartIsOurs then
    RegDeleteValue(HKCU, '{#RunKey}', '{#RunValue}');
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  { On an upgrade, show autostart as it is now: it may have been changed from the tray menu. }
  if (CurPageID = wpSelectTasks) and not TasksInitialized then
  begin
    TasksInitialized := True;
    if WizardForm.PrevAppDir <> '' then
    begin
      if AutostartIsOurs then
        WizardSelectTasks('autostart')
      else
        WizardSelectTasks('!autostart');
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  if CloseApp then
    Result := ''
  else
    Result := CustomMessage('CloseAppFailed');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and not WizardIsTaskSelected('autostart') then
    RemoveAutostartIfOurs;
end;

{ The checkbox on the last page; a silent install only restarts a copy it closed. }
function ShouldLaunch: Boolean;
begin
  Result := (not WizardSilent) or AppWasRunning;
end;

function InitializeUninstall: Boolean;
begin
  Result := CloseApp;
  if not Result then
    SuppressibleMsgBox(CustomMessage('CloseAppFailed'), mbError, MB_OK, IDOK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  { Also covers autostart that was turned on from the tray menu rather than by Setup. }
  if CurUninstallStep = usUninstall then
    RemoveAutostartIfOurs;
end;
