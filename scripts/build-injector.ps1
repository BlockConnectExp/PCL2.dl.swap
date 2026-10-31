<#
    Builds the injector package that ships with every release.

    Inputs:
      tools\Pcl2Swap.Injector              WinUI 3, x64, self-contained
      dist\exloader\Pcl2Swap.Shim.x64.*    produced by src\Pcl2Swap.Shim\build.ps1

    Outputs:
      dist\injector\                                  publish output + shim seed
      dist\PCL2.dl.swap-Injector-<ver>.zip            the release asset

    Usage:
      .\scripts\build-injector.ps1 -ToolVersion "v26.3-Alpha3"
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$ToolVersion = 'v26.3-Alpha3',
    [string]$DistDir = 'dist',
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'tools\Pcl2Swap.Injector\Pcl2Swap.Injector.csproj'
$dist = if ([System.IO.Path]::IsPathRooted($DistDir)) { $DistDir } else { Join-Path $root $DistDir }
$out = Join-Path $dist 'injector'

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $out -Force | Out-Null

Write-Host "[injector] publishing $project"
$publishArgs = @(
    'publish', $project, '-c', $Configuration,
    '-p:Platform=x64', '-p:RuntimeIdentifier=win-x64',
    '-o', $out, '--nologo'
)
if ($NoRestore) { $publishArgs += '--no-restore' }
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw 'injector publish failed' }

# Ship the same shim pair the ExLoader card uses, so the injector works offline.
$shimSrc = Join-Path $dist 'exloader'
$shimOut = Join-Path $out 'shim'
New-Item -ItemType Directory -Path $shimOut -Force | Out-Null
foreach ($name in 'Pcl2Swap.Shim.x64.dll', 'Pcl2Swap.Shim.x64.dat') {
    $source = Join-Path $shimSrc $name
    if (-not (Test-Path $source)) { throw "missing shim piece: $source" }
    Copy-Item $source $shimOut -Force
}

$compact = $ToolVersion -replace '\s', ''
Set-Content (Join-Path $shimOut 'version.txt') $compact -NoNewline -Encoding ascii

$dataOut = Join-Path $out 'data'
New-Item -ItemType Directory -Path $dataOut -Force | Out-Null
Copy-Item (Join-Path $root 'data\offset-manifest.json') $dataOut -Force

$zip = Join-Path $dist ("PCL2.dl.swap-Injector-$compact.zip")
if (Test-Path $zip) { Remove-Item $zip -Force }
Write-Host "[injector] compressing $zip"
Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip -CompressionLevel Optimal

$size = (Get-Item $zip).Length / 1MB
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host ("[injector] artifact = {0} ({1:N1} MB)" -f (Split-Path -Leaf $zip), $size)
Write-Host "[injector] sha256   = $hash"
