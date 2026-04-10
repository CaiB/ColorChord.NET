using namespace System.IO;
# Prerequisites:
# PowerShell 7+
# dotnet CLI installed and on PATH

# Maybe need this at some point
# (adds requirement: Nuget installed and on PATH)
#$env:APPVEYOR_BUILD_FOLDER = $PSScriptRoot; #'C:\Users\CaiB\Development\CCToujouTest\ColorChord.NET\';
#$env:APPVEYOR_BUILD_VERSION = '0.0.0.0-toujou';
#./DoAutobuild.ps1

$InformationPreference = 'Continue';

if (!(Test-Path '../BenchmarkResults' -PathType 'Container')) { New-Item -ItemType 'Directory' -Path '../BenchmarkResults' | Out-Null; }
$ResultsDir = Resolve-Path '../BenchmarkResults';

Write-Host 'Building benchmarks project...';
& dotnet publish './Tests/Benchmarks/Benchmarks.csproj' --output './Tests/Benchmarks/PublishResult' --configuration Release --tl:off --verbosity normal | Out-File '../Build_Benchmarks.log';
if ($LastExitCode -NE 0) { Write-Error 'Building the Benchmarks project failed'; return; }
Write-Host 'Build finished.';

Push-Location './Tests/Benchmarks/PublishResult/';
try
{
    Write-Information 'Found benchmarks:';
    & .\Benchmarks.exe --list tree | Write-Information;

    Write-Host 'Running benchmarks...';
    & .\Benchmarks.exe --exporters json --filter "*" --artifacts $ResultsDir --memory --disasm --iterationCount 100 | Write-Information;

    [string] $LogFile = Get-Item $(Join-Path $ResultsDir '*.log'); # TODO: Not sure if this is guaranteed unique
    if ([string]::IsNullOrEmpty($LogFile)) { Write-Error 'No log file found'; continue; }
    [StreamReader] $LogReader = [File]::OpenText($LogFile);
    [bool] $InLogExportSection = $false;
    [string] $LogLine = $LogReader.ReadLine();
    while (!$LogReader.EndOfStream)
    {
        if ($LogLine -EQ '// * Export *') { $InLogExportSection = $true; }
        elseif ($InLogExportSection -AND [string]::IsNullOrWhiteSpace($LogLine)) { $InLogExportSection = $false; }
        elseif ($InLogExportSection -AND ($LogLine -Match '^\s*(.*\.json)$')) { Write-Output $Matches[1]; }
        $LogLine = $LogReader.ReadLine();
    }
    $LogReader.Dispose();
}
finally { Pop-Location; }