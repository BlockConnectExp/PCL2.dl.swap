<#
    Assembles dist\ from source.

    Produces:
      dist\PclDlSwap.Loader.exe          patch + launch entry point
      dist\PclDlSwap.Patcher.dll         the patch engine
      dist\PclDlSwap.Payload.dll         sandbox scanner / watchdog
      dist\pcl2swap-<pcl2>-<ver>.dll     the patch engine under its release name
      dist\PclDlSwap.Profiler.dll        optional native CLR profiler
      dist\data\offset-manifest.json

    Usage:
      .\scripts\build.ps1 [-Configuration Release] [-SkipProfiler] [-PayloadVersion "26.0.0.0"] [-TargetPcl "2.99.0"] [-ToolVersion "v26.3-Alpha2"]
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [switch]$SkipProfiler,
    [string]$TargetPcl = '2.99.0',
    [string]$ToolVersion = 'v26.3-Alpha2',
    # Non-default output roots let two agent sessions build in parallel without fighting over
    # the shared dist folder (a running injected PCL locks its shim copy).
    [string]$DistDir = 'dist'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dist = if ([System.IO.Path]::IsPathRooted($DistDir)) { $DistDir } else { Join-Path $root $DistDir }

Write-Host "[build] root      = $root"
Write-Host "[build] dist      = $dist"

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Path $dist, (Join-Path $dist 'data') -Force | Out-Null

function Build-Project {
    param([string]$RelativePath)
    $project = Join-Path $root $RelativePath
    Write-Host "[build] $RelativePath"
    & dotnet build $project -c $Configuration -v quiet --nologo | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "build failed: $RelativePath" }
}

Build-Project 'src\PclDlSwap.Payload\PclDlSwap.Payload.csproj'
Build-Project 'src\PclDlSwap.Patcher\PclDlSwap.Patcher.csproj'
Build-Project 'src\PclDlSwap.Loader\PclDlSwap.Loader.csproj'
Build-Project 'src\PclDlSwap.HookFixture\PclDlSwap.HookFixture.csproj'
Build-Project 'tools\VerifyOffsets\VerifyOffsets.csproj'

$loaderOut = Join-Path $root "src\PclDlSwap.Loader\bin\$Configuration"
Copy-Item (Join-Path $loaderOut '*.dll')      $dist -Force
Copy-Item (Join-Path $loaderOut '*.exe')      $dist -Force
Copy-Item (Join-Path $loaderOut '*.config')   $dist -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $root 'data\offset-manifest.json') (Join-Path $dist 'data') -Force

# Release-name copy of the patch engine.
$releaseName = "pcl2swap-$TargetPcl-$ToolVersion.dll"
Copy-Item (Join-Path $dist 'PclDlSwap.Patcher.dll') (Join-Path $dist $releaseName) -Force
Write-Host "[build] artifact  = $releaseName"

if (-not $SkipProfiler) {
    $profilerBuild = Join-Path $root 'src\PclDlSwap.Profiler\build.cmd'
    if (Test-Path $profilerBuild) {
        Write-Host '[build] native profiler'
        $previous = $env:DIST_OVERRIDE
        & cmd /c "`"$profilerBuild`""
        if ($LASTEXITCODE -ne 0) { Write-Warning '[build] profiler build failed (optional component)' }
    }
}

Write-Host '[build] verifying offset manifest'
& dotnet run -c $Configuration --project (Join-Path $root 'tools\VerifyOffsets') -- `
    (Join-Path $root 'data\offset-manifest.json') `
    "C:\Users\Sails\Documents\Workspace\02-Research\_scratch\pcl2-dlswap\samples\v299\Plain Craft Launcher 2.exe" 2>&1 |
    Select-Object -Last 6 | Out-Host

Write-Host '[build] hashes'
Get-ChildItem $dist -Recurse -File | ForEach-Object {
    $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "{0}  {1}  {2}" -f $hash, $_.Length, $_.FullName.Substring($dist.Length + 1)
} | Out-Host

Write-Host '[build] done'

# GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
