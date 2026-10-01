; =====================================================================
; openDynamic - Inno Setup Script
; Versión: 1.0.0 (Hito M6 - Release Beta)
; Arquitectura: Windows x64 (WPF, .NET 10)
; =====================================================================

#define MyAppName "openDynamic"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "DAHL13"
#define MyAppURL "https://github.com/DAHL13/openDynamic"
#define MyAppExeName "OpenDynamic.App.exe"

; Ruta de origen de los binarios publicados con 'PublishReadyToRun=true'
#ifndef AppSourcePath
  #define AppSourcePath "..\publish"
#endif

[Setup]
; Identificador de aplicación único y estable
AppId={{C78DF69B-B03C-4C15-9BF6-324F1813A29B}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

; REGLA DE ORO 3: Cero privilegios de Administrador (UAC).
; Instalación por usuario en {localappdata}\Programs\openDynamic ({autopf}\openDynamic).
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\openDynamic
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes

; Configuración de salida del instalador
OutputDir=Output
OutputBaseFilename=openDynamic-setup
SetupIconFile=..\src\OpenDynamic.App\Resources\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes

; Restricciones de arquitectura: x64 moderno
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Interfaz moderna
WizardStyle=modern
CloseApplications=force
CloseApplicationsFilter=*.exe

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
spanish.CreateDesktopIcon=Crear un acceso directo en el &Escritorio
english.CreateDesktopIcon=Create a &desktop shortcut
spanish.AutostartTask=Iniciar openDynamic automáticamente con Windows
english.AutostartTask=Launch openDynamic automatically with Windows
spanish.DotNetPrompt=openDynamic requiere Microsoft .NET 10 Desktop Runtime (x64) para funcionar.%n%n¿Desea abrir la página oficial de descarga para instalarlo ahora?
english.DotNetPrompt=openDynamic requires Microsoft .NET 10 Desktop Runtime (x64) to run.%n%nWould you like to open the official download page to install it now?
spanish.KeepSettingsPrompt=¿Desea conservar su archivo de configuración y preferencias (%AppData%\openDynamic\settings.json)?%n%nSeleccione "Sí" para conservarlo, o "No" para eliminar todos los datos de usuario y registros.
english.KeepSettingsPrompt=Do you want to keep your user configuration and preferences (%AppData%\openDynamic\settings.json)?%n%nSelect "Yes" to keep them, or "No" to remove all user data and logs.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "{cm:AutostartTask}"; GroupDescription: "{cm:AutoStartProgramGroupDescription}"; Flags: unchecked

[Files]
; Binarios compilados listos para ejecutar (ReadyToRun)
Source: "{#AppSourcePath}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Inicio automático opcional por tarea en HKCU (sin privilegios elevados)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "openDynamic"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[InstallDelete]
; Tarea R1: Limpieza de binarios huérfanos de la integración con Antigravity en actualizaciones
Type: filesandordirs; Name: "{app}\hook"
Type: files; Name: "{app}\OpenDynamic.Hook.exe"

[UninstallDelete]
Type: files; Name: "{app}\*.*"
Type: dirifempty; Name: "{app}"

[Code]
// =====================================================================
// Verificación del Runtime de .NET 10 Desktop (x64)
// =====================================================================
function IsDotNet10DesktopInstalled: Boolean;
var
  InstalledNames: TArrayOfString;
  I: Integer;
begin
  Result := False;
  // Verificar la clave del registro oficial donde .NET registra los runtimes de Windows Desktop
  if RegGetSubkeyNames(HKLM, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', InstalledNames) then
  begin
    for I := 0 to GetArrayLength(InstalledNames) - 1 do
    begin
      if Pos('10.', InstalledNames[I]) = 1 then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;

  // Verificación secundaria en HKCU por si se instaló a nivel de usuario
  if not Result and RegGetSubkeyNames(HKCU, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', InstalledNames) then
  begin
    for I := 0 to GetArrayLength(InstalledNames) - 1 do
    begin
      if Pos('10.', InstalledNames[I]) = 1 then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;
end;

function InitializeSetup: Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  if not IsDotNet10DesktopInstalled then
  begin
    if MsgBox(CustomMessage('DotNetPrompt'), mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open', 'https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    end;
  end;
end;

// =====================================================================
// Desinstalador Limpio: Purga de registro y limpieza de configuración
// =====================================================================
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  SettingsDir: String;
  LogsDir: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    // 1. Purgar incondicionalmente la clave de inicio automático en el registro de HKCU
    // independientemente de si fue creada por el instalador o por los Ajustes de openDynamic
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'openDynamic');

    // 2. Ofrecer la opción al usuario de conservar o eliminar la configuración (%AppData%\openDynamic)
    SettingsDir := ExpandConstant('{userappdata}\openDynamic');
    LogsDir := ExpandConstant('{localappdata}\openDynamic');

    if DirExists(SettingsDir) or DirExists(LogsDir) then
    begin
      if MsgBox(CustomMessage('KeepSettingsPrompt'), mbConfirmation, MB_YESNO) = IDNO then
      begin
        // El usuario eligió NO conservar: eliminar directorios de datos y logs
        if DirExists(SettingsDir) then
        begin
          DelTree(SettingsDir, True, True, True);
        end;
        if DirExists(LogsDir) then
        begin
          DelTree(LogsDir, True, True, True);
        end;
      end;
    end;
  end;
end;
