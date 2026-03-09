

; Use some useful packaging stuff from: http://toneday.blogspot.com/2010/12/innosetup.html
; dotnet_Passive enabled shows the .NET/VC2012 installation progress, as it can take quite some time

; Enable the required define(s) below if a local event function (prepended with Local) is used
;#define haveLocalPrepareToInstall
;#define haveLocalNeedRestart
;#define haveLocalNextButtonClick


[Setup]
; NOTE: The value of AppId uniquely identifies this application.
; Do not use the same AppId value in installers for other applications.
; (To generate a new GUID, click Tools | Generate GUID inside the IDE.)
AppID={{3D8D0F0D-7DBF-400C-9C44-00BD21986138}
AppName=pGina
AppVersion=4.0.0.0
AppVerName=pGina v4.0.0.0
AppPublisher=Fork pGina Team
AppPublisherURL=http://www.pgina.org/
AppSupportURL=http://www.pgina.org/
AppUpdatesURL=http://www.pgina.org/
DefaultDirName={commonpf}\pGina
DefaultGroupName=pGina
AllowNoIcons=true
LicenseFile=..\LICENSE
OutputBaseFilename=pGinaSetup-4.0.0.0
SetupIconFile=..\pGina\src\Configuration\Resources\pginaicon_redcircle.ico
Compression=lzma/Max
SolidCompression=true
AppCopyright=Fork pGina Team
ExtraDiskSpaceRequired=6
DisableDirPage=auto
AlwaysShowDirOnReadyPage=yes
AlwaysShowGroupOnReadyPage=yes
DisableProgramGroupPage=auto

ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
;Name: "english"; MessagesFile: "compiler:Default.isl"

[Registry]
; Eliminar claves de registro al desinstalar
Root: HKLM; Subkey: "SOFTWARE\pGina"; Flags: uninsdeletekey
Root: HKCU; Subkey: "SOFTWARE\pGina"; Flags: uninsdeletekey
; También limpiar claves de versiones anteriores si existen
Root: HKLM; Subkey: "SOFTWARE\pGina3"; Flags: uninsdeletekey
Root: HKCU; Subkey: "SOFTWARE\pGina3"; Flags: uninsdeletekey

[InstallDelete]
; Eliminar configuraciones previas al instalar (instalación limpia)
Type: filesandordirs; Name: "{commonappdata}\pGina"
Type: filesandordirs; Name: "{commonappdata}\pGina3"
Type: filesandordirs; Name: "{userappdata}\pGina"
Type: filesandordirs; Name: "{userappdata}\pGina3"
Type: filesandordirs; Name: "{localappdata}\pGina"
Type: filesandordirs; Name: "{localappdata}\pGina3"

[UninstallDelete]
; Eliminar configuraciones al desinstalar
Type: filesandordirs; Name: "{commonappdata}\pGina"
Type: filesandordirs; Name: "{commonappdata}\pGina3"
Type: filesandordirs; Name: "{userappdata}\pGina"
Type: filesandordirs; Name: "{userappdata}\pGina3"
Type: filesandordirs; Name: "{localappdata}\pGina"
Type: filesandordirs; Name: "{localappdata}\pGina3"
; Eliminar carpeta de instalación completamente
Type: filesandordirs; Name: "{app}"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\pGina\src\bin\pGina.Configuration.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\pGina\src\bin\*.exe"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\pGina\src\bin\*.dll"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\pGina\src\bin\*.xml"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\pGina\src\bin\*.config"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\Plugins\Core\bin\*.dll"; DestDir: "{app}\Plugins\Core"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\Plugins\Core\bin\*.xml"; DestDir: "{app}\Plugins\Core"; Flags: ignoreversion recursesubdirs createallsubdirs
;Source: "..\Plugins\Contrib\bin\*.dll"; DestDir: "{app}\Plugins\Contrib"; Flags: ignoreversion recursesubdirs createallsubdirs


[Icons]
Name: "{group}\pGina"; Filename: "{app}\pGina.Configuration.exe"
Name: "{commondesktop}\pGina"; Filename: "{app}\pGina.Configuration.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\pGina.InstallUtil.exe"; Parameters: "post-install"; StatusMsg: "Installing service, CP/GINA, and setting permissions..."; WorkingDir: "{app}"; Flags: runhidden
Filename: "{app}\pGina.Configuration.exe"; Description: "{cm:LaunchProgram,pGina}"; Flags: nowait postinstall skipifsilent runascurrentuser

[UninstallRun]
Filename: "{app}\pGina.InstallUtil.exe"; Parameters: "post-uninstall"; StatusMsg: "Removing service and CP/GINA..."; WorkingDir: "{app}"; Flags: runhidden; RunOnceId: "UninstallService"

; More custom stuff from [] for ensuring user gets everything needed
[Files]
Source: "scripts\isxdl\isxdl.dll"; Flags: dontcopy

[Code]
procedure isxdl_AddFile(URL, Filename: PAnsiChar);
external 'isxdl_AddFile@files:isxdl.dll stdcall';

function isxdl_DownloadFiles(hWnd: Integer): Integer;
external 'isxdl_DownloadFiles@files:isxdl.dll stdcall';

function isxdl_SetOption(Option, Value: PAnsiChar): Integer;
external 'isxdl_SetOption@files:isxdl.dll stdcall';

[CustomMessages]
DependenciesDir=MyProgramDependencies

en.depdownload_msg=The following applications are required before setup can continue:%n%n%1%nDownload and install now?

en.depdownload_admin=An Administrator account is required installing these dependencies.%nPlease run this setup again using 'Run as Administrator' or install the following dependencies manually:%n%n%1%nClose this message and press Cancel to exit setup.
;de.depdownload_admin=

en.previousinstall_admin=This setup was previously run as Administrator. A non-administrator is not allowed to update in the selected location.%n%nPlease run this setup again using 'Run as Administrator'.%nClose this message and press Cancel to exit setup.
;de.previousinstall_admin=

en.depdownload_memo_title=Download dependencies

en.depinstall_memo_title=Install dependencies

en.depinstall_title=Installing dependencies

en.depinstall_description=Please wait while Setup installs dependencies on your computer.

en.depinstall_status=Installing %1...

en.depinstall_missing=%1 must be installed before setup can continue. Please install %1 and run Setup again.

en.depinstall_error=An error occured while installing the dependencies. Please restart the computer and run the setup again or install the following dependencies manually:%n

en.isxdl_langfile=


[Files]
Source: "scripts\isxdl\german2.ini"; Flags: dontcopy

[Code]
type
	TProduct = record
		File: String;
		Title: String;
		Parameters: String;
		InstallClean : Boolean;
		MustRebootAfter : Boolean;
        RequestRestart : Boolean;
	end;
	
var
	installMemo, downloadMemo, downloadMessage: string;
	products: array of TProduct;
	DependencyPage: TOutputProgressWizardPage;

	rebootRequired : boolean;
	rebootMessage : string;
  
procedure AddProduct(FileName, Parameters, Title, Size, URL: string; InstallClean : Boolean; MustRebootAfter : Boolean);
var
	path: string;
	i: Integer;
begin
	installMemo := installMemo + '%1' + Title + #13;
	
	path := ExpandConstant('{src}{\}') + CustomMessage('DependenciesDir') + '\' + FileName;
	if not FileExists(path) then begin
		path := ExpandConstant('{tmp}{\}') + FileName;
		
		isxdl_AddFile(URL, path);
		
		downloadMemo := downloadMemo + '%1' + Title + #13;
		downloadMessage := downloadMessage + '    ' + Title + ' (' + Size + ')' + #13;
	end;
	
	i := GetArrayLength(products);
	SetArrayLength(products, i + 1);
	products[i].File := path;
	products[i].Title := Title;
	products[i].Parameters := Parameters;
	products[i].InstallClean := InstallClean;
	products[i].MustRebootAfter := MustRebootAfter;
	products[i].RequestRestart := false;
end;

function GetProductcount: integer;
begin
    Result := GetArrayLength(products);
end;

function SmartExec(prod : TProduct; var ResultCode : Integer) : Boolean;
begin
    if (UpperCase(Copy(prod.File,Length(prod.File)-2,3)) <> 'EXE') then begin
        Result := ShellExec('', prod.File, prod.Parameters, '', SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode);
    end else begin
        Result := Exec(prod.File, prod.Parameters, '', SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode);
    end;
    if (ResultCode = 3010) then begin
        prod.RequestRestart := true;
        ResultCode := 0;
    end;
end;

function PendingReboot : Boolean;
var	Names: String;
begin
  if (RegQueryMultiStringValue(HKEY_LOCAL_MACHINE, 'SYSTEM\CurrentControlSet\Control\Session Manager', 'PendingFileRenameOperations', Names)) then begin
      Result := true;
  end else if ((RegQueryMultiStringValue(HKEY_LOCAL_MACHINE, 'SYSTEM\CurrentControlSet\Control\Session Manager', 'SetupExecute', Names)) and (Names <> ''))  then begin
		Result := true;
	end
	else begin
	  Result := false;
  end;		
end;

function InstallProducts: Boolean;
var
	ResultCode, i, productCount, finishCount: Integer;
begin
	Result := true;
	productCount := GetArrayLength(products);
		
	if productCount > 0 then begin
		DependencyPage := CreateOutputProgressPage(CustomMessage('depinstall_title'), CustomMessage('depinstall_description'));
		DependencyPage.Show;
		
		for i := 0 to productCount - 1 do begin
		    if ((products[i].InstallClean) and PendingReboot)  then begin
		        rebootRequired := true;
		        rebootmessage := products[i].Title;
		        exit;
		    end;
		  
		    DependencyPage.SetText(FmtMessage(CustomMessage('depinstall_status'), [products[i].Title]), '');
		    DependencyPage.SetProgress(i, productCount);
			
            if SmartExec(products[i], ResultCode) then begin
				if ResultCode = 0 then
					finishCount := finishCount + 1;
				if (products[i].MustRebootAfter = true) then begin
				    rebootRequired := true;
				    rebootmessage := products[i].Title;
				    if not PendingReboot then begin
  				        RegWriteMultiStringValue(HKEY_LOCAL_MACHINE, 'SYSTEM\CurrentControlSet\Control\Session Manager', 'PendingFileRenameOperations', '');
                    end;
                    exit;
                end;
            end
			else begin
			    Result := false;
				break;
			end;
	    end;
		
		for i := 0 to productCount - finishCount - 1 do begin
			products[i] := products[i+finishCount];
		end;
		SetArrayLength(products, productCount - finishCount);
		
		DependencyPage.Hide;
	end;
end;


function PrepareToInstall(var NeedsRestart: Boolean): String;
var
	i: Integer;
	s: string;
begin
	if not InstallProducts() then begin
		s := CustomMessage('depinstall_error');
		
		for i := 0 to GetArrayLength(products) - 1 do begin
			s := s + #13 + '    ' + products[i].Title;
		end;
		
		Result := s;
	end
  else if (rebootrequired) then
	begin
	   Result := RebootMessage;
	   NeedsRestart := true;
	    RegWriteStringValue(HKEY_CURRENT_USER, 'SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce',
                           'InstallBootstrap', ExpandConstant('{srcexe}'));
	end;
end;


function NeedRestart : Boolean;
var i: integer;
begin
    result := false;
	for i := 0 to GetArrayLength(products) - 1 do
        result := result or products[i].RequestRestart;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
var
	s: string;
begin
	if downloadMemo <> '' then
		s := s + CustomMessage('depdownload_memo_title') + ':' + NewLine + FmtMessage(downloadMemo, [Space]) + NewLine;
	if installMemo <> '' then
		s := s + CustomMessage('depinstall_memo_title') + ':' + NewLine + FmtMessage(installMemo, [Space]) + NewLine;

	s := s + MemoDirInfo + NewLine + NewLine + MemoGroupInfo
	
	if MemoTasksInfo <> '' then
		s := s + NewLine + NewLine + MemoTasksInfo;

	Result := s
end;


function NextButtonClick(CurPageID: Integer): Boolean;
var pf: string;
begin
	Result := true;

    if (CurPageID = wpWelcome) and (not IsAdmin()) and Result then begin
   
        if (Wizardform.PrevAppDir <> '') then begin
            pf := ExpandConstant('{pf}');
            if Copy(Wizardform.PrevAppDir,1,Length(pf)) = pf then begin
                SuppressibleMsgBox(CustomMessage('previousinstall_admin'), mbConfirmation, MB_OK, IDOK);
                Result := false;
            end;
        end;
    end;
    if (CurPageID = wpWelcome) and (GetArrayLength(products) > 0) and (not IsAdminLoggedOn()) and Result then begin
        SuppressibleMsgBox(FmtMessage(CustomMessage('depdownload_admin'), [downloadMessage]), mbConfirmation, MB_OK, IDOK);
        Result := false;
    end;
	if CurPageID = wpReady then begin

		if downloadMemo <> '' then begin
			if ActiveLanguage() <> 'en' then begin
				ExtractTemporaryFile(CustomMessage('isxdl_langfile'));
				isxdl_SetOption('language', ExpandConstant('{tmp}{\}') + CustomMessage('isxdl_langfile'));
			end;
			
			if SuppressibleMsgBox(FmtMessage(CustomMessage('depdownload_msg'), [downloadMessage]), mbConfirmation, MB_YESNO, IDYES) = IDNO then
				Result := false
			else if isxdl_DownloadFiles(StrToInt(ExpandConstant('{wizardhwnd}'))) = 0 then
				Result := false;
		end;
	end;
end;

function IsX64: Boolean;
begin
	Result := Is64BitInstallMode and (ProcessorArchitecture = paX64);
end;

function IsIA64: Boolean;
begin
	Result := Is64BitInstallMode and (ProcessorArchitecture = paX64);
end;

function GetURL(x86, x64, ia64: String): String;
begin
	if IsX64() and (x64 <> '') then
		Result := x64;
	if IsIA64() and (ia64 <> '') then
		Result := ia64;
	
	if Result = '' then
		Result := x86;
end;

[Code]
var
	WindowsVersion: TWindowsVersion;
	
procedure initwinversion();
begin
	GetWindowsVersionEx(WindowsVersion);
end;

function exactwinversion(MajorVersion, MinorVersion: integer): boolean;
begin
	Result := (WindowsVersion.Major = MajorVersion) and (WindowsVersion.Minor = MinorVersion);
end;

function minwinversion(MajorVersion, MinorVersion: integer): boolean;
begin
	Result := (WindowsVersion.Major > MajorVersion) or ((WindowsVersion.Major = MajorVersion) and (WindowsVersion.Minor >= MinorVersion));
end;

function maxwinversion(MajorVersion, MinorVersion: integer): boolean;
begin
	Result := (WindowsVersion.Major < MajorVersion) or ((WindowsVersion.Major = MajorVersion) and (WindowsVersion.Minor <= MinorVersion));
end;

function exactwinspversion(MajorVersion, MinorVersion, SpVersion: integer): boolean;
begin
	if exactwinversion(MajorVersion, MinorVersion) then
		Result := WindowsVersion.ServicePackMajor = SpVersion
	else
		Result := true;
end;

function minwinspversion(MajorVersion, MinorVersion, SpVersion: integer): boolean;
begin
	if exactwinversion(MajorVersion, MinorVersion) then
		Result := WindowsVersion.ServicePackMajor >= SpVersion
	else
		Result := true;
end;

function maxwinspversion(MajorVersion, MinorVersion, SpVersion: integer): boolean;
begin
	if exactwinversion(MajorVersion, MinorVersion) then
		Result := WindowsVersion.ServicePackMajor <= SpVersion
	else
		Result := true;
end;
[Code]
function GetFullVersion(VersionMS, VersionLS: cardinal): string;
var
	version: string;
begin
	version := IntToStr(word(VersionMS shr 16));
	version := version + '.' + IntToStr(word(VersionMS and not $ffff0000));
	
	version := version + '.' + IntToStr(word(VersionLS shr 16));
	version := version + '.' + IntToStr(word(VersionLS and not $ffff0000));
	
	Result := version;
end;

function fileversion(file: string): string;
var
	versionMS, versionLS: cardinal;
begin
	if GetVersionNumbers(file, versionMS, versionLS) then
		Result := GetFullVersion(versionMS, versionLS)
	else
		Result := '0';
end;


[CustomMessages]
dotnetfx40client_title=.NET 4.0 Client Framework

dotnetfx40client_size=3 MB - 197 MB
en.dotnetfx40client_lcid=''


[Code]
const
	dotnetfx40client_url = 'http://download.microsoft.com/download/7/B/6/7B629E05-399A-4A92-B5BC-484C74B5124B/dotNetFx40_Client_setup.exe';

function dotnetfx40client(checkOnly : boolean) : boolean;
var
	version: cardinal;
begin
    result := true;
	RegQueryDWordValue(HKLM, 'Software\Microsoft\NET Framework Setup\NDP\v4\client', 'Install', version);
	if version <> 1 then begin
        result := false;
        if not checkOnly then
    		AddProduct('dotNetFx40_Client_setup.exe',
    			CustomMessage('dotnetfx40client_lcid') + '/q ' + '/passive ' + '/norestart',
    			CustomMessage('dotnetfx40client_title'),
    			CustomMessage('dotnetfx40client_size'),
    			dotnetfx40client_url,false,false);
    end;
end;

[CustomMessages]
dotnetfx40full_title=.NET 4.0 Full Framework

dotnetfx40full_size=3 MB - 197 MB
en.dotnetfx40full_lcid=''


[Code]
const
	dotnetfx40full_url = 'http://download.microsoft.com/download/1/B/E/1BE39E79-7E39-46A3-96FF-047F95396215/dotNetFx40_Full_setup.exe';

function dotnetfx40full(checkOnly : boolean) : boolean;
var
	version: cardinal;
begin
    result := true;
	RegQueryDWordValue(HKLM, 'Software\Microsoft\NET Framework Setup\NDP\v4\full', 'Install', version);
	if version <> 1 then begin
        result := false;
        if not checkOnly then
    		AddProduct('dotNetFx40_Full_setup.exe',
    			CustomMessage('dotnetfx40full_lcid') + '/q ' + '/passive ' + '/norestart',
    			CustomMessage('dotnetfx40full_title'),
    			CustomMessage('dotnetfx40full_size'),
    			dotnetfx40full_url,false,false);
    end;
end;

[Code]
type
	SERVICE_STATUS = record
    	dwServiceType				: cardinal;
    	dwCurrentState				: cardinal;
    	dwControlsAccepted			: cardinal;
    	dwWin32ExitCode				: cardinal;
    	dwServiceSpecificExitCode	: cardinal;
    	dwCheckPoint				: cardinal;
    	dwWaitHint					: cardinal;
	end;
	HANDLE = cardinal;

const
	SERVICE_QUERY_CONFIG		= $1;
	SERVICE_CHANGE_CONFIG		= $2;
	SERVICE_QUERY_STATUS		= $4;
	SERVICE_START				= $10;
	SERVICE_STOP				= $20;
	SERVICE_ALL_ACCESS			= $f01ff;
	SC_MANAGER_ALL_ACCESS		= $f003f;
	SERVICE_WIN32_OWN_PROCESS	= $10;
	SERVICE_WIN32_SHARE_PROCESS	= $20;
	SERVICE_WIN32				= $30;
	SERVICE_INTERACTIVE_PROCESS = $100;
	SERVICE_BOOT_START          = $0;
	SERVICE_SYSTEM_START        = $1;
	SERVICE_AUTO_START          = $2;
	SERVICE_DEMAND_START        = $3;
	SERVICE_DISABLED            = $4;
	SERVICE_DELETE              = $10000;
	SERVICE_CONTROL_STOP		= $1;
	SERVICE_CONTROL_PAUSE		= $2;
	SERVICE_CONTROL_CONTINUE	= $3;
	SERVICE_CONTROL_INTERROGATE = $4;
	SERVICE_STOPPED				= $1;
	SERVICE_START_PENDING       = $2;
	SERVICE_STOP_PENDING        = $3;
	SERVICE_RUNNING             = $4;
	SERVICE_CONTINUE_PENDING    = $5;
	SERVICE_PAUSE_PENDING       = $6;
	SERVICE_PAUSED              = $7;

function OpenSCManager(lpMachineName, lpDatabaseName: AnsiString; dwDesiredAccess :cardinal): HANDLE;
external 'OpenSCManagerA@advapi32.dll stdcall';

function OpenService(hSCManager :HANDLE;lpServiceName: AnsiString; dwDesiredAccess :cardinal): HANDLE;
external 'OpenServiceA@advapi32.dll stdcall';

function CloseServiceHandle(hSCObject :HANDLE): boolean;
external 'CloseServiceHandle@advapi32.dll stdcall';

function CreateService(hSCManager :HANDLE;lpServiceName, lpDisplayName: AnsiString;dwDesiredAccess,dwServiceType,dwStartType,dwErrorControl: cardinal;lpBinaryPathName,lpLoadOrderGroup: AnsiString; lpdwTagId : cardinal;lpDependencies,lpServiceStartName,lpPassword :AnsiString): cardinal;
external 'CreateServiceA@advapi32.dll stdcall';

function DeleteService(hService :HANDLE): boolean;
external 'DeleteService@advapi32.dll stdcall';

function StartNTService(hService :HANDLE;dwNumServiceArgs : cardinal;lpServiceArgVectors : cardinal) : boolean;
external 'StartServiceA@advapi32.dll stdcall';

function ControlService(hService :HANDLE; dwControl :cardinal;var ServiceStatus :SERVICE_STATUS) : boolean;
external 'ControlService@advapi32.dll stdcall';

function QueryServiceStatus(hService :HANDLE;var ServiceStatus :SERVICE_STATUS) : boolean;
external 'QueryServiceStatus@advapi32.dll stdcall';

function QueryServiceStatusEx(hService :HANDLE;ServiceStatus :SERVICE_STATUS) : boolean;
external 'QueryServiceStatus@advapi32.dll stdcall';

function OpenServiceManager() : HANDLE;
begin
	if UsingWinNT() = true then begin
		Result := OpenSCManager('','ServicesActive',SC_MANAGER_ALL_ACCESS);
		if Result = 0 then
			MsgBox('the servicemanager is not available', mbError, MB_OK)
	end
	else begin
			MsgBox('only nt based systems support services', mbError, MB_OK)
			Result := 0;
	end
end;

function IsServiceInstalled(ServiceName: string) : boolean;
var
	hSCM	: HANDLE;
	hService: HANDLE;
begin
	hSCM := OpenServiceManager();
	Result := false;
	if hSCM <> 0 then begin
		hService := OpenService(hSCM,ServiceName,SERVICE_QUERY_CONFIG);
        if hService <> 0 then begin
            Result := true;
            CloseServiceHandle(hService)
		end;
        CloseServiceHandle(hSCM)
	end
end;

function InstallService(FileName, ServiceName, DisplayName, Description : string;ServiceType,StartType :cardinal) : boolean;
var
	hSCM	: HANDLE;
	hService: HANDLE;
begin
	hSCM := OpenServiceManager();
	Result := false;
	if hSCM <> 0 then begin
		hService := CreateService(hSCM,ServiceName,DisplayName,SERVICE_ALL_ACCESS,ServiceType,StartType,0,FileName,'',0,'','','');
		if hService <> 0 then begin
			Result := true;
			if Description<> '' then
				RegWriteStringValue(HKLM,'System\CurrentControlSet\Services' + ServiceName,'Description',Description);
			CloseServiceHandle(hService)
		end;
        CloseServiceHandle(hSCM)
	end
end;

function RemoveService(ServiceName: string) : boolean;
var
	hSCM	: HANDLE;
	hService: HANDLE;
begin
	hSCM := OpenServiceManager();
	Result := false;
	if hSCM <> 0 then begin
		hService := OpenService(hSCM,ServiceName,SERVICE_DELETE);
        if hService <> 0 then begin
            Result := DeleteService(hService);
            CloseServiceHandle(hService)
		end;
        CloseServiceHandle(hSCM)
	end
end;

function StartService(ServiceName: string) : boolean;
var
	hSCM	: HANDLE;
	hService: HANDLE;
begin
	hSCM := OpenServiceManager();
	Result := false;
	if hSCM <> 0 then begin
		hService := OpenService(hSCM,ServiceName,SERVICE_START);
        if hService <> 0 then begin
        	Result := StartNTService(hService,0,0);
            CloseServiceHandle(hService)
		end;
        CloseServiceHandle(hSCM)
	end;
end;

function StopService(ServiceName: string) : boolean;
var
	hSCM	: HANDLE;
	hService: HANDLE;
	Status	: SERVICE_STATUS;
begin
	hSCM := OpenServiceManager();
	Result := false;
	if hSCM <> 0 then begin
		hService := OpenService(hSCM,ServiceName,SERVICE_STOP);
        if hService <> 0 then begin
        	Result := ControlService(hService,SERVICE_CONTROL_STOP,Status);
            CloseServiceHandle(hService)
		end;
        CloseServiceHandle(hSCM)
	end;
end;

function IsServiceRunning(ServiceName: string) : boolean;
var
	hSCM	: HANDLE;
	hService: HANDLE;
	Status	: SERVICE_STATUS;
begin
	hSCM := OpenServiceManager();
	Result := false;
	if hSCM <> 0 then begin
		hService := OpenService(hSCM,ServiceName,SERVICE_QUERY_STATUS);
    	if hService <> 0 then begin
			if QueryServiceStatus(hService,Status) then begin
				Result :=(Status.dwCurrentState = SERVICE_RUNNING)
        	end;
            CloseServiceHandle(hService)
		    end;
        CloseServiceHandle(hSCM)
	end
end;

function SetupService(service, port, comment: string) : boolean;
var
	filename	: string;
	s			: string;
	lines		: TArrayOfString;
	n			: longint;
	i			: longint;
	servnamlen	: integer;
	error		: boolean;
begin
	if UsingWinNT() = true then
		filename := ExpandConstant('{sys}\drivers\etc\services')
	else
		filename := ExpandConstant('{win}\services');

	if LoadStringsFromFile(filename,lines) = true then begin
		Result		:= true;
		n			:= GetArrayLength(lines) - 1;
		servnamlen	:= Length(service);
		error		:= false;

		for i:=0 to n do begin
			if Copy(lines[i],1,1) <> '#' then begin
				s := Copy(lines[i],1,servnamlen);
				if CompareText(s,service) = 0 then
					exit; // found service-entry

				if Pos(port,lines[i]) > 0 then begin
					error := true;
					lines[i] := '#' + lines[i] + '   # disabled because collision with  ' + service + ' service';
				end;
			end
			else if CompareText(Copy(lines[i],2,servnamlen),service) = 0 then begin
				Delete(lines[i],1,1);
				Result := SaveStringsToFile(filename,lines,false);
				exit;
			end;
		end;

		if error = true then begin
			if SaveStringsToFile(filename,lines,false) = false then begin
				Result := false;
				exit;
			end;
		end;

		s := service + '       ' + port + '           # ' + comment + #13#10;
		if SaveStringToFile(filename,s,true) = false then begin
			Result := false;
			exit;
		end;

		if error = true then begin
			MsgBox('the ' + service + ' port was already used. The old service is disabled now. You should check the services file manually now.',mbInformation,MB_OK);
		end;
	end
	else
		Result := false;
end;

function CheckVersion(Filename : string;hh,hl,lh,ll : integer) : boolean;
var
	VersionMS	: cardinal;
	VersionLS	: cardinal;
	CheckMS		: cardinal;
	CheckLS		: cardinal;
begin
	if GetVersionNumbers(Filename,VersionMS,VersionLS) = false then
		Result := false
	else begin
		CheckMS := (hh shl $10) or hl;
		CheckLS := (lh shl $10) or ll;
		Result := (VersionMS > CheckMS) or ((VersionMS = CheckMS) and (VersionLS >= CheckLS));
	end;
end;

function NeedShellFolderUpdate() : boolean;
begin
	Result := CheckVersion('ShFolder.dll',5,50,4027,300) = false;
end;

function NeedVCRedistUpdate() : boolean;
begin
	Result := (CheckVersion('mfc42.dll',6,0,8665,0) = false)
		or (CheckVersion('msvcrt.dll',6,0,8797,0) = false)
		or (CheckVersion('comctl32.dll',5,80,2614,3600) = false);
end;

function NeedHTMLHelpUpdate() : boolean;
begin
	Result := CheckVersion('hh.exe',4,72,0,0) = false;
end;

function NeedWinsockUpdate() : boolean;
begin
	Result := (UsingWinNT() = false) and (CheckVersion('mswsock.dll',4,10,0,1656) = false);
end;

function NeedDCOMUpdate() : boolean;
begin
	Result := (UsingWinNT() = false) and (CheckVersion('oleaut32.dll',2,30,0,0) = false);
end;





[CustomMessages]
win2000sp3_title=Windows 2000 Service Pack 3
winxpsp2_title=Windows XP Service Pack 2
winxpsp3_title=Windows XP Service Pack 3

