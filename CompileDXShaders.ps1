using namespace System;

param
(
    [string] $ShadersPath = $(Join-Path $PSScriptRoot './ColorChord.NET/Outputs/DisplayD3D12Modes/Shaders/'),
    [string] $CompiledPath = $(Join-Path $ShadersPath 'Compiled/'),
    [string] $FXCPath = 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\'
);

[Hashtable] $DefaultModels = 
@{
    'VS' = 'vs_5_1';
    'PS' = 'ps_5_1';
};

[string] $FXC = Join-Path $FXCPath 'fxc.exe';

if (!(Test-Path $CompiledPath)) {New-Item $CompiledPath -ItemType 'Directory' | Out-Null; }

Get-ChildItem $ShadersPath -Filter '*.hlsl' | ForEach-Object `
{
    [string] $FileName = $_.Name;
    if ($FileName -Like 'INC_*')
    {
        Write-Host "Skipping '$FileName'..." -ForegroundColor Green;
        return;
    }
    Write-Host "Compiling shader '$FileName'..." -ForegroundColor Green;
    [string] $ShaderType = $_.Name -Split '_' | Select-Object -First 1;
    [string] $OutFile = Join-Path $CompiledPath "$($_.BaseName).cso";

    [string] $ShaderModel = $DefaultModels[$ShaderType];
    [string] $EntryPoint = 'Main';

    Get-Content $_.FullName | Select-String '^\s*//\s*#compile (.*)$' | % { $_.Matches.Groups[1].Value | Select-String '\s*([^:]+):([^\s]+)\s*' -AllMatches; } | % { $_.Matches; } | % `
    {
        [string] $Key = $_.Groups[1].Value;
        [string] $Val = $_.Groups[2].Value;
        if ($Key -EQ 'ShaderModel') { $ShaderModel = $Val; }
        if ($Key -EQ 'EntryPoint') { $EntryPoint = $Val; }
    };

    & $FXC /nologo /Fo $OutFile /T $ShaderModel /E $EntryPoint $_.FullName;
    if ($LastExitCode -NE 0) { throw "Failed to compile $FileName"; }
}