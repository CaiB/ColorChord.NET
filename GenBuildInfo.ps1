param($ConfigHashFile, $ConfigFile);

Write-Host "Putting default config hash into file '$ConfigHashFile'.";

[string] $Hash = (Get-FileHash $ConfigFile -Algorithm 'MD5').Hash;

$HashObj = New-Object -TypeName 'System.Security.Cryptography.MD5CryptoServiceProvider'
$FileStream = [System.IO.File]::Open((Resolve-Path $ConfigFile), [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
try { [System.BitConverter]::ToString($HashObj.ComputeHash($FileStream)) }
finally { $FileStream.Dispose() }

$Class = 
@"
namespace ColorChord.NET.Config;
internal static class DefaultConfigInfo
{
    internal const string DefaultConfigFileMD5 = "$Hash";
}
"@

Set-Content -Path $ConfigHashFile -Value $Class;