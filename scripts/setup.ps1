$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $projectRoot
New-Item -ItemType Directory -Force '.tools', 'src/Lms.Poc/wwwroot/vendor' | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools/cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
if (-not (Test-Path '.tools/dotnet/dotnet.exe')) {
    Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile '.tools/dotnet-install.ps1'
    & powershell -NoProfile -ExecutionPolicy Bypass -File '.tools/dotnet-install.ps1' -Version '10.0.404' -InstallDir (Join-Path $projectRoot '.tools/dotnet') -NoPath
    if ($LASTEXITCODE -ne 0) { throw 'SDK installation failed.' }
}
if (-not (Test-Path '.tools/livekit/livekit-server.exe')) {
    $release = 'https://github.com/livekit/livekit/releases/download/v1.13.11'
    Invoke-WebRequest "$release/livekit_1.13.11_windows_amd64.zip" -OutFile '.tools/livekit.zip'
    $expectedHash = '5d071ea43339f0140d24a66f0b2ff8a7bf6c4d0dceb19b30abca835670213667'
    if ((Get-FileHash '.tools/livekit.zip' -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedHash) { throw 'LiveKit checksum mismatch.' }
    Expand-Archive -LiteralPath '.tools/livekit.zip' -DestinationPath '.tools/livekit' -Force
}
$vendor = 'src/Lms.Poc/wwwroot/vendor'
if (-not (Test-Path "$vendor/livekit-client.umd.min.js")) {
    Invoke-WebRequest 'https://cdn.jsdelivr.net/npm/livekit-client@2.17.2/dist/livekit-client.umd.min.js' -OutFile "$vendor/livekit-client.umd.min.js"
    Invoke-WebRequest 'https://cdn.jsdelivr.net/npm/livekit-client@2.17.2/LICENSE' -OutFile "$vendor/LiveKit-LICENSE.txt"
}
if (-not (Test-Path "$vendor/Vazirmatn.woff2")) {
    Invoke-WebRequest 'https://cdn.jsdelivr.net/gh/rastikerdar/vazirmatn@v33.003/fonts/webfonts/Vazirmatn%5Bwght%5D.woff2' -OutFile "$vendor/Vazirmatn.woff2"
    Invoke-WebRequest 'https://cdn.jsdelivr.net/gh/rastikerdar/vazirmatn@v33.003/OFL.txt' -OutFile "$vendor/Vazirmatn-OFL.txt"
}
& (Join-Path $PSScriptRoot 'setup-whiteboard.ps1')
& '.tools/dotnet/dotnet.exe' build 'src/Lms.Poc' --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Host 'Ready. Run: powershell -ExecutionPolicy Bypass -File scripts/start.ps1' -ForegroundColor Green
