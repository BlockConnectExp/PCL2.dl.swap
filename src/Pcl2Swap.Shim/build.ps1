<#
    Builds the ExLoader-facing shim: a native x64 DLL that hosts the CLR payload inside PCL.

    Embedded RCDATA resources:
      101  PclDlSwap.Payload.dll        managed payload
      102  Gee.External.Capstone.dll    managed Capstone wrapper
      103  capstone.dll                 native Capstone (win-x64)
      104  MonoMod.Utils.dll            IL-copy engine
      105  Mono.Cecil.dll               IL reading/writing
      106  PCL2Swap.Api.dll             addon SDK surface

    Outputs (under dist\exloader\):
      Pcl2Swap.Shim.x64.dll   upload this file to the ExLoader injector card
      Pcl2Swap.Shim.x64.dat   decimal byte dump matching ExLoader's userFiles encoding

    Usage:
      .\src\Pcl2Swap.Shim\build.ps1 [-Configuration Release] [-SkipPayloadBuild]
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [switch]$SkipPayloadBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$shimDir = $PSScriptRoot
$workDir = Join-Path $shimDir 'obj'
$outDir = Join-Path $root 'dist\exloader'

$payloadDll = Join-Path $root "src\PclDlSwap.Payload\bin\$Configuration\PclDlSwap.Payload.dll"
$wrapperDll = Join-Path $root "src\PclDlSwap.Payload\bin\$Configuration\Gee.External.Capstone.dll"
$capstoneX64 = Join-Path $env:USERPROFILE ".nuget\packages\gee.external.capstone\2.3.0\runtimes\win-x64\native\capstone.dll"
$monoModDll = Join-Path $root "src\PclDlSwap.Payload\bin\$Configuration\MonoMod.Utils.dll"
$cecilDll = Join-Path $root "src\PclDlSwap.Payload\bin\$Configuration\Mono.Cecil.dll"
$apiDll = Join-Path $root "src\PclDlSwap.Payload\bin\$Configuration\PCL2Swap.Api.dll"

if (-not $SkipPayloadBuild) {
    Write-Host "[shim] building payload ($Configuration)"
    & dotnet build (Join-Path $root 'src\PclDlSwap.Payload\PclDlSwap.Payload.csproj') -c $Configuration -v quiet --nologo | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'payload build failed' }
}

foreach ($input in @($payloadDll, $wrapperDll, $capstoneX64, $monoModDll, $cecilDll, $apiDll)) {
    if (-not (Test-Path -LiteralPath $input)) { throw "missing input: $input" }
}

# The embedded disassembler must match the 64-bit host process.
$capstoneBytes = [IO.File]::ReadAllBytes($capstoneX64)
$capstonePe = [BitConverter]::ToInt32($capstoneBytes, 0x3C)
$capstoneMachine = [BitConverter]::ToUInt16($capstoneBytes, $capstonePe + 4)
if ($capstoneMachine -ne 0x8664) {
    throw ("capstone.dll is not x64 (machine=0x{0:X4}): {1}" -f $capstoneMachine, $capstoneX64)
}

New-Item -ItemType Directory -Path $workDir, $outDir -Force | Out-Null

# The resource compiler treats backslashes as escapes, so feed it forward slashes.
$rc = @"
101 RCDATA "$($payloadDll -replace '\\', '/')"
102 RCDATA "$($wrapperDll -replace '\\', '/')"
103 RCDATA "$($capstoneX64 -replace '\\', '/')"
104 RCDATA "$($monoModDll -replace '\\', '/')"
105 RCDATA "$($cecilDll -replace '\\', '/')"
106 RCDATA "$($apiDll -replace '\\', '/')"
"@
Set-Content -LiteralPath (Join-Path $workDir 'Pcl2Swap.Shim.rc') -Value $rc -Encoding ASCII

$vcvars = 'C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\VC\Auxiliary\Build\vcvars64.bat'
if (-not (Test-Path -LiteralPath $vcvars)) {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    $vcvars = $null
    if (Test-Path -LiteralPath $vswhere) {
        $vsPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
        if ($vsPath) { $vcvars = Join-Path $vsPath 'VC\Auxiliary\Build\vcvars64.bat' }
    }
}
if (-not $vcvars -or -not (Test-Path -LiteralPath $vcvars)) {
    throw 'vcvars64.bat not found; install VS Build Tools with the C++ workload'
}

$shimCpp = Join-Path $shimDir 'Shim.cpp'
$cmd = @"
@echo off
call "$vcvars" >nul || exit /b 1
cd /d "$workDir" || exit /b 1
rc /nologo /fo "Pcl2Swap.Shim.res" "Pcl2Swap.Shim.rc" || exit /b 1
cl /nologo /c /O2 /GS- /Gs9999999 /utf-8 /W3 /DUNICODE /D_UNICODE "$shimCpp" /Fo:"Pcl2Swap.Shim.obj" || exit /b 1
link /nologo /DLL /NODEFAULTLIB /ENTRY:DllMain /MACHINE:X64 /INCREMENTAL:NO /OPT:REF /OPT:ICF "Pcl2Swap.Shim.obj" "Pcl2Swap.Shim.res" kernel32.lib user32.lib /OUT:"Pcl2Swap.Shim.x64.dll" || exit /b 1
dumpbin /nologo /exports "Pcl2Swap.Shim.x64.dll" > "exports.txt" || exit /b 1
dumpbin /nologo /headers "Pcl2Swap.Shim.x64.dll" > "headers.txt" || exit /b 1
exit /b 0
"@
$cmdPath = Join-Path $workDir 'build-shim.cmd'
Set-Content -LiteralPath $cmdPath -Value $cmd -Encoding ASCII

Write-Host "[shim] compiling $shimCpp"
& cmd /c "`"$cmdPath`""
if ($LASTEXITCODE -ne 0) { throw 'native shim build failed' }

$built = Join-Path $workDir 'Pcl2Swap.Shim.x64.dll'
$exports = Get-Content -LiteralPath (Join-Path $workDir 'exports.txt') -Raw
foreach ($symbol in @('Pcl2SwapStart', 'Pcl2SwapInit')) {
    if ($exports -notmatch [regex]::Escape($symbol)) { throw "export missing: $symbol" }
}

$headers = Get-Content -LiteralPath (Join-Path $workDir 'headers.txt') -Raw
if ($headers -notmatch 'machine \(x64\)') { throw 'built DLL is not x64' }

Copy-Item -LiteralPath $built -Destination (Join-Path $outDir 'Pcl2Swap.Shim.x64.dll') -Force

# ExLoader stores uploaded modules as the decimal byte sequence of the DLL.
$dllBytes = [IO.File]::ReadAllBytes($built)
[IO.File]::WriteAllText((Join-Path $outDir 'Pcl2Swap.Shim.x64.dat'), ($dllBytes -join ','), [Text.Encoding]::ASCII)

Write-Host '[shim] done'
Get-ChildItem $outDir -File | ForEach-Object {
    $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "{0}  {1}  {2}" -f $hash, $_.Length, $_.Name
} | Out-Host

# GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
