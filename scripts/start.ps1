$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $projectRoot
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools/cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
if (-not (Test-Path '.tools/livekit/livekit-server.exe') -or -not (Test-Path '.tools/dotnet/dotnet.exe')) { throw 'Run scripts/setup.ps1 first.' }
if (-not (Test-Path 'src/Lms.Poc/wwwroot/vendor/whiteboard/classroom.js')) { throw 'Run scripts/setup-whiteboard.ps1 to build the whiteboard assets first.' }
if ((Test-NetConnection 127.0.0.1 -Port 5080 -WarningAction SilentlyContinue -InformationLevel Quiet) -or (Test-NetConnection 127.0.0.1 -Port 7880 -WarningAction SilentlyContinue -InformationLevel Quiet)) {
    $stateFile = Join-Path $projectRoot '.run/processes.json'
    if (Test-Path -LiteralPath $stateFile) {
        $state = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
        $existingApi = Get-Process -Id $state.backend -ErrorAction SilentlyContinue
        $existingLiveKit = Get-Process -Id $state.livekit -ErrorAction SilentlyContinue
        if ($existingApi -and $existingLiveKit -and $existingApi.Path -eq (Join-Path $projectRoot '.tools/dotnet/dotnet.exe') -and $existingLiveKit.Path -eq (Join-Path $projectRoot '.tools/livekit/livekit-server.exe')) {
            Write-Host 'Demo is already running: http://localhost:5080' -ForegroundColor Green
            return
        }
    }
    throw 'Port 5080 or 7880 is already in use. If this demo is running, visit http://localhost:5080. Otherwise stop the conflicting service.'
}
& '.tools/dotnet/dotnet.exe' build 'src/Lms.Poc' --nologo --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Build failed; run setup.ps1.' }
New-Item -ItemType Directory -Force '.run' | Out-Null
$livekit = $null
try {
    $livekit = Start-Process -FilePath (Join-Path $projectRoot '.tools/livekit/livekit-server.exe') -ArgumentList '--dev','--bind','127.0.0.1','--node-ip','127.0.0.1' -WorkingDirectory $projectRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput '.run/livekit.out.log' -RedirectStandardError '.run/livekit.err.log'
    $dll = Join-Path $projectRoot 'src/Lms.Poc/bin/Debug/net10.0/Lms.Poc.dll'
    $backend = Start-Process -FilePath (Join-Path $projectRoot '.tools/dotnet/dotnet.exe') -ArgumentList "`"$dll`"",'--urls','http://localhost:5080' -WorkingDirectory (Join-Path $projectRoot 'src/Lms.Poc') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $projectRoot '.run/backend.out.log') -RedirectStandardError (Join-Path $projectRoot '.run/backend.err.log')
    @{ livekit = $livekit.Id; backend = $backend.Id } | ConvertTo-Json | Set-Content '.run/processes.json'
    $ready = $false
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        Start-Sleep -Milliseconds 500
        if ($backend.HasExited -or $livekit.HasExited) { throw 'A server exited. Check .run/*.log.' }
        try { $health = Invoke-RestMethod 'http://localhost:5080/api/health'; if ($health.livekit) { $ready = $true; break } } catch { }
    }
    if (-not $ready) { throw 'Servers did not become ready. Check .run/*.log.' }
    Write-Host 'Demo ready: http://localhost:5080' -ForegroundColor Green
    Write-Host 'Presenter code: demo123 | Open another browser window as Participant.'
    Write-Host 'Stop: powershell -ExecutionPolicy Bypass -File scripts/stop.ps1'
} catch {
    if ($livekit -and -not $livekit.HasExited) { Stop-Process -Id $livekit.Id }
    if ($backend -and -not $backend.HasExited) { Stop-Process -Id $backend.Id }
    throw
}
