; DoodleSharp Inno Setup Script
; Builds an installer for DoodleSharp - 2D Geometry Visualization Tool

#define MyAppName "DoodleSharp"
#define MyAppVersion "2026.8.3"
#define MyAppPublisher "Nicety"
#define MyAppExeName "DoodleSharp.exe"
#define MyAppURL "https://github.com/harilalmn/DoodleSharp"

; Paths - adjust if your layout differs
; C:\Work\Nicety\Projects\DoodleSharp\bin\Release\net9.0-windows
#define BuildOutput "bin\Release\net9.0-windows"
#define SampleProjects "Sample Projects"

[Setup]
; DoodleSharp's own AppId. Changing it would make an installer upgrade-in-place over a
; different product, so it stays fixed for the life of the application.
AppId={{B3617608-7E68-45D8-B548-DA5681B286A9}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=installer\output
OutputBaseFilename=DoodleSharp-{#MyAppVersion}-Setup
SetupIconFile=img\logo.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DisableProgramGroupPage=yes
LicenseFile=LICENSE
ChangesAssociations=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "associatevizproj"; Description: "Associate .vizproj files with {#MyAppName}"; GroupDescription: "File associations:"

[Files]
; Main application
Source: "{#BuildOutput}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\DoodleSharp.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\DoodleSharp.deps.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\DoodleSharp.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion

; C2VGeometry (shared geometry library used by sketches and projects)
Source: "{#BuildOutput}\C2VGeometry.dll"; DestDir: "{app}"; Flags: ignoreversion

; Clipper2 (robust polygon boolean-operation engine used by C2VGeometry's BooleanOps/RegionBooleanOps)
Source: "{#BuildOutput}\Clipper2Lib.dll"; DestDir: "{app}"; Flags: ignoreversion

; Direct3D backend (optional; the app falls back to its CPU rasterizer when no device is
; available). Vortice ships managed-only assemblies: there is no native runtimes subfolder for
; them, so they land flat in the build output and each needs its own line here, per the
; explicit-enumeration convention. (Do not spell the native runtimes path out in a comment here --
; the backslash-n was once taken as a newline escape, which split this comment and left a bare line
; that Inno Setup parsed as a [Files] parameter. That aborted the v2026.8.1 release build.)
Source: "{#BuildOutput}\Vortice.Direct3D11.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\Vortice.Direct3D9.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\Vortice.D3DCompiler.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\Vortice.DXGI.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\Vortice.DirectX.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\Vortice.Mathematics.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\SharpGen.Runtime.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\SharpGen.Runtime.COM.dll"; DestDir: "{app}"; Flags: ignoreversion

; Docking layout (AvalonDock). The NuGet packages are named Dirkster.AvalonDock* but the assemblies
; drop that prefix, so these names come from the Release build output rather than the package ids.
; Themes.VS is a transitive dependency of Themes.VS2013 and ships as its own assembly.
Source: "{#BuildOutput}\AvalonDock.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\AvalonDock.Core.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\AvalonDock.Themes.VS.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\AvalonDock.Themes.VS2013.dll"; DestDir: "{app}"; Flags: ignoreversion

; Dependencies
Source: "{#BuildOutput}\ICSharpCode.AvalonEdit.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\Microsoft.CodeAnalysis.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\Microsoft.CodeAnalysis.CSharp.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\Microsoft.CodeAnalysis.CSharp.Scripting.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\Microsoft.CodeAnalysis.Scripting.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\Microsoft.Extensions.DependencyInjection.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\Microsoft.Extensions.DependencyInjection.Abstractions.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\Microsoft.Extensions.Logging.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\Microsoft.Extensions.Logging.Abstractions.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\Microsoft.Extensions.Options.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\Microsoft.Extensions.Primitives.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\Newtonsoft.Json.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\NuGet.Common.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\NuGet.Configuration.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\NuGet.Frameworks.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\NuGet.Packaging.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\NuGet.Protocol.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\NuGet.Versioning.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\PdfSharp.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\PdfSharp.Charting.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\PdfSharp.Quality.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\PdfSharp.Snippets.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\PdfSharp.System.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\PdfSharp.WPFonts.dll"; DestDir: "{app}"; Flags: ignoreversion

; Localization resource DLLs
Source: "{#BuildOutput}\cs\*"; DestDir: "{app}\cs"; Flags: ignoreversion recursesubdirs
Source: "{#BuildOutput}\de\*"; DestDir: "{app}\de"; Flags: ignoreversion recursesubdirs
Source: "{#BuildOutput}\es\*"; DestDir: "{app}\es"; Flags: ignoreversion recursesubdirs
Source: "{#BuildOutput}\fr\*"; DestDir: "{app}\fr"; Flags: ignoreversion recursesubdirs
Source: "{#BuildOutput}\it\*"; DestDir: "{app}\it"; Flags: ignoreversion recursesubdirs
Source: "{#BuildOutput}\ja\*"; DestDir: "{app}\ja"; Flags: ignoreversion recursesubdirs
Source: "{#BuildOutput}\ko\*"; DestDir: "{app}\ko"; Flags: ignoreversion recursesubdirs
Source: "{#BuildOutput}\pl\*"; DestDir: "{app}\pl"; Flags: ignoreversion recursesubdirs
Source: "{#BuildOutput}\pt-BR\*"; DestDir: "{app}\pt-BR"; Flags: ignoreversion recursesubdirs
Source: "{#BuildOutput}\ru\*"; DestDir: "{app}\ru"; Flags: ignoreversion recursesubdirs
Source: "{#BuildOutput}\tr\*"; DestDir: "{app}\tr"; Flags: ignoreversion recursesubdirs
Source: "{#BuildOutput}\zh-Hans\*"; DestDir: "{app}\zh-Hans"; Flags: ignoreversion recursesubdirs
Source: "{#BuildOutput}\zh-Hant\*"; DestDir: "{app}\zh-Hant"; Flags: ignoreversion recursesubdirs

; Sample projects
Source: "{#SampleProjects}\*"; DestDir: "{app}\Samples"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; .vizproj file association
Root: HKA; Subkey: "Software\Classes\.vizproj"; ValueType: string; ValueName: ""; ValueData: "DoodleSharp.VizProj"; Flags: uninsdeletevalue; Tasks: associatevizproj
Root: HKA; Subkey: "Software\Classes\DoodleSharp.VizProj"; ValueType: string; ValueName: ""; ValueData: "DoodleSharp Project File"; Flags: uninsdeletekey; Tasks: associatevizproj
Root: HKA; Subkey: "Software\Classes\DoodleSharp.VizProj\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"; Tasks: associatevizproj
Root: HKA; Subkey: "Software\Classes\DoodleSharp.VizProj\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: associatevizproj

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
function IsDotNet9Installed(): Boolean;
var
  ResultCode: Integer;
  Output: AnsiString;
  TempFile: String;
begin
  Result := False;
  TempFile := ExpandConstant('{tmp}\dotnet_check.txt');
  if Exec('cmd.exe', '/c dotnet --list-runtimes > "' + TempFile + '" 2>&1', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    if LoadStringFromFile(TempFile, Output) then
    begin
      Result := Pos('Microsoft.WindowsDesktop.App 9.', String(Output)) > 0;
    end;
    DeleteFile(TempFile);
  end;
end;

function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if not IsDotNet9Installed() then
  begin
    if MsgBox('{#MyAppName} requires .NET 9.0 Desktop Runtime.'#13#13 +
              'Would you like to download it now?'#13 +
              '(Click No to continue installation anyway)', mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open', 'https://dotnet.microsoft.com/en-us/download/dotnet/9.0', '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
    end;
  end;
end;
