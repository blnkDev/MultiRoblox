# Builds MultiRoblox, runs the self-test and (with -Zip) assembles the release package in dist\.
# Usage:  .\build.ps1        build
#         .\build.ps1 -Zip   build + zip + SHA256 (what the release workflow publishes)
param([switch]$Zip)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$ver = [regex]::Match((Get-Content "$root\src\AssemblyInfo.cs" -Raw), 'AssemblyVersion\("(\d+\.\d+\.\d+)').Groups[1].Value
if (-not $ver) { throw 'Version not found in src\AssemblyInfo.cs' }

$csc = Join-Path $env:windir 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'   # ships with Windows: nothing to install
New-Item -ItemType Directory -Force "$root\dist" | Out-Null
& $csc /nologo /optimize+ /target:winexe `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Net.Http.dll `
    /r:System.Security.dll /r:System.Core.dll /r:System.Management.dll `
    /win32icon:"$root\assets\MultiRoblox.ico" /out:"$root\dist\MultiRoblox.exe" `
    "$root\src\MultiRoblox.cs" "$root\src\AssemblyInfo.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

$t = Start-Process "$root\dist\MultiRoblox.exe" -ArgumentList '--selftest' -Wait -PassThru
if ($t.ExitCode -ne 0) { throw "Self-test failed (exit code $($t.ExitCode))" }

if ($Zip) {
    $pkg = "$root\dist\MultiRoblox-v$ver.zip"   # not $zip: PowerShell variables are case-insensitive and would clash with -Zip
    Compress-Archive -Path "$root\dist\MultiRoblox.exe", "$root\README.md", "$root\LICENSE" -DestinationPath $pkg -Force
    $h = (Get-FileHash $pkg -Algorithm SHA256).Hash.ToLower()
    "$h  MultiRoblox-v$ver.zip" | Set-Content "$root\dist\SHA256SUMS.txt" -Encoding ASCII
}
"OK: MultiRoblox v$ver"
