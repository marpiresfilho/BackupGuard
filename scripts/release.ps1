<#
.SYNOPSIS
    Publica uma nova versão do BackupGuard no GitHub. As instalações se atualizam sozinhas.

.EXAMPLE
    .\scripts\release.ps1 -Version 0.2.1 -Notes "Correção no resumo diário."

.DESCRIPTION
    1. Atualiza a versão nos projetos.
    2. Gera o pacote (scripts\build-package.ps1).
    3. Assina com a chave privada desta máquina (tools\BackupGuard.Release).
    4. Faz commit, cria a tag vX.Y.Z, envia ao GitHub e cria o Release com os arquivos.
#>
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$Notes,
    # Linha extra opcional no fim da mensagem do commit (ex.: Co-Authored-By).
    [string]$Trailer
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "$env:ProgramFiles\dotnet\dotnet.exe" }

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Versão deve estar no formato 1.2.3" }
if (git tag --list "v$Version") { throw "A versão v$Version já existe." }

# 1. Versão nos projetos.
foreach ($csproj in "src\BackupGuard.Service\BackupGuard.Service.csproj", "src\BackupGuard.App\BackupGuard.App.csproj") {
    $text = Get-Content $csproj -Raw
    $text = $text -replace '<Version>[^<]+</Version>', "<Version>$Version</Version>"
    [IO.File]::WriteAllText((Join-Path $repoRoot $csproj), $text)
}

# 2. Testes e pacote.
& $dotnet test BackupGuard.sln -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Os testes falharam; nada foi publicado." }
& (Join-Path $PSScriptRoot "build-package.ps1")
$zip = Join-Path $repoRoot "artifacts\BackupGuard-$Version.zip"
if (-not (Test-Path $zip)) { throw "Pacote não encontrado: $zip" }

# 3. Assinatura.
& $dotnet run --project tools\BackupGuard.Release -- sign $zip $Version $Notes
if ($LASTEXITCODE -ne 0) { throw "Falha ao assinar." }

# 4. Git + GitHub Release.
git add -A
$commitArgs = @("-m", "Versão $Version", "-m", $Notes)
if ($Trailer) { $commitArgs += @("-m", $Trailer) }
git commit @commitArgs
git tag "v$Version"
git push origin HEAD --tags
gh release create "v$Version" $zip "artifacts\release.json" "artifacts\release.json.sig" `
    --title "BackupGuard $Version" --notes $Notes
if ($LASTEXITCODE -ne 0) { throw "Falha ao criar o Release no GitHub." }

Write-Host ""
Write-Host "Versão $Version publicada. As instalações vão se atualizar em até 6 horas (ou em Atualizações → Procurar agora)." -ForegroundColor Green
