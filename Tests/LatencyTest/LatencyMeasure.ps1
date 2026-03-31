using namespace System.Net.Sockets;
using namespace System.Collections.Generic;
using namespace System.Text;
using namespace System.Diagnostics;
using namespace System.Linq;
using namespace System.IO;
$ErrorActionPreference = 'Stop';

# Developed for a Rigol DS1054Z, but may work on other oscilloscopes with a few tweaks
# Channel 1 is assumed to be the light sensor, channel 3 the audio
# Config:
[string] $SCOPE_IP = '192.168.39.24';
[ushort] $SCOPE_PORT = 5555;
[int] $CYCLE_COUNT = 6000; # How many readings to take
[int] $SAMPLE_COUNT = 60000; # How many samples the oscilloscope should capture
[float] $EXPECTED_AUDIO_PEAK = 0.700; # The approximate peak voltage of the sine wave
[float] $EXPECTED_MAX_LATENCY = 0.060; # The max latency we expect to see, used to set the horizontal timebase

[int] $TIMEOUT_MS = 1000;
[int] $WAIT_MS = 5000;
[int] $MAX_BYTES = 100000;
[int] $X_DIVS = 12;
[int] $Y_DIVS = 8;

[byte] $NEWLINE_BYTE = [byte]([char]"`n");

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
        Flush-Instr;
        #Write-Host "$Pos to $EndPos";
        Send-InstrCommand ':WAV:DATA?';
        [List[byte]] $WaveData = Receive-InstrRaw;
        if ($WaveData[0] -NE [byte]([char]'#')) { throw "Expected data first byte to be '#' char, instead got '$([char]$WaveData[0])'"; } # TODO: This sometimes triggers if the light sensor reading is off-scale at the bottom?
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
        Flush-Instr;
        Send-InstrCommand ':TRIG:STAT?';
        if ($(Receive-InstrString) -EQ 'STOP') { return $true; }
    }
    return $false;
}

Send-InstrCommand '*IDN?';
Write-Host "Instrument is: $(Receive-InstrString)";

Stop-Instr;
Start-InstrRun;

Send-InstrCommand ':CHAN1:DISP ON';
Send-InstrCommand ':CHAN2:DISP OFF';
Send-InstrCommand ':CHAN3:DISP ON';
Send-InstrCommand ':CHAN4:DISP OFF';

Send-InstrCommand ':CHAN1:BWLimit 20M';
Send-InstrCommand ':CHAN1:COUP DC';
# It is assumed that the vertical offset and range of the light sensor channel are pre-set manually.
# Can do automatically with:
Send-InstrCommand ':CHAN1:SCAL 1.000';
Send-InstrCommand ':CHAN1:OFFS -7.200';

Send-InstrCommand ':CHAN3:BWLimit 20M';
Send-InstrCommand ':CHAN3:COUP AC';
Send-InstrCommand ':CHAN3:OFFS 0.000';
[float] $AudioVerticalScale = ($EXPECTED_AUDIO_PEAK * 2.2) / $Y_DIVS;
$VerticalScaleVals = 0.010, 0.020, 0.050, 0.100, 0.200, 0.500, 1.0, 2.0, 5.0, 10.0, 20.0, 50.0, 100.0;
foreach ($PossibleValue in $VerticalScaleVals)
{
    if ($AudioVerticalScale -LT $PossibleValue) { $AudioVerticalScale = $PossibleValue; break; }
}
Send-InstrCommand ":CHAN3:SCAL $AudioVerticalScale";

Send-InstrCommand ':TIM:MODE MAIN';
[float] $TimebaseScale = ($EXPECTED_MAX_LATENCY / $X_DIVS);
$TimebaseScaleVals = 0.001, 0.002, 0.005, 0.010, 0.020, 0.050, 0.100, 0.200, 0.500, 1.0;
foreach ($PossibleValue in $TimebaseScaleVals)
{
    if ($TimebaseScale -LT $PossibleValue) { $TimebaseScale = $PossibleValue; break; }
}
Write-Host "Timebase scale $TimebaseScale";
Send-InstrCommand ":TIM:MAIN:SCAL $TimebaseScale";
[float] $TimebaseOffset = $TimebaseScale * $X_DIVS / 2.0 * 0.8;
Send-InstrCommand ":TIM:MAIN:OFFS $TimebaseOffset";

Send-InstrCommand ':TRIG:MODE EDGE';
Send-InstrCommand ':TRIG:SWE SING';
Send-InstrCommand ':TRIG:COUP HFReject';
Send-InstrCommand ':TRIG:EDG:SOUR CHAN3';
Send-InstrCommand ':TRIG:EDG:SLOP POS';
Send-InstrCommand ":TRIG:EDG:LEV $($EXPECTED_AUDIO_PEAK / 3.0)";

Wait-InstrReady | Out-Null;
Set-InstrMemDepth $SAMPLE_COUNT;

[string] $StartTimeFormatted = ([DateTime]::Now.ToString('yyyy-MM-dd_HH-mm-ss'));
[string] $DataPath = Join-Path $PSScriptRoot "Data_$StartTimeFormatted/";
if (!(Test-Path $DataPath)) { New-Item -ItemType 'Container' $DataPath; }
[StreamWriter] $SummaryCSV = [StreamWriter]::new($(Join-Path $DataPath $('Run.csv' -F $Cycle)));
$SummaryCSV.WriteLine('RunID,DetectedLatency');

Write-Host 'Starting in 5 seconds...';
Start-Sleep 5;

try
{
    [Stopwatch] $CycleTimer = [Stopwatch]::StartNew();
    for ($Cycle = 1; $Cycle -LE $CYCLE_COUNT; $Cycle++)
    {
        $CycleTimer.Restart();
        Write-Host -NoNewLine ('Cyc {0:D5} of {1:D5} ' -F $Cycle, $CYCLE_COUNT); 
        Wait-InstrReady | Out-Null;
        Flush-Instr;
        Start-InstrSingle;
        Wait-InstrReady | Out-Null;

        Write-Host -NoNewLine '| Play ';
        [System.Media.SoundPlayer]::new('./sine.wav').PlaySync();

        Write-Host -NoNewLine '| Capture ';
        [bool] $Triggered = Wait-InstrTrigger -Timeout 1000;
        if ($Triggered)
        {
            Write-Host -NoNewLine '| Download ';
            Flush-Instr;
            [List[byte]] $Channel1Data, [PSCustomObject] $Channel1Meta = Receive-InstrWaveformData 1;
            [List[byte]] $Channel3Data, [PSCustomObject] $Channel3Meta = Receive-InstrWaveformData 3;

            Write-Host -NoNewLine '| Save ';
            [StreamWriter] $CSV = [StreamWriter]::new($(Join-Path $DataPath $('{0:D5}.csv' -F $Cycle)));
            $CSV.WriteLine('Time,Audio,Display');

            # NOTE: this assumes light sensor voltage goes up with increased brightness
            [int] $IdleCutoff = $Channel1Data.Count / 20;
            [float] $IdleAudioSum = 0.0;
            [float] $IdleDisplaySum = 0.0;
            [float] $IdleAudioMax = -1000;
            [float] $IdleAudioAvg = -1000;
            [float] $AudioThreshold = -1000;
            [float] $IdleDisplayMax = -1000;
            [float] $IdleDisplayAvg = -1000;
            [float] $DisplayThreshold = -1000;
            [float] $AudioTime = 1000;
            [float] $DisplayTime = 1000;

            for ($Sample = 0; $Sample -LT $Channel1Data.Count; $Sample++)
            {
                [float] $Time = $Channel1Meta.XOrigin + ($Channel1Meta.XIncrement * $Sample);
                [float] $AudioLevel = $Channel3Meta.YIncrement * ($Channel3Data[$Sample] - $Channel3Meta.YOrigin - $Channel3Meta.YReference);
                [float] $DisplayLevel = $Channel1Meta.YIncrement * ($Channel1Data[$Sample] - $Channel1Meta.YOrigin - $Channel1Meta.YReference);
                $CSV.WriteLine([string]::Format('{0},{1},{2}', $Time, $AudioLevel, $DisplayLevel));
                if ($Sample -LT $IdleCutoff)
                {
                    $IdleAudioSum += $AudioLevel;
                    $IdleAudioMax = [MathF]::Max([MathF]::Abs($AudioLevel), $IdleAudioMax);
                    $IdleDisplaySum += $DisplayLevel;
                    $IdleDisplayMax = [MathF]::Max($DisplayLevel, $IdleDisplayMax);
                }
                elseif ($Sample -EQ $IdleCutoff)
                {
                    $IdleAudioAvg = $IdleAudioSum / $IdleCutoff;
                    $IdleDisplayAvg = $IdleDisplaySum / $IdleCutoff;
                    $AudioThreshold = $IdleAudioAvg + (($IdleAudioMax - $IdleAudioAvg) * 5.0);
                    $DisplayThreshold = $IdleDisplayAvg + (($IdleDisplayMax - $IdleDisplayAvg) * 5.0);
                }
                else
                {
                    if ($AudioLevel -GT $AudioThreshold) { $AudioTime = [MathF]::Min($AudioTime, $Time); }
                    if ($DisplayLevel -GT $DisplayThreshold) { $DisplayTime = [MathF]::Min($DisplayTime, $Time); }
                }
            }
            [float] $MeasuredLatency = $DisplayTime - $AudioTime;

            $CSV.WriteLine();
            $CSV.WriteLine('AudioTrig,DisplayTrig,Latency');
            $CSV.WriteLine([string]::Format('{0},{1},{2}', $AudioTime, $DisplayTime, $MeasuredLatency));
            $CSV.Close();
            $SummaryCSV.WriteLine([string]::Format('{0:D5},{1}', $Cycle, $MeasuredLatency));
            Write-Host -NoNewLine ('| Done: {0:F2}ms' -F ($MeasuredLatency * 1000.0));
            Write-Host (', {0:F3}s' -F ($CycleTimer.ElapsedMilliseconds / 1000.0));
        }
        else
        {
            Stop-Instr;
            Write-Host '| FAIL';
            Write-Host 'Did not trigger. Check the trigger settings and connections.';
        }
    }
}
finally { $SummaryCSV.Close(); }
