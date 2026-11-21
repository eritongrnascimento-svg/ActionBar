#ifndef AppVersion
  #define AppVersion "0.6.2"
#endif
#ifndef Payload
  #error Define Payload with the full path to a self-contained Windows publish.
#endif
#ifndef WebViewBootstrap
  #error Define WebViewBootstrap with the path to Microsoft's signed Evergreen bootstrapper.
#endif

[Setup]
AppId=ActionBar
AppName=ActionBar
AppVersion={#AppVersion}
AppPublisher=ActionBar
DefaultDirName={localappdata}\Programs\ActionBar
DefaultGroupName=ActionBar
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\Assets\ActionBar.ico
UninstallDisplayIcon={app}\ActionBar.exe
OutputBaseFilename=ActionBar-{#AppVersion}-Windows-x64-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; Flags: unchecked
Name: "importportable"; Description: "Importar os dados de uma edição portátil"; Flags: unchecked

[Files]
Source: "{#Payload}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#WebViewBootstrap}"; DestDir: "{tmp}"; DestName: "WebView2Setup.exe"; Flags: deleteafterinstall; Check: NeedsWebView

[Icons]
Name: "{autoprograms}\ActionBar"; Filename: "{app}\ActionBar.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\ActionBar"; Filename: "{app}\ActionBar.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{tmp}\WebView2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "Instalando o Microsoft Edge WebView2…"; Flags: waituntilterminated; Check: NeedsWebView
Filename: "{app}\ActionBar.exe"; Description: "Abrir o ActionBar"; Flags: nowait postinstall skipifsilent unchecked

[Code]
var
  Portable: TInputDirWizardPage;

function NeedsWebView: Boolean;
var
  Version: string;
  Key: string;
begin
  Key := 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  Result := not ((RegQueryStringValue(HKLM32, Key, 'pv', Version) and
                  (Version <> '') and (Version <> '0.0.0.0')) or
                 (RegQueryStringValue(HKCU, Key, 'pv', Version) and
                  (Version <> '') and (Version <> '0.0.0.0')));
end;

procedure InitializeWizard;
begin
  Portable := CreateInputDirPage(wpSelectTasks, 'Importar uma edição portátil',
    'Selecione a pasta app da edição antiga.',
    'A instalação copia apenas os arquivos de dados que ainda não existirem no destino.',
    False, '');
  Portable.Add('Pasta que contém ActionBar.exe e a subpasta data');
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = Portable.ID) and not WizardIsTaskSelected('importportable');
end;

procedure ImportFile(const OldPath, NewPath, Name: string);
begin
  if FileExists(OldPath + '\' + Name) and not FileExists(NewPath + '\' + Name) then
  begin
    if not ForceDirectories(NewPath) or not FileCopy(OldPath + '\' + Name, NewPath + '\' + Name, True) then
      Log('Não foi possível importar ' + Name);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Chosen, SourceData, DestData: string;
begin
  if (CurStep <> ssPostInstall) or not WizardIsTaskSelected('importportable') then exit;
  Chosen := Trim(Portable.Values[0]);
  if Chosen = '' then exit;
  SourceData := AddBackslash(Chosen) + 'data';
  DestData := ExpandConstant('{app}\data');
  if not DirExists(SourceData) then exit;
  ImportFile(SourceData, DestData, 'settings.json');
  ImportFile(SourceData, DestData, 'files.json');
  ImportFile(SourceData, DestData, 'lembretes.json');
end;

