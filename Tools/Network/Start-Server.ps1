param(
    [ValidateRange(1,65535)][int]$Port = 7777,
    [string]$ListenAddress = '0.0.0.0',
    [ValidateRange(1,65535)][int]$DirectoryPort = 7778
)
$ErrorActionPreference = 'Stop'
if ($Port -eq $DirectoryPort) { throw 'Game port and directory port must be different.' }
$parsedAddress = $null
if (-not [System.Net.IPAddress]::TryParse($ListenAddress, [ref]$parsedAddress) -or
    $parsedAddress.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) {
    throw 'ListenAddress must be an IPv4 address.'
}
$serverExe = Join-Path $PSScriptRoot 'ReEscapeServer.exe'
if (-not (Test-Path -LiteralPath $serverExe)) { throw "Missing server build: $serverExe" }
$existingEndpoint = Get-NetUDPEndpoint -LocalPort @($Port, $DirectoryPort) -ErrorAction SilentlyContinue |
    Where-Object { $_.LocalAddress -eq '0.0.0.0' -or $_.LocalAddress -eq $parsedAddress.ToString() -or $parsedAddress.Equals([System.Net.IPAddress]::Any) } |
    Select-Object -First 1
if ($null -ne $existingEndpoint) {
    throw "UDP port $($existingEndpoint.LocalPort) is already occupied by PID $($existingEndpoint.OwningProcess). No new server was started."
}
$logDirectory = Join-Path $PSScriptRoot 'Logs'
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
$serverLog = Join-Path $logDirectory ('Server-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.log')
$serverArgs = @('-batchmode', '-nographics', '--server', '--listen', $parsedAddress.ToString(), '--port', $Port, '--directory-port', $DirectoryPort, '-logFile', ('"' + $serverLog + '"'))
$serverProcess = Start-Process -FilePath $serverExe -ArgumentList $serverArgs -WorkingDirectory $PSScriptRoot -WindowStyle Hidden -PassThru
$deadline = (Get-Date).AddSeconds(45)
$ready = $false
while ((Get-Date) -lt $deadline) {
    $serverProcess.Refresh()
    if ($serverProcess.HasExited) {
        if (Test-Path -LiteralPath $serverLog) { Get-Content -Encoding UTF8 -LiteralPath $serverLog -Tail 15 }
        throw "Server exited before READY. Exit code: $($serverProcess.ExitCode). Log: $serverLog"
    }
    if ((Test-Path -LiteralPath $serverLog) -and
        (Select-String -LiteralPath $serverLog -SimpleMatch '[DedicatedServerBootstrap] READY' -Quiet)) {
        $ready = $true
        break
    }
    Start-Sleep -Milliseconds 250
}
if (-not $ready) {
    # Only terminate the process created by this invocation.
    $serverProcess.Refresh()
    if (-not $serverProcess.HasExited) { Stop-Process -Id $serverProcess.Id }
    throw "Server startup timed out before READY; the new process was stopped. Log: $serverLog"
}
Write-Host 'Server READY'
Write-Host "Server PID: $($serverProcess.Id)"
Write-Host "UDP listen: $ListenAddress`:$Port"
Write-Host "Room directory UDP: $ListenAddress`:$DirectoryPort"
Write-Host "Log: $serverLog"
Write-Host ('Stop (CMD): powershell -Command "Stop-Process -Id ' + $serverProcess.Id + '"')
