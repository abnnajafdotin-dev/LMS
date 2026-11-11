$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$version = '22.23.4'
$archiveName = "node-v$version-win-x64.zip"
$archivePath = Join-Path $projectRoot ".tools/$archiveName"
$nodePath = Join-Path $projectRoot ".tools/node-v$version-win-x64/node.exe"
if (-not (Test-Path -LiteralPath $nodePath)) {
    New-Item -ItemType Directory -Force (Join-Path $projectRoot '.tools') | Out-Null
    Invoke-WebRequest "https://nodejs.org/dist/v$version/$archiveName" -OutFile $archivePath
    $expectedHash = 'cbc74b5e0982c9dbb41525fd8fb5dcd32fbee237863f7829072f4639a1ce5f20'
    if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedHash) {
        throw 'Node archive checksum mismatch.'
    }
    Expand-Archive -LiteralPath $archivePath -DestinationPath (Join-Path $projectRoot '.tools') -Force
}
& (Join-Path $PSScriptRoot 'build-whiteboard.ps1') -Install
Write-Host 'Preview: http://localhost:5080/vendor/whiteboard/index.html (start the demo first).'
