param([switch]$Install)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$localNode = Join-Path $projectRoot '.tools/node-v22.23.4-win-x64'
if (Test-Path (Join-Path $localNode 'node.exe')) { $env:PATH = "$localNode;$env:PATH" }
if (-not (Get-Command npm.cmd -ErrorAction SilentlyContinue)) {
    throw 'Node.js >=22.12 is required for build only. Install Node or use .tools/node-v22.23.4-win-x64.'
}
Push-Location (Join-Path $projectRoot 'frontend/whiteboard')
try {
    if ($Install) {
        & npm.cmd ci --cache (Join-Path $projectRoot '.tools/npm-cache') --no-fund --no-audit
        if ($LASTEXITCODE -ne 0) { throw 'Whiteboard dependency installation failed.' }
    }
    & npm.cmd run build
    if ($LASTEXITCODE -ne 0) { throw 'Whiteboard build failed.' }
} finally { Pop-Location }
