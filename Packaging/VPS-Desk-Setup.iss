; The build script sets the payload, output file name and edition text.

#define PayloadDir GetEnv("VPSDESK_INSTALLER_PAYLOAD")
#define OutputDir GetEnv("VPSDESK_INSTALLER_OUTPUT")
#define SetupFileName GetEnv("VPSDESK_INSTALLER_FILE_NAME")

[Setup]
AppId={{EEABAA15-8AA9-4B10-8D64-CE1E576B1BEA}
AppName=VPS Desk
AppVersion=1.0.0
AppPublisher=VPS Desk
DefaultDirName={localappdata}\Programs\VPS Desk
DefaultGroupName=VPS Desk
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename={#SetupFileName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName=VPS Desk

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\VPS Desk"; Filename: "{app}\VpsDesk.Desktop.exe"; Tasks: desktopinstall
Name: "{autodesktop}\VPS Desk"; Filename: "{app}\VpsDesk.Desktop.exe"; Tasks: desktopinstall

[Tasks]
Name: "desktopinstall"; Description: "Crear acceso directo en el escritorio"; GroupDescription: "Accesos directos:"; Flags: unchecked

[Run]
Filename: "{app}\VpsDesk.Desktop.exe"; Description: "Abrir VPS Desk"; Flags: nowait postinstall skipifsilent

[Code]
var
  ModePage: TInputOptionWizardPage;

procedure InitializeWizard;
begin
  ModePage := CreateInputOptionPage(
    wpWelcome,
    'Destino de VPS Desk',
    'Elige cómo usar esta instalación',
    'La instalación local guarda los datos en el perfil de Windows. La instalación portable guarda los datos junto al programa y permite mover la carpeta.',
    True,
    False);
  ModePage.Add('Instalar en esta PC');
  ModePage.Add('Instalación portable (USB o carpeta elegida)');
  ModePage.SelectedValueIndex := 0;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = ModePage.ID then
  begin
    if ModePage.SelectedValueIndex = 0 then
      WizardForm.DirEdit.Text := ExpandConstant('{localappdata}\Programs\VPS Desk')
    else
      WizardForm.DirEdit.Text := 'E:\VPS-DESK';
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    if ModePage.SelectedValueIndex = 1 then
      SaveStringToFile(ExpandConstant('{app}\portable.mode'), 'VPS Desk portable data mode' + #13#10, False)
    else if FileExists(ExpandConstant('{app}\portable.mode')) then
      DeleteFile(ExpandConstant('{app}\portable.mode'));
  end;
end;
