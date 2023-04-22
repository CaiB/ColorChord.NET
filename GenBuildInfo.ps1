param($ConfigHashFile, $ConfigFile);

Write-Host "Putting default config hash into file '$ConfigHashFile'.";

[string] $Hash = (Get-FileHash $ConfigFile -Algorithm 'MD5').Hash;

$Class = 
@"
namespace ColorChord.NET.Config;
internal static class DefaultConfigInfo
{
    internal const string DefaultConfigFileMD5 = "$Hash";
}
"@

Set-Content -Path $ConfigHashFile -Value $Class;