<#
    Signs data/addons-index.json with the repository index key.

    The signature covers the exact bytes of the JSON file and is written next to it as
    "<index>.sig". Payloads trust "data/index-key.pub.xml" (embedded) plus any key in
    <data root>\trusted-keys.

    Usage:
      .\scripts\sign-index.ps1 [-Index data\addons-index.json] [-PrivateKey keys\index-signing.key.xml]
#>
[CmdletBinding()]
param(
    [string]$Index = 'data\addons-index.json',
    [string]$PrivateKey = 'keys\index-signing.key.xml'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$indexPath = if ([IO.Path]::IsPathRooted($Index)) { $Index } else { Join-Path $root $Index }
$keyPath = if ([IO.Path]::IsPathRooted($PrivateKey)) { $PrivateKey } else { Join-Path $root $PrivateKey }

if (-not (Test-Path $indexPath)) { throw "index not found: $indexPath" }
if (-not (Test-Path $keyPath)) { throw "private key not found: $keyPath" }

$rsa = [Security.Cryptography.RSA]::Create()
$rsa.FromXmlString([IO.File]::ReadAllText($keyPath))
$bytes = [IO.File]::ReadAllBytes($indexPath)
$signature = $rsa.SignData($bytes, [Security.Cryptography.HashAlgorithmName]::SHA256,
    [Security.Cryptography.RSASignaturePadding]::Pkcs1)

$publicXml = $rsa.ToXmlString($false)
$sha = [Security.Cryptography.SHA256]::Create()
$hash = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($publicXml))
$keyId = ($hash[0..7] | ForEach-Object { $_.ToString('x2') }) -join ''

$document = [ordered]@{
    algorithm = 'RSA-SHA256'
    keyId     = $keyId
    signature = [Convert]::ToBase64String($signature)
}
$sigPath = $indexPath + '.sig'
[IO.File]::WriteAllText($sigPath, ($document | ConvertTo-Json -Compress),
    [Text.UTF8Encoding]::new($false))

"signed: $indexPath"
"sig:    $sigPath"
"keyId:  $keyId"
