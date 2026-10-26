<#
.SYNOPSIS
    Gera o pacote de instalação para levar a outros computadores: artifacts\BackupGuard-<versão>.zip

.DESCRIPTION
    O pacote é autocontido: não precisa de .NET, 7-Zip nem do código-fonte no destino.
    Conteúdo: BackupGuard.exe (interface), BackupGuard.Service.exe (serviço), 7-Zip\ (7z.exe + licença),
    Instalar.cmd (duplo clique), install.ps1, uninstall.ps1 e LEIA-ME.txt.

    -PublishOnly só compila para a pasta 'publish' (usado pelo install-service.ps1 no código-fonte).
#>
param([switch]$PublishOnly)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $repoRoot "publish"
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "$env:ProgramFiles\dotnet\dotnet.exe" }

# 1. Compila os dois programas como executáveis únicos e autocontidos.
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
foreach ($project in "BackupGuard.Service", "BackupGuard.App") {
    Write-Host "Compilando $project..." -ForegroundColor Cyan
    & $dotnet publish (Join-Path $repoRoot "src\$project") -c Release -r win-x64 --self-contained `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $publishDir -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "Falha na compilação de $project." }
}

# 2. Embute o 7-Zip (licença LGPL, redistribuição permitida com o arquivo de licença).
$sevenZip = "$env:ProgramFiles\7-Zip"
if (-not (Test-Path "$sevenZip\7z.exe")) { throw "7-Zip não encontrado em $sevenZip (necessário para montar o pacote)." }
New-Item -ItemType Directory -Force -Path "$publishDir\7-Zip" | Out-Null
foreach ($file in "7z.exe", "7z.dll", "License.txt") {
    Copy-Item "$sevenZip\$file" "$publishDir\7-Zip\" -Force
}

if ($PublishOnly) { exit 0 }

# 3. Monta a pasta do pacote e compacta.
[xml]$csproj = Get-Content (Join-Path $repoRoot "src\BackupGuard.Service\BackupGuard.Service.csproj")
$version = ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
$name = "BackupGuard-$version"
$artifacts = Join-Path $repoRoot "artifacts"
$packageDir = Join-Path $artifacts $name
if (Test-Path $packageDir) { Remove-Item $packageDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $packageDir | Out-Null

Copy-Item "$publishDir\BackupGuard.exe", "$publishDir\BackupGuard.Service.exe" $packageDir
Copy-Item "$publishDir\7-Zip" $packageDir -Recurse
Copy-Item (Join-Path $PSScriptRoot "install-service.ps1") (Join-Path $packageDir "install.ps1")
Copy-Item (Join-Path $PSScriptRoot "uninstall-service.ps1") (Join-Path $packageDir "uninstall.ps1")

Set-Content -Path (Join-Path $packageDir "Instalar.cmd") -Encoding ASCII -Value @'
@echo off
rem Instala ou atualiza o BackupGuard (o Windows vai pedir permissao de administrador).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
'@

$readme = @"
BackupGuard $version
====================

INSTALAR OU ATUALIZAR
  1. Extraia este .zip em qualquer pasta.
  2. De dois cliques em Instalar.cmd e aceite o pedido de administrador.
  3. Abra "BackupGuard" no Menu Iniciar e configure:
     Tarefas, Notificacoes, Seguranca (senha), Google Drive e, no Painel, Iniciar.

  Numa atualizacao, a configuracao e mantida e o servico reinicia sozinho.

REMOVER
  Rode uninstall.ps1 (botao direito > Executar com o PowerShell).
  A configuracao em C:\ProgramData\BackupGuard e mantida.

RESTAURAR UM BACKUP
  Baixe o arquivo .7z do Google Drive e abra com o 7-Zip usando a senha dos backups.

Inclui 7-Zip (www.7-zip.org), distribuido sob a licenca GNU LGPL - veja 7-Zip\License.txt.
"@
Set-Content -Path (Join-Path $packageDir "LEIA-ME.txt") -Value $readme -Encoding UTF8

$zip = Join-Path $artifacts "$name.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$packageDir\*" -DestinationPath $zip
Write-Host ""
Write-Host "Pacote gerado: $zip ($([math]::Round((Get-Item $zip).Length / 1MB)) MB)" -ForegroundColor Green
