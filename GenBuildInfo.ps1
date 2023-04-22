param($ConfigHashFile, $ConfigFile);

Write-Host "Putting default config hash into file '$ConfigHashFile'.";

$HashObj = New-Object -TypeName 'System.Security.Cryptography.MD5CryptoServiceProvider';
$FileStream = [System.IO.File]::Open((Resolve-Path $ConfigFile), [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite);
try
{
    [byte[]] $HashBytes = $HashObj.ComputeHash($FileStream);
    $HexBuilder = [System.Text.StringBuilder]::new($HashBytes.Length * 2);
    foreach($b in $HashBytes) { $HexBuilder.AppendFormat("{0:X2}", $b); }
    [string] $Hash = $HexBuilder.ToString();
}
finally { $FileStream.Dispose(); }

$Class = 
@"
namespace ColorChord.NET.Config;
internal static class DefaultConfigInfo
{
    internal const string DefaultConfigFileMD5 = "$Hash";
}
"@

Set-Content -Path $ConfigHashFile -Value $Class;