<#
    Audits the English dictionary against Chinese user strings in an official PCL build.

    Usage:
      .\scripts\i18n-audit.ps1 -PclExe "C:\path\Plain Craft Launcher 2.exe"
      .\scripts\i18n-audit.ps1 -PclExe "..." -Output "$env:TEMP\pcl-i18n-missing.tsv"

    The audit is intentionally exact-string based. Dynamic log rows are expected to miss here;
    PhraseEnglish handles those separately. Page classes (PCL.Page*, FormMain) are reported
    independently so a headline "coverage" number cannot hide the log/exception string volume.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PclExe,

    [string]$Output = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$ilspy = Get-Command ilspycmd -ErrorAction Stop
$PclExe = [IO.Path]::GetFullPath($PclExe)
if (-not (Test-Path -LiteralPath $PclExe)) {
    throw "PCL executable not found: $PclExe"
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('pcl2swap-i18n-audit-' + [Guid]::NewGuid().ToString('N'))
$ilPath = Join-Path $tempRoot 'pcl.il'
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
try {
    & $ilspy.Source -il $PclExe | Set-Content -LiteralPath $ilPath -Encoding UTF8
    if ($LASTEXITCODE -ne 0) { throw "ilspycmd failed with exit code $LASTEXITCODE" }

    $translationFiles = @(
        (Join-Path $root 'src\PclDlSwap.Payload\Swap\I18n.cs'),
        (Join-Path $root 'src\PclDlSwap.Payload\Swap\I18n.Extra.cs')
    )
    $exactKeys = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $phraseKeys = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($file in $translationFiles) {
        $dictionary = ''
        foreach ($line in [IO.File]::ReadLines($file)) {
            if ($line -match 'private\s+static\s+readonly\s+Dictionary<string,\s*string>\s+(\w+)') {
                $dictionary = $matches[1]
                continue
            }
            if ($dictionary -and $line -match '^\s*\};') {
                $dictionary = ''
                continue
            }
            if ($dictionary -and
                $line -match '^\s*\{\s*"((?:\\.|[^"\\])*)"\s*,') {
                $key = $matches[1].Replace('\"', '"').Replace('\\', '\')
                if ($dictionary -match 'Phrase') {
                    [void]$phraseKeys.Add($key)
                } else {
                    [void]$exactKeys.Add($key)
                }
            }
        }
    }
    $sharedKeys = @($phraseKeys | Where-Object { $exactKeys.Contains($_) }).Count
    $distinctKeys = $exactKeys.Count + $phraseKeys.Count - $sharedKeys

    # Track the full type stack so nested page helpers inherit the nearest PCL.Page owner.
    $classStack = New-Object 'System.Collections.Generic.List[string]'
    $pendingClass = $null
    $byType = @{}
    foreach ($line in [IO.File]::ReadLines($ilPath)) {
        if ($line -match '^\s*\.class\s+(.+)$') {
            $parts = $matches[1].Trim() -split '\s+'
            $pendingClass = $parts[$parts.Count - 1]
        }

        if ($line -match 'ldstr\s+"((?:\\.|[^"\\])*)"') {
            $raw = $matches[1]
            if ($raw -match '[\u3400-\u9FFF]' -and $raw.Length -ge 2) {
                $page = $classStack | Where-Object { $_ -match '^PCL\.(Page|FormMain)' } |
                    Select-Object -Last 1
                if ($page) {
                    if (-not $byType.ContainsKey($page)) {
                        $byType[$page] = New-Object 'System.Collections.Generic.HashSet[string]' `
                            ([StringComparer]::Ordinal)
                    }
                    [void]$byType[$page].Add($raw)
                }
            }
        }

        if ($pendingClass -and $line -match '\{') {
            $classStack.Add($pendingClass)
            $pendingClass = $null
        }
        if ($line -match '// end of class ' -and $classStack.Count -gt 0) {
            $classStack.RemoveAt($classStack.Count - 1)
        }
    }

    $all = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($set in $byType.Values) {
        foreach ($text in $set) { [void]$all.Add($text) }
    }

    $missing = New-Object 'System.Collections.Generic.List[string]'
    foreach ($type in $byType.Keys | Sort-Object) {
        foreach ($text in $byType[$type] | Sort-Object) {
            if (-not $exactKeys.Contains($text)) {
                $clean = $text.Replace("`t", ' ').Replace("`r", '\r').Replace("`n", '\n')
                $missing.Add("$type`t$clean")
            }
        }
    }

    $covered = $all.Count - $missing.Count
    $coverage = if ($all.Count -eq 0) { 0 } else { [Math]::Round(100.0 * $covered / $all.Count, 2) }
    "PCL: $PclExe"
    "Exact keys: $($exactKeys.Count)"
    "Phrase keys: $($phraseKeys.Count)"
    "Shared exact/phrase keys: $sharedKeys"
    "Distinct keys: $distinctKeys"
    "Page types: $($byType.Count)"
    "Page Chinese strings: $($all.Count)"
    "Exact hits: $covered"
    "Missing: $($missing.Count)"
    "Exact page coverage: $coverage%"

    if ($Output) {
        $outputPath = [IO.Path]::GetFullPath($Output)
        $parent = Split-Path -Parent $outputPath
        if ($parent) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
        "type`tchinese" | Set-Content -LiteralPath $outputPath -Encoding UTF8
        $missing | Add-Content -LiteralPath $outputPath -Encoding UTF8
        "Missing table: $outputPath"
    } else {
        'Top missing strings:'
        $missing | Select-Object -First 40 | ForEach-Object { '  ' + $_ }
    }
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        $resolved = [IO.Path]::GetFullPath($tempRoot)
        $tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
        if ($resolved.StartsWith($tempBase + '\', [StringComparison]::OrdinalIgnoreCase)) {
            [IO.Directory]::Delete($resolved, $true)
        }
    }
}


# GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
