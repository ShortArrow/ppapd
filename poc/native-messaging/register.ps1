<#
.SYNOPSIS
Builds the PoC Native Messaging host and registers it for Chrome under HKCU.

.DESCRIPTION
Writes the host manifest next to the built executable and points
HKCU\Software\Google\Chrome\NativeMessagingHosts\jp.shortarrow.ppapd.poc at it.
Needs no administrator rights. Pass -Unregister to delete the registry key.
#>
param([switch]$Unregister)

$ErrorActionPreference = 'Stop'
$name = 'jp.shortarrow.ppapd.poc'
$extensionId = 'emhcpcooogencdnpjbgopakclfnojche'
$key = "HKCU:\Software\Google\Chrome\NativeMessagingHosts\$name"

if ($Unregister) {
    Remove-Item -Path $key -ErrorAction SilentlyContinue
    return
}

$hostDir = Join-Path $PSScriptRoot 'host'
dotnet build $hostDir -c Release --nologo -v quiet
$exe = Join-Path $hostDir 'bin\Release\net10.0\PocHost.exe'

$manifestPath = Join-Path $hostDir 'bin\Release\net10.0\host-manifest.json'
@{
    name            = $name
    description     = 'ppapd PoC native messaging host'
    path            = $exe
    type            = 'stdio'
    allowed_origins = @("chrome-extension://$extensionId/")
} | ConvertTo-Json | Set-Content -Path $manifestPath -Encoding utf8NoBOM

New-Item -Path $key -Force | Out-Null
Set-ItemProperty -Path $key -Name '(default)' -Value $manifestPath
Get-Item -Path $key
