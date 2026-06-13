; =============================================================================
; WinEventMonitor.iss  —  Script de Inno Setup
; Requisitos: Inno Setup 6  (https://jrsoftware.org/isdl.php)
; Compilar:  ISCC.exe WinEventMonitor.iss
;            o mediante build.ps1 en la raiz del repo
; =============================================================================

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

#define MyAppName        "Windows Event Monitor"
#define MyAppExeName     "WinEventMonitor.Service.exe"
#define MyAppTrayExe     "WinEventMonitor.Tray.exe"
#define MyAppPublisher   "dgarciap88"
#define MyAppURL         "https://github.com/dgarciap88/WinEventMonitor"
#define MyAppSupportURL  "https://github.com/dgarciap88/WinEventMonitor/issues"
#define MyAppDescription "Herramienta de monitorizacion de eventos de seguridad de Windows en tiempo real"
#define MyAppServiceName "WinEventMonitor"
#define MyDataDir        "{commonappdata}\WinEventMonitor"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppSupportURL}
AppUpdatesURL={#MyAppURL}/releases
AppComments={#MyAppDescription}
AppCopyright=Copyright (C) 2026 {#MyAppPublisher}
DefaultDirName={autopf}\WinEventMonitor
DefaultGroupName={#MyAppName}
OutputDir=Output
OutputBaseFilename=WinEventMonitor-{#MyAppVersion}-Setup
Compression=lzma2/max
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
PrivilegesRequired=admin
WizardStyle=modern
SetupIconFile=
; No sobreescribir datos persistentes al actualizar
UsePreviousAppDir=yes
; Permitir upgrade in-place sin desinstalar primero
CloseApplications=force

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Files]
; Backend publicado (auto-contenido win-x64)
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Excluir explicitamente datos de usuario que NO deben sobreescribirse
; (los datos viven en CommonAppData, no en app dir, asi que no hay conflicto)

[Icons]
; Acceso directo en el menu inicio que abre la app Tray
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppTrayExe}"
Name: "{group}\Desinstalar {#MyAppName}"; Filename: "{uninstallexe}"
; Acceso directo en el escritorio (opcional)
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppTrayExe}"; Tasks: desktopicon

[Registry]
; Arranque automatico al inicio de sesion del usuario
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "WinEventMonitor"; \
    ValueData: """{app}\{#MyAppTrayExe}"""; \
    Flags: uninsdeletevalue

[Tasks]
Name: "desktopicon"; Description: "Crear icono en el escritorio"; GroupDescription: "Iconos adicionales:"

[Run]
; Parar servicio anterior si existe (upgrade)
Filename: "sc.exe"; Parameters: "stop {#MyAppServiceName}"; Flags: runhidden waituntilterminated; StatusMsg: "Deteniendo servicio anterior..."; Check: ServiceExists

; Registrar el servicio de Windows
Filename: "sc.exe"; \
    Parameters: "create {#MyAppServiceName} binPath= ""{app}\{#MyAppExeName}"" start= auto DisplayName= ""{#MyAppName}"""; \
    Flags: runhidden waituntilterminated; StatusMsg: "Registrando servicio de Windows..."

; Configurar descripcion del servicio
Filename: "sc.exe"; \
    Parameters: "description {#MyAppServiceName} ""Monitor de eventos de seguridad de Windows en tiempo real"""; \
    Flags: runhidden waituntilterminated

; Arrancar el servicio
Filename: "sc.exe"; Parameters: "start {#MyAppServiceName}"; Flags: runhidden waituntilterminated; StatusMsg: "Iniciando servicio..."

; Lanzar la app Tray tras instalar (abre la ventana con la UI)
Filename: "{app}\{#MyAppTrayExe}"; Description: "Abrir Windows Event Monitor ahora"; Flags: postinstall nowait skipifsilent

[UninstallRun]
; Cerrar la app Tray antes de desinstalar
Filename: "taskkill.exe"; Parameters: "/IM {#MyAppTrayExe} /F"; Flags: runhidden waituntilterminated
; Parar y eliminar el servicio al desinstalar
Filename: "sc.exe"; Parameters: "stop {#MyAppServiceName}";   Flags: runhidden waituntilterminated
Filename: "sc.exe"; Parameters: "delete {#MyAppServiceName}"; Flags: runhidden waituntilterminated

[Code]
// Comprueba si el servicio ya existe (para el paso de upgrade)
function ServiceExists: Boolean;
var
  ResultCode: Integer;
begin
  Exec('sc.exe', 'query {#MyAppServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := (ResultCode = 0);
end;

// Al desinstalar: preguntar si se quiere conservar la base de datos
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Answer: Integer;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    Answer := MsgBox(
      'Deseas eliminar tambien los datos guardados (base de datos de eventos, clave API y configuracion)?' + #13#10 +
      'Ubicacion: C:\ProgramData\WinEventMonitor' + #13#10#13#10 +
      'Selecciona "No" para conservar los datos (recomendado si vas a reinstalar).',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2);
    if Answer = IDYES then
    begin
      DelTree(ExpandConstant('{commonappdata}\WinEventMonitor'), True, True, True);
    end;
  end;
end;
