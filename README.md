# BackupGuard

Serviço do Windows que envia arquivos de backup (bancos de dados, exportações, etc.) para o Google Drive,
compactados e criptografados — feito para que um ataque de ransomware no computador não leve os backups junto.

## O que ele faz com cada backup
1. **Espera o arquivo ficar pronto.** O arquivo só é usado depois de ficar 2 minutos sem mudar e sem estar aberto por outro programa.
2. **Compacta e criptografa.** Usa 7z (LZMA2, nível máximo) com AES-256, que também esconde os nomes dos arquivos.
3. **Testa o .7z** recém-criado.
4. **Envia** para `BackupGuard/<Tarefa>` no Google Drive.
5. **Confere o MD5** do Drive contra o MD5 local.
6. **Apaga os arquivos da pasta local**, sem apagar a pasta. Isso só acontece se a conferência bater; se falhar, o original é mantido e o envio é tentado de novo.
7. **Avisa no Telegram** com tamanhos, taxa de compactação e tempo gasto.

## Avisos e proteções
- **Serviço do Windows**: inicia junto com o sistema, sem precisar de login, e reinicia sozinho se cair.
- **Alerta "backup não chegou"**: dispara quando nenhum backup novo chega até o horário esperado mais a tolerância.
- **Alerta de anomalia**: dispara quando o backup fica muito menor que o anterior, ou quando quase não compacta, o que é sinal de arquivo criptografado por ransomware.
- **Resumo diário**: último backup de cada tarefa, falhas, espaço em disco e uso do Drive.
- **Avisos de serviço iniciado e parado**: revelam reinícios inesperados.
- **Monitor externo opcional** (healthchecks.io): avisa mesmo se o PC morrer calado.
- **Retenção por tarefa**: de 1 dia a "nunca apagar". Os arquivos vencidos vão para a lixeira do Drive (recuperáveis por 30 dias), e os N backups mais recentes nunca saem.
- **Segredos protegidos**: token do Google, token do Telegram e senha ficam criptografados com DPAPI, em pasta acessível só a Administradores.

## Instalação (em cada servidor)

Pré-requisito: [7-Zip](https://www.7-zip.org/) instalado. A próxima versão vai trazê-lo embutido.

Em um PowerShell **como Administrador**, na pasta do projeto:

```powershell
.\scripts\install-service.ps1
```

Depois abra **BackupGuard** no Menu Iniciar (o Windows pede permissão de administrador) e configure:

1. **Tarefas**: pasta com os arquivos a proteger, horário esperado do backup e retenção.
2. **Notificações**: token do bot do Telegram e, se quiser, a URL do monitor externo.
3. **Segurança**: gerar a senha dos backups. **Guarde a senha fora do PC.**
4. **Google Drive**: conectar a conta nova, selecionando o `credentials.json`.
5. **Painel**: iniciar o serviço.

Toda alteração feita pela interface é avisada no Telegram, com o usuário do Windows que a fez.

Para ver a interface com dados fictícios, sem administrador e sem mexer no serviço:

```powershell
BackupGuard.exe --demo
```

Os mesmos ajustes também podem ser feitos por linha de comando: `BackupGuard.Service.exe help`.

### Credenciais do Google
1. Acesse <https://console.cloud.google.com> com a conta que vai guardar os backups e crie um projeto.
2. Em **APIs e serviços → Biblioteca**, ative a **Google Drive API**.
3. Em **Branding**, preencha nome, e-mail, página inicial, política de privacidade e domínio autorizado. Em **Público-alvo**, escolha *Externo* e **publique o app ("Em produção")**. No modo "Teste", o token expira em 7 dias; esse foi o erro `invalid_grant` do script antigo.
4. Em **Credenciais → Criar credenciais → ID do cliente OAuth**, escolha o tipo **App para computador** e baixe o JSON.
5. Use esse arquivo no comando `auth` e apague-o depois.

### Exemplo de `config.json`
```jsonc
{
  "machineName": "Servidor Principal",
  "sevenZipPath": "C:\\Program Files\\7-Zip\\7z.exe",
  "compressionLevel": 9,
  "compressionThreads": null,          // null = automático (metade dos núcleos, até 4)
  "driveRootFolderName": "BackupGuard",
  "scanIntervalSeconds": 30,
  "dailySummaryTime": "08:00",
  "telegram": { "enabled": true, "chatId": "123456789" },
  "healthCheck": { "pingUrl": null },  // ex.: "https://hc-ping.com/<uuid>"
  "jobs": [
    {
      "id": "financeiro",
      "name": "Financeiro",
      "sourceFolder": "D:\\Backups\\Financeiro",
      "includeSubfolders": true,
      "filePatterns": ["*"],
      "stableSeconds": 120,
      "expectedBackupTime": "12:00",
      "graceMinutes": 180,
      "retentionDays": 30,             // null = nunca apagar
      "minBackupsToKeep": 3
    }
  ]
}
```
Depois de alterar o arquivo manualmente: `Restart-Service BackupGuard`.

## Restaurar um backup
Baixe o `.7z` do Drive e abra com o 7-Zip usando a senha. Funciona em qualquer computador, sem precisar deste programa.

## Atualização automática
O serviço consulta os Releases deste repositório a cada 6 horas. Ele só instala uma versão que:
- esteja **assinada** com a chave do projeto (ECDSA P-256; a chave pública fica em `UpdateSignature.PublicKey`);
- seja **mais nova** que a instalada;
- tenha o `.zip` com o mesmo **SHA-256** que foi assinado.

A troca acontece entre um backup e outro. Antes de trocar, o serviço faz uma cópia da versão atual; se a nova não subir, a anterior é restaurada sozinha. O Telegram recebe um aviso antes e outro depois.

Para publicar uma versão:

```powershell
.\scripts\release.ps1 -Version 0.2.1 -Notes "O que mudou nesta versão."
```

A chave privada de assinatura fica só na máquina de quem publica (`%APPDATA%\BackupGuard-release`). Faça uma cópia de segurança com `dotnet run --project tools\BackupGuard.Release -- export-key <arquivo>`.

## Desenvolvimento
```powershell
dotnet build BackupGuard.sln
dotnet test BackupGuard.sln
```

| Pasta | Conteúdo |
|---|---|
| `src/BackupGuard.Core` | Configuração, segredos, 7-Zip, Google Drive, motor de backup, retenção, alertas e tradução de erros |
| `src/BackupGuard.Service` | Serviço do Windows e comandos de linha (`help` lista todos) |
| `src/BackupGuard.App` | Interface gráfica (WPF): Painel, Tarefas, Histórico, Notificações, Google Drive, Segurança e Registros |
| `tests/BackupGuard.Tests` | Testes, incluindo o fluxo completo com 7-Zip real e Drive simulado |
| `scripts/` | Instalação e remoção do serviço |

Arquivos em `C:\ProgramData\BackupGuard`: `config.json`, `secrets.bin`, `google\` (token), `state.json`, `history.jsonl`, `logs\`.

## Próximas etapas
- [x] Interface gráfica (WPF) no estilo do app de referência
- [ ] Instalador com 7-Zip embutido e atualização automática pelo GitHub (Velopack, com assinatura própria)
- [ ] Tela de restauração com teste de integridade
