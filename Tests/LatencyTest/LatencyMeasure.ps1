using namespace System.Net.Sockets;
using namespace System.Collections.Generic;
using namespace System.Text;
using namespace System.Diagnostics;
using namespace System.Linq;
using namespace System.IO;

# Developed for a Rigol DS1054Z, but may work on other oscilloscopes with a few tweaks
# Channel 1 is assumed to be the light sensor, channel 3 the audio

$ErrorActionPreference = 'Stop';
[string] $SCOPE_IP = '192.168.39.24';
[ushort] $SCOPE_PORT = 5555;
[byte] $NEWLINE_BYTE = [byte]([char]"`n");
[int] $TIMEOUT_MS = 1000;
[int] $WAIT_MS = 5000;
[int] $MAX_BYTES = 100000;
[int] $CYCLE_COUNT = 10;

[TcpClient] $Scope = [TcpClient]::new();
$Scope.Connect([IPEndPoint]::new([IPAddress]::Parse($SCOPE_IP), $SCOPE_PORT));
$Scope.ReceiveTimeout = $TIMEOUT_MS;
$Scope.SendTimeout = $TIMEOUT_MS;
[NetworkStream] $ScopeStream = $Scope.GetStream();

function Flush-Instr { while ($ScopeStream.Available -GT 0) { $ScopeStream.Read(); } }
function Send-InstrCommand
{
    param ([Parameter(Mandatory = $true, Position = 0)] [string] $Command);
    $ScopeStream.Write([Encoding]::UTF8.GetBytes("$Command`n"));
}

function Receive-InstrRaw
{
    [Stopwatch] $WaitTimer = [Stopwatch]::StartNew();
    [List[byte]] $ReceivedData = [List[byte]]::new(1000);
    while ($WaitTimer.ElapsedMilliseconds -LT $TIMEOUT_MS)
    {
        if ($Scope.Available -LE 0)
        {
            Start-Sleep -Milliseconds 25;
            continue;
        }

        $WaitTimer.Restart();
        [byte[]] $Buffer = [byte[]]::new($Scope.Available);
        [int] $BytesRead = $ScopeStream.Read($Buffer, 0, $Buffer.Length);
        $ReceivedData.AddRange([Enumerable]::Take($Buffer, $BytesRead));
        if ($ReceivedData.Contains($NEWLINE_BYTE)) { break; }
    }
    return $ReceivedData;
}

function Receive-InstrString
{
    [List[byte]] $ReceivedData = Receive-InstrRaw;
    if ($null -EQ $ReceivedData || $ReceivedData.Count -EQ 0) { return ''; }
    return [Encoding]::UTF8.GetString($ReceivedData).Trim();;
}

function Receive-InstrBool
{
    [string] $Result = $(Receive-InstrString).Trim();
    return ($Result -EQ '1' || $Result -EQ 'ON' || $Result -EQ 'YES');
}

function Receive-InstrState
{
    Send-InstrCommand ':TRIG:STAT?';
    return $(Receive-InstrString).Trim();
}

function Get-InstrRunning { return 'TD','WAIT','RUN','AUTO' -Contains $(Receive-InstrState); }
function Start-InstrRun { Send-InstrCommand ':RUN'; }
function Start-InstrSingle { Send-InstrCommand ':SING'; }
function Stop-Instr { Send-InstrCommand ':STOP'; }

function Receive-InstrWaveformPreamble
{
    Send-InstrCommand ':WAV:PRE?';
    [string[]] $Parts = $(Receive-InstrString) -Split ',';
    return @{
        'PointCount' = [int]$Parts[2];
        'XIncrement' = [float]$Parts[4];
        'XOrigin' = [float]$Parts[5];
        'XReference' = [float]$Parts[6];
        'YIncrement' = [float]$Parts[7];
        'YOrigin' = [float]$Parts[8];
        'YReference' = [float]$Parts[9];
    };
}

function Receive-InstrWaveformData
{
    param ([Parameter(Position = 0)] [int] $Channel = 1);
    Send-InstrCommand ":WAV:SOUR CHAN$Channel";
    Send-InstrCommand ':WAV:MODE RAW';
    Send-InstrCommand ':WAV:FORM BYTE';
    $WaveformMetadata = Receive-InstrWaveformPreamble;
    [int] $PointCount = $WaveformMetadata.PointCount;
    [int] $Pos = 1;
    [List[byte]] $Data = [List[byte]]::new($PointCount);
    while ($Data.Count -LT $PointCount)
    {
        [int] $EndPos = [Math]::Min($PointCount, $Pos + $MAX_BYTES);
        Send-InstrCommand ":WAV:STAR $Pos";
        Send-InstrCommand ":WAV:STOP $EndPos";
        #Write-Host "$Pos to $EndPos";
        Send-InstrCommand ':WAV:DATA?';
        [List[byte]] $WaveData = Receive-InstrRaw;
        if ($WaveData[0] -NE [byte]([char]'#')) { throw "Expected data first byte to be '#' char, instead got '$([char]$WaveData[0])'"; }
        [int] $DataLenLen = $WaveData[1] - 48;
        [int] $DataLen = [int][Encoding]::UTF8.GetString([Enumerable]::Take([Enumerable]::Skip($WaveData, 2), $DataLenLen));
        $Data.AddRange([Enumerable]::Take([Enumerable]::Skip($WaveData, 2 + $DataLenLen), $DataLen));
        #Write-Host $DataLen;
        $Pos += $DataLen;
    }
    return $Data, $WaveformMetadata;
}

function Set-InstrMemDepth
{
    param ([Parameter(Position = 0)] [int] $PointCount = 1200);
    if (!(Get-InstrRunning))
    {
        Start-InstrRun;
        Send-InstrCommand ":ACQ:MDEP $PointCount";
        Stop-Instr;
    }
    else { Send-InstrCommand ":ACQ:MDEP $PointCount"; }
    Wait-InstrReady | Out-Null;
}

function Get-InstrMemDepth
{
    Send-InstrCommand ':ACQ:MDEP?';
    return [int] $(Receive-InstrString);
}

function Wait-InstrReady
{
    [Stopwatch] $WaitTimer = [Stopwatch]::StartNew();
    while ($WaitTimer.ElapsedMilliseconds -LT $WAIT_MS)
    {
        Flush-Instr;
        Send-InstrCommand '*OPC?';
        if (Receive-InstrBool) { return $true; }
    }
    return $false;
}

function Wait-InstrTrigger
{
    param ([Parameter(Position = 0)] [int] $Timeout = $WAIT_MS);
    [Stopwatch] $WaitTimer = [Stopwatch]::StartNew();
    while ($WaitTimer.ElapsedMilliseconds -LT $Timeout)
    {
        Send-InstrCommand ':TRIG:STAT?';
        if (Receive-InstrString -EQ 'STOP') { return $true; }
    }
    return $false;
}

Send-InstrCommand '*IDN?';
Write-Host "Instrument is: $(Receive-InstrString)";

Send-InstrCommand ':CHAN1:DISP ON';
Send-InstrCommand ':CHAN2:DISP OFF';
Send-InstrCommand ':CHAN3:DISP ON';
Send-InstrCommand ':CHAN4:DISP OFF';

Send-InstrCommand ':CHAN1:BWLimit 20M';
Send-InstrCommand ':CHAN3:BWLimit 20M';
Send-InstrCommand ':CHAN1:COUP DC';
Send-InstrCommand ':CHAN3:COUP DC';

Send-InstrCommand ':TRIG:MODE EDGE';
Send-InstrCommand ':TRIG:SWE SING';
Send-InstrCommand ':TRIG:EDG:SOUR CHAN3';
# Note: the trigger level needs to be manually set
# Could do with: Send-InstrCommand ':TRIG:EDG:LEV (num)';
Wait-InstrReady | Out-Null;
Set-InstrMemDepth 60000;

[string] $DataPath = Join-Path $PSScriptRoot 'Data/';
if (!(Test-Path $DataPath)) { New-Item -ItemType 'Container' $DataPath; }

Write-Host 'Starting in 5 seconds...';
Start-Sleep 5;

for ($Cycle = 0; $Cycle -LT $CYCLE_COUNT; $Cycle++)
{
    Write-Host -NoNewLine ('Cyc {0:D5} of {1:D5} ' -F ($Cycle + 1), $CYCLE_COUNT); 
    Wait-InstrReady | Out-Null;
    Flush-Instr;
    Start-InstrSingle;

    Write-Host -NoNewLine '| Play ';
    [System.Media.SoundPlayer]::new('./sine.wav').PlaySync();

    Write-Host -NoNewLine '| Capture ';
    [bool] $Triggered = Wait-InstrTrigger -Timeout 1000;
    if ($Triggered)
    {
        Write-Host -NoNewLine '| Download ';
        [List[byte]] $Channel1Data, [PSCustomObject] $Channel1Meta = Receive-InstrWaveformData 1;
        [List[byte]] $Channel3Data, [PSCustomObject] $Channel3Meta = Receive-InstrWaveformData 3;

        Write-Host -NoNewLine '| Save';
        [StreamWriter] $CSV = [StreamWriter]::new($(Join-Path $DataPath $('{0:D5}.csv' -F $Cycle)));
        $CSV.WriteLine('Time,Audio,Display');
        for ($Sample = 0; $Sample -LT $Channel1Data.Count; $Sample++)
        {
            [float] $Time = $Channel1Meta.XOrigin + ($Channel1Meta.XIncrement * $Sample);
            [float] $AudioLevel = $Channel3Meta.YIncrement * ($Channel3Data[$Sample] - $Channel3Meta.YOrigin - $Channel3Meta.YReference);
            [float] $DisplayLevel = $Channel1Meta.YIncrement * ($Channel1Data[$Sample] - $Channel1Meta.YOrigin - $Channel1Meta.YReference);
            $CSV.WriteLine([string]::Format('{0},{1},{2}', $Time, $AudioLevel, $DisplayLevel));
        }
        $CSV.Close();
        Write-Host ' | Done!';
    }
    else
    {
        Stop-Instr;
        Write-Host 'FAIL';
        Write-Host 'Did not trigger. Check the trigger settings and connections.';
    }
}