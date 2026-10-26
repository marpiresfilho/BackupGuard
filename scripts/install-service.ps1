<#
.SYNOPSIS
    Instala (ou atualiza) o BackupGuard: serviço do Windows, interface e 7-Zip embutido.

.DESCRIPTION
    - Copia os programas para C:\Program Files\BackupGuard (só administradores podem alterar).
    - Registra o serviço com início automático, antes do login no Windows, e reinício se cair.
    - Cria o atalho "BackupGuard" no Menu Iniciar.
    - Numa atualização, mantém toda a configuração em C:\ProgramData\BackupGuard.

    Funciona de dois jeitos:
      - Dentro do pacote (BackupGuard-x.y.z.zip): os programas estão na mesma pasta deste script.
      - No código-fonte: compila antes, a não ser que receba -SkipBuild.
#>
param(
    [string]$InstallDir = "$env:ProgramFiles\BackupGuard",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

# Pede permissão de administrador sozinho (UAC) e continua numa janela elevada.
$identity = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $relaunch = @('-NoExit', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-InstallDir', "`"$InstallDir`"")
    if ($SkipBuild) { $relaunch += '-SkipBuild' }
    Start-Process powershell -Verb RunAs -ArgumentList $relaunch
    exit
}

$serviceName = "BackupGuard"
$isPackage = Test-Path (Join-Path $PSScriptRoot "BackupGuard.Service.exe")
if ($isPackage) {
    $sourceDir = $PSScriptRoot
}
else {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $sourceDir = Join-Path $repoRoot "publish"
    if (-not $SkipBuild) {
        & (Join-Path $PSScriptRoot "build-package.ps1") -PublishOnly
        if ($LASTEXITCODE -ne 0) { throw "Falha na compilação." }
    }
}

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing -and $existing.Status -ne "Stopped") {
    Write-Host "Parando serviço existente..." -ForegroundColor Cyan
    Stop-Service -Name $serviceName -Force
    (Get-Service $serviceName).WaitForStatus("Stopped", [TimeSpan]::FromSeconds(60))
}

Write-Host "Copiando arquivos para $InstallDir..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
foreach ($item in "BackupGuard.exe", "BackupGuard.Service.exe", "7-Zip", "uninstall.ps1") {
    $path = Join-Path $sourceDir $item
    if (Test-Path $path) { Copy-Item -Path $path -Destination $InstallDir -Recurse -Force }
}
$exe = Join-Path $InstallDir "BackupGuard.Service.exe"

if (-not $existing) {
    Write-Host "Registrando serviço..." -ForegroundColor Cyan
    & sc.exe create $serviceName binPath= "`"$exe`"" start= auto DisplayName= "BackupGuard - Backup em nuvem" | Out-Null
    & sc.exe description $serviceName "Envia backups para o Google Drive com compactação e criptografia." | Out-Null
}

# Reinicia sozinho se cair: após 1 min, 1 min, 5 min; zera a contagem a cada 24h.
& sc.exe failure $serviceName reset= 86400 actions= restart/60000/restart/60000/restart/300000 | Out-Null
& sc.exe failureflag $serviceName 1 | Out-Null

# Atalho da interface no Menu Iniciar (para todos os usuários; abrir pede administrador).
$shortcutPath = Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs\BackupGuard.lnk"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = Join-Path $InstallDir "BackupGuard.exe"
$shortcut.WorkingDirectory = $InstallDir
$shortcut.Description = "BackupGuard - backup em nuvem"
$shortcut.Save()

if (-not (Test-Path "$env:ProgramData\BackupGuard\config.json")) {
    & $exe init | Out-Null
}

try {
    Start-Service -Name $serviceName
    Write-Host ""
    Write-Host "BackupGuard instalado e em execução." -ForegroundColor Green
}
catch {
    # Instalação nova: o serviço só sobe depois que houver ao menos uma tarefa configurada.
    Write-Host ""
    Write-Host "BackupGuard instalado. Abra 'BackupGuard' no Menu Iniciar e configure:" -ForegroundColor Yellow
    Write-Host "  1. Tarefas: pastas a proteger e horários"
    Write-Host "  2. Notificações: token do Telegram"
    Write-Host "  3. Segurança: gerar a senha dos backups (guarde fora do computador)"
    Write-Host "  4. Google Drive: conectar a conta"
    Write-Host "  5. Salvar e, no Painel, Iniciar o serviço"
}
