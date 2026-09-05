param()

$ErrorActionPreference = 'Stop'
$fanControl = 'C:\Program Files (x86)\FanControl'

if (-not ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Bitte PowerShell als Administrator starten.'
}
if (Get-Process -Name FanControl,GHelper -ErrorAction SilentlyContinue) {
    throw 'Bitte Fan Control und G-Helper vollständig schließen.'
}

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'FanControl.ROGNUC15JNK.dll') -Destination (Join-Path $fanControl 'Plugins\FanControl.ROGNUC15JNK.dll') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ROG-NUC15JNK-ENABLE-CONTROLS.TEST') -Destination (Join-Path $fanControl 'ROG-NUC15JNK-ENABLE-CONTROLS.TEST') -Force
Write-Host 'ROG NUC Plugin installiert.'
