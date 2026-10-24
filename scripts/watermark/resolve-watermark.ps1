<#
    Resolves the canonical BlockConnect watermark for this repository.

    Canonical form:  GitHub@{repo-owner} | {organization-display-name}@{gh-login}
    Personal repos:  GitHub@{repo-owner}

    The organization display name comes from .blockconnect-watermark.json when present,
    otherwise from the GitHub organization record, otherwise it falls back to the owner login.
#>
[CmdletBinding()]
param(
    [switch]$Canonical
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

$remote = (git -C $root remote get-url origin 2>$null)
if (-not $remote) { throw 'origin remote not found' }

$owner = if ($remote -match 'github\.com[:/](?<owner>[^/]+)/(?<repo>[^/.]+)') { $Matches.owner } else { throw "cannot parse owner from $remote" }

$login = (gh api user --jq '.login' 2>$null)
if (-not $login) { throw 'gh is not authenticated' }

$override = Join-Path $root '.blockconnect-watermark.json'
$display = $null
if (Test-Path $override) {
    $display = (Get-Content $override -Raw | ConvertFrom-Json).organizationDisplayName
}
if (-not $display) {
    $display = (gh api "orgs/$owner" --jq '.name' 2>$null)
}
if (-not $display) { $display = $owner }

$isOrganization = $true
try { gh api "orgs/$owner" --jq '.login' | Out-Null } catch { $isOrganization = $false }

$watermark = if ($isOrganization) { "GitHub@$owner | $display@$login" } else { "GitHub@$owner" }

if ($Canonical) { $watermark } else { "<!-- $watermark -->" }

# GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover