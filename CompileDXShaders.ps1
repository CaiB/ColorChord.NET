[string] $FXCPath = 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\fxc.exe';
[string] $ShadersPath = './Outputs/DisplayD3D12Modes/Shaders/';
[string] $CompiledPath = Join-Path $ShadersPath 'Compiled/';

Write-Output 'Compiling vertex shaders...';
Get-ChildItem (Join-Path $ShadersPath 'Vertex') -Filter '*.hlsl' | ForEach-Object `
{
    [string] $OutFile = Join-Path $CompiledPath "$($_.BaseName).cso";
    # TODO: It might be nice to read these from a comment at the top of the file instead of hardcoding :)
    [string] $ShaderType = 'vs_5_1';
    [string] $EntryPoint = 'main';
    & $FXCPath /Fo $OutFile /T $ShaderType /E $EntryPoint $_.FullName;
}

Write-Output 'Compiling pixel shaders...';
Get-ChildItem (Join-Path $ShadersPath 'Pixel') -Filter '*.hlsl' | ForEach-Object `
{
    [string] $OutFile = Join-Path $CompiledPath "$($_.BaseName).cso";
    # TODO: It might be nice to read these from a comment at the top of the file instead of hardcoding :)
    [string] $ShaderType = 'ps_5_1';
    [string] $EntryPoint = 'main';
    & $FXCPath /Fo $OutFile /T $ShaderType /E $EntryPoint $_.FullName;
}