& nuget restore

& dotnet publish "$env:APPVEYOR_BUILD_FOLDER\ColorChord.NET\ColorChord.NET.csproj" --output "$env:APPVEYOR_BUILD_FOLDER\ColorChord.NET\PublishResult" --configuration Release --verbosity minimal
if ($LastExitCode -NE 0) { Exit -1; }

$ExtensionDirs = Get-ChildItem "$env:APPVEYOR_BUILD_FOLDER\Extensions\" -Directory
$ExtensionDirs | ForEach-Object
{
    & dotnet publish "$env:APPVEYOR_BUILD_FOLDER\Extensions\$($_.Name)\$($_.Name).csproj" --output "$env:APPVEYOR_BUILD_FOLDER\Extensions\PublishResult" -p:OutputPath="$env:APPVEYOR_BUILD_FOLDER\Extensions\PublishResult" --configuration Release --verbosity minimal
    if ($LastExitCode -NE 0) { Exit -1; }
}

# Remove-Item "$env:APPVEYOR_BUILD_FOLDER\Extensions\PublishResult\ColorChord.NET-API.*"

& 7z a "ColorChord.NET-autobuild-v$($env:appveyor_build_version).zip" "$env:APPVEYOR_BUILD_FOLDER\ColorChord.NET\PublishResult\*"
& 7z a "ColorChord.NET-Extensions-autobuild-v$($env:appveyor_build_version).zip" "$env:APPVEYOR_BUILD_FOLDER\Extensions\PublishResult\CC.NET-Ext-*"