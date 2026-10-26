<#
.SYNOPSIS
    Remove o BackupGuard. Os dados em %ProgramData%\BackupGuard (config, token, senha) são mantidos.
#>
param([string]$InstallDir = "$env:ProgramFiles\BackupGuard")

$ErrorActionPreference = "Stop"

$identity = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell -Verb RunAs -ArgumentList @('-NoExit', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"")
    exit
}

$serviceName = "BackupGuard"
if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    & sc.exe delete $serviceName | Out-Null
    Write-Host "Serviço removido."
}
Remove-Item (Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs\BackupGuard.lnk") -ErrorAction SilentlyContinue
if (Test-Path $InstallDir) {
    Remove-Item -Recurse -Force $InstallDir
    Write-Host "Arquivos removidos de $InstallDir."
}
Write-Host "Dados mantidos em $env:ProgramData\BackupGuard (apague manualmente se quiser)."
