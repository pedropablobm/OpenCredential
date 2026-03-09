[CustomMessages]
vc2013x86_title=Microsoft Visual C++ 2013 Redistributable (x86)
vc2013x64_title=Microsoft Visual C++ 2013 Redistributable (x64)

en.vc2013x86_size=6.5 MB
en.vc2013x64_size=7.0 MB

#ifdef dotnet_Passive
#define vc2013_passive "'/passive '"
#else
#define vc2013_passive "''"
#endif

[Code]
const
    vc2013x86_url = 'https://aka.ms/highdpimfc2013x86enu';
    vc2013x64_url = 'https://aka.ms/highdpimfc2013x64enu';

procedure vc2013();
var
    version: cardinal;
begin
    // x86 (32 bit) runtime - REQUIRED for Credential Provider
    version := 0;
    if not RegQueryDWordValue(HKLM32, 'SOFTWARE\Microsoft\VisualStudio\12.0\VC\VCRedist\x86', 'Installed', version) then
        RegQueryDWordValue(HKLM32, 'SOFTWARE\Microsoft\VisualStudio\12.0\VC\Runtimes\x86', 'Installed', version);
    
    if version <> 1 then
        AddProduct('vcredist_x86_2013.exe',
            '/q ' + {#vc2013_passive} + '/norestart',
            CustomMessage('vc2013x86_title'),
            CustomMessage('vc2013x86_size'),
            vc2013x86_url, false, false);
    
    // x64 (64 bit) runtime
    if isX64 then begin
        version := 0;
        if not RegQueryDWordValue(HKLM32, 'SOFTWARE\Microsoft\VisualStudio\12.0\VC\VCRedist\x64', 'Installed', version) then
            RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\VisualStudio\12.0\VC\Runtimes\x64', 'Installed', version);
        
        if version <> 1 then
            AddProduct('vcredist_x64_2013.exe',
                '/q ' + {#vc2013_passive} + '/norestart',
                CustomMessage('vc2013x64_title'),
                CustomMessage('vc2013x64_size'),
                vc2013x64_url, false, false);
    end;
end;