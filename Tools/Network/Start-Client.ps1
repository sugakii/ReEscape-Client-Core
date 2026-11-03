param(
    [string]$Address = '47.114.104.192',
    [ValidateRange(1,65535)][int]$Port = 7777,
    [ValidateRange(1,65535)][int]$DirectoryPort = 7778
)
$ErrorActionPreference = 'Stop'
$parsedAddress = $null
if (-not [System.Net.IPAddress]::TryParse($Address, [ref]$parsedAddress) -or
    $parsedAddress.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork -or
    $parsedAddress.Equals([System.Net.IPAddress]::Any)) {
    throw 'Address must be the server IPv4 address; 0.0.0.0 is not a destination.'
}
$clientExe = Join-Path $PSScriptRoot 'ReEscapeClient.exe'
if (-not (Test-Path -LiteralPath $clientExe)) { throw "Missing client build: $clientExe" }
$logDirectory = Join-Path $PSScriptRoot 'Logs'
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
$clientLog = Join-Path $logDirectory ('Client-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.log')
$clientArgs = @('--address', $parsedAddress.ToString(), '--port', $Port, '--directory-port', $DirectoryPort, '-logFile', ('"' + $clientLog + '"'))
$clientProcess = Start-Process -FilePath $clientExe -ArgumentList $clientArgs -WorkingDirectory $PSScriptRoot -PassThru
Write-Host "Client PID: $($clientProcess.Id)"
Write-Host "Server: $Address`:$Port"
Write-Host "Room directory: $Address`:$DirectoryPort (UDP); client starts at the main menu."
Write-Host "Log: $clientLog"
