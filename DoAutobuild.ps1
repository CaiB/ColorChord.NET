$BaseDir = $env:APPVEYOR_BUILD_FOLDER;
$BuildVersion = $env:appveyor_build_version;

& nuget restore

# ColorChord.NET
& dotnet publish "$BaseDir\ColorChord.NET\ColorChord.NET.csproj" --output "$BaseDir\ColorChord.NET\PublishResult" --configuration Release --verbosity minimal
if ($LastExitCode -NE 0) { [Environment]::Exit(-1); }

# Extensions
$ExtensionDirs = Get-ChildItem "$BaseDir\Extensions\" -Directory;
$ExtensionDirs | ForEach-Object `
{
    & dotnet publish "$BaseDir\Extensions\$($_.Name)\$($_.Name).csproj" --output "$BaseDir\Extensions\PublishResult" -p:OutputPath="$BaseDir\Extensions\PublishResult" --configuration Release --verbosity minimal
    if ($LastExitCode -NE 0) { [Environment]::Exit(-2); }
}

# Gen2DFTLib
$Gen2Profiles = Get-ChildItem "$BaseDir\Gen2DFTLib\Properties\PublishProfiles\" -Filter '*.pubxml' -File;
$Gen2TargetDir = New-Item -ItemType Directory "$BaseDir\Gen2DFTLib\PublishResult"
$Gen2Profiles | ForEach-Object `
{
    & dotnet publish "$BaseDir\Gen2DFTLib\Gen2DFTLib.csproj" /p:PublishProfile=$($_.FullName)
    if ($LastExitCode -NE 0) { [Environment]::Exit(-3); }
    Copy-Item "$BaseDir\Gen2DFTLib\bin\AOT\*" $Gen2TargetDir;
}

& 7z a -mx9 "ColorChord.NET-autobuild-v$BuildVersion.zip" "$BaseDir\ColorChord.NET\PublishResult\*"
& 7z a -mx9 "ColorChord.NET-Extensions-autobuild-v$BuildVersion.zip" "$BaseDir\Extensions\PublishResult\CC.NET-Ext-*"
& 7z a -mx9 "Gen2DFTLib-autobuild-v$BuildVersion.zip" $(Join-Path $Gen2TargetDir.FullName '\*')
