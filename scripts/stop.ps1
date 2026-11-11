$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$statePath = Join-Path $projectRoot '.run/processes.json'
if (-not (Test-Path -LiteralPath $statePath)) { Write-Host 'No demo process file.'; return }
$state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
foreach ($entry in @(@{ Id = $state.backend; Path = (Join-Path $projectRoot '.tools/dotnet/dotnet.exe') }, @{ Id = $state.livekit; Path = (Join-Path $projectRoot '.tools/livekit/livekit-server.exe') })) {
    $process = Get-Process -Id $entry.Id -ErrorAction SilentlyContinue
    if ($process -and $process.Path -eq $entry.Path) { Stop-Process -Id $process.Id; Write-Host "Stopped demo process $($process.Id)." }
}
