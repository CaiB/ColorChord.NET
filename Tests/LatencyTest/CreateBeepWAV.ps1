using namespace System.IO;

[string] $FILE_NAME = 'sine.wav';
[float] $FREQUENCY = 1760.0;
[uint] $SAMPLE_RATE = 48000;
[float] $LENGTH_SEC = 0.3;
[float] $AMPLITUDE = 0.7;

[int] $LengthSamples = [MathF]::Round($SAMPLE_RATE * $LENGTH_SEC);
[short[]] $SoundData = [short[]]::new($LengthSamples);
for ($i = 0; $i -LT $LengthSamples; $i++)
{
    [float] $Sample = $AMPLITUDE * [MathF]::Sin([MathF]::Pi * 2.0 * $FREQUENCY * $i / $SAMPLE_RATE);
    $SoundData[$i] = [short][MathF]::Round(32767 * $Sample);
}

[FileStream] $File = [FileStream]::new((Join-Path $PSScriptRoot $FILE_NAME), [FileMode]::Create, [FileAccess]::Write, [FileShare]::ReadWrite -BOR [FileShare]::Delete);
[uint] $DataSize = $LengthSamples * 2;
[uint] $TopChunkSize = $DataSize + 36;
$File.Write(@(0x52,0x49,0x46,0x46), 0, 4); # "RIFF"
$File.WriteByte(($TopChunkSize) -BAND 0xFF);
$File.WriteByte(($TopChunkSize -SHR 8) -BAND 0xFF);
$File.WriteByte(($TopChunkSize -SHR 16) -BAND 0xFF);
$File.WriteByte(($TopChunkSize -SHR 24) -BAND 0xFF);
$File.Write(@(0x57,0x41,0x56,0x45), 0, 4); # "WAVE"
$File.Write(@(0x66,0x6D,0x74,0x20,0x10,0x00,0x00,0x00,0x01,0x00,0x01,0x00), 0, 12); # "fmt "[ChunkLen][PCM][1ch]
$File.WriteByte(($SAMPLE_RATE) -BAND 0xFF);
$File.WriteByte(($SAMPLE_RATE -SHR 8) -BAND 0xFF);
$File.WriteByte(($SAMPLE_RATE -SHR 16) -BAND 0xFF);
$File.WriteByte(($SAMPLE_RATE -SHR 24) -BAND 0xFF);
[uint] $BytesPerSec = $SAMPLE_RATE * 2;
$File.WriteByte(($BytesPerSec) -BAND 0xFF);
$File.WriteByte(($BytesPerSec -SHR 8) -BAND 0xFF);
$File.WriteByte(($BytesPerSec -SHR 16) -BAND 0xFF);
$File.WriteByte(($BytesPerSec -SHR 24) -BAND 0xFF);
$File.Write(@(0x02,0x00,0x10,0x00,0x64,0x61,0x74,0x61), 0, 8); # [FrameSize][BitDepth]"data"
$File.WriteByte(($DataSize) -BAND 0xFF);
$File.WriteByte(($DataSize -SHR 8) -BAND 0xFF);
$File.WriteByte(($DataSize -SHR 16) -BAND 0xFF);
$File.WriteByte(($DataSize -SHR 24) -BAND 0xFF);

for ($i = 0; $i -LT $LengthSamples; $i++)
{
    $File.WriteByte(($SoundData[$i]) -BAND 0xFF);
    $File.WriteByte(($SoundData[$i] -SHR 8) -BAND 0xFF);
}

$File.Close();
