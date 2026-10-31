<#
    Builds the demo addon and packs it into demo.sample.swapaddon.

    Usage:
      .\build.ps1 [-Version 1.0.0] [-ApiPath <path to PCL2Swap.Api.dll>]
#>
[CmdletBinding()]
param(
    [string]$Version = '1.0.0',
    [string]$ApiPath = ''
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$root = Split-Path -Parent (Split-Path -Parent $here)
$packer = Join-Path $root 'tools\PclAddonPack\bin\Release\net48\PclAddonPack.exe'
if (-not (Test-Path $packer)) {
    & dotnet build (Join-Path $root 'tools\PclAddonPack\PclAddonPack.csproj') -c Release --nologo | Out-Host
}

$buildArgs = @('build', (Join-Path $here 'DemoAddon.csproj'), '-c', 'Release', '--nologo')
if ($ApiPath) { $buildArgs += "-p:ApiPath=$ApiPath" }
& dotnet @buildArgs | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'demo addon build failed' }

$outDir = Join-Path $here 'bin\Release\net48'
$out = Join-Path $here 'demo.sample.swapaddon'
$packArgs = @(
    '--project', $outDir,
    '--id', 'demo.sample',
    '--name', 'Demo Addon',
    '--version', $Version,
    '--entry', 'DemoAddon.DemoAddon',
    '--description', 'PCL2 Swap demo addon: settings page, More page, widgets and persisted data.',
    '--permissions', 'pages,files,clipboard,messaging',
    '--scope', 'demo.core;Core features;Page body and module messaging;true;Core features;Page body and module messaging',
    '--scope', 'demo.advanced;Advanced tools;Scope API, data table, empty state and code-block examples;false;Advanced tools;Scope API, data table, empty state and code-block examples',
    '--out', $out
)
& $packer @packArgs
if ($LASTEXITCODE -ne 0) { throw 'pack failed' }
"packed: $out"
