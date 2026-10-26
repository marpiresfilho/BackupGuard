using System.Security.Principal;
using System.Text;
using BackupGuard.Core;
using BackupGuard.Core.Config;
using BackupGuard.Core.Drive;
using BackupGuard.Core.Notifications;
using BackupGuard.Core.Pipeline;
using BackupGuard.Core.Security;
using BackupGuard.Core.State;
using Google.Apis.Auth.OAuth2;

namespace BackupGuard.Service;

/// <summary>
/// Comandos de configuração. Até a interface gráfica ficar pronta, é por aqui que se configura.
/// Todos exigem administrador, porque mexem em %ProgramData%\BackupGuard.
/// </summary>
internal static class Cli
{
    private const string Help = """
        BackupGuard — comandos (execute em um terminal como Administrador):

          init                         cria a pasta de dados, protege com ACL e gera config.json de exemplo
          auth <credentials.json>      importa o cliente OAuth e autoriza a conta Google (abre o navegador)
          secret telegram              grava o token do bot do Telegram
          secret password              define a senha dos arquivos .7z (digitada)
          secret password --generate   gera uma senha forte e mostra uma única vez
          secret show-password         mostra a senha atual (para guardar em local seguro)
          test                         confere config, 7-Zip, Telegram e Google Drive
          run-once [--now]             faz uma varredura agora (--now ignora a espera de estabilidade)
          summary                      envia o resumo diário agora
          status                       mostra o último backup de cada tarefa
        """;

    public static async Task<int> RunAsync(string[] args, AppPaths paths)
    {
        // Chamado pelo próprio serviço (sem console) para trocar a versão instalada.
        if (args is ["apply-update", var staged, var installDir, var from, var to])
            return UpdateApplier.Run(paths, staged, installDir, from, to);

        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            var command = args[0].ToLowerInvariant();
            if (command is "help" or "-h" or "--help" or "/?")
            {
                Console.WriteLine(Help);
                return 0;
            }
            RequireAdmin();

            return command switch
            {
                "init" => Init(paths),
                "auth" when args.Length >= 2 => await AuthAsync(paths, args[1]),
                "secret" when args.Length >= 2 => Secret(paths, args[1].ToLowerInvariant(), args.Skip(2).ToArray()),
                "test" => await TestAsync(paths),
                "run-once" => await RunOnceAsync(paths, args.Contains("--now")),
                "summary" => await SummaryAsync(paths),
                "status" => Status(paths),
                _ => Fail("Comando desconhecido ou incompleto.\n\n" + Help),
            };
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    private static int Init(AppPaths paths)
    {
        paths.EnsureDirectories();
        FolderAcl.RestrictToAdmins(paths.Root);
        var store = new ConfigStore(paths);
        if (store.Exists)
        {
            Console.WriteLine($"config.json já existe em {paths.ConfigFile} — mantido.");
        }
        else
        {
            // Sem tarefa de exemplo: uma pasta de exemplo que exista de verdade teria os arquivos enviados e apagados.
            store.Save(new AppConfig { Telegram = new TelegramConfig { Enabled = false } });
            Console.WriteLine($"config.json criado em {paths.ConfigFile}. Cadastre as tarefas pela interface.");
        }
        Console.WriteLine($"Pasta {paths.Root} protegida: acesso só para SYSTEM e Administradores.");
        return 0;
    }

    private static async Task<int> AuthAsync(AppPaths paths, string credentialsFile)
    {
        paths.EnsureDirectories();
        var client = (await GoogleClientSecrets.FromFileAsync(credentialsFile)).Secrets;
        var store = new SecretStore(paths);
        store.Update(s =>
        {
            s.GoogleClientId = client.ClientId;
            s.GoogleClientSecret = client.ClientSecret;
        });

        Console.WriteLine("Abrindo o navegador. Entre com a conta Google que vai guardar os backups...");
        var secrets = store.Load();
        await GoogleDriveClient.AuthorizeInteractiveAsync(paths, secrets, CancellationToken.None);
        var drive = await GoogleDriveClient.CreateAsync(paths, secrets, CancellationToken.None);
        var quota = await drive.GetQuotaAsync(CancellationToken.None);
        Console.WriteLine($"✔ Conta autorizada. Uso do Drive: {Format.Bytes(quota.Usage)}" +
                          (quota.Limit is { } l ? $" de {Format.Bytes(l)}" : ""));
        Console.WriteLine($"O token foi salvo criptografado. Pode apagar '{credentialsFile}' deste computador.");
        return 0;
    }

    private static int Secret(AppPaths paths, string which, string[] rest)
    {
        paths.EnsureDirectories();
        var store = new SecretStore(paths);
        switch (which)
        {
            case "telegram":
                var token = ReadHidden("Token do bot do Telegram: ");
                if (string.IsNullOrWhiteSpace(token))
                    return Fail("Token vazio.");
                store.Update(s => s.TelegramBotToken = token.Trim());
                Console.WriteLine("✔ Token salvo.");
                return 0;

            case "password":
                string password;
                if (rest.Contains("--generate"))
                {
                    password = SecretStore.GeneratePassword();
                    Console.WriteLine($"\nSenha gerada:\n\n    {password}\n");
                }
                else
                {
                    password = ReadHidden("Nova senha dos arquivos .7z (mín. 16 caracteres): ");
                    if (password.Length < 16)
                        return Fail("Senha curta demais.");
                    if (ReadHidden("Repita a senha: ") != password)
                        return Fail("As senhas não conferem.");
                }
                var previous = store.Load().ArchivePassword;
                store.Update(s => s.ChangeArchivePassword(password));
                Console.WriteLine("✔ Senha salva.");
                Console.WriteLine("⚠  GUARDE ESTA SENHA FORA DESTE COMPUTADOR (gerenciador de senhas e papel).");
                Console.WriteLine("   Sem ela não é possível restaurar nenhum backup.");
                if (previous is not null && previous != password)
                    Console.WriteLine("⚠  Backups antigos continuam com a senha ANTERIOR (guardada como senha antiga) — guarde as duas.");
                return 0;

            case "show-password":
                var current = store.Load().ArchivePassword;
                Console.WriteLine(current is null ? "Nenhuma senha definida." : $"Senha atual: {current}");
                return 0;

            default:
                return Fail("Use: secret telegram | secret password [--generate] | secret show-password");
        }
    }

    private static async Task<int> TestAsync(AppPaths paths)
    {
        using var provider = BuildProvider(paths);
        var config = provider.GetRequiredService<AppConfig>();
        var secrets = provider.GetRequiredService<Secrets>();
        var ok = true;

        void Check(bool passed, string label, string? detail = null)
        {
            ok &= passed;
            Console.WriteLine($"{(passed ? "✔" : "✘")} {label}{(detail is null ? "" : $" — {detail}")}");
        }

        Check(true, "config.json válido", $"{config.Jobs.Count} tarefa(s)");
        foreach (var c in config.Jobs)
            Check(Directory.Exists(c.SourceFolder), $"pasta de {c.Name}", c.SourceFolder);

        try
        {
            Check(true, "7-Zip", await provider.GetRequiredService<SevenZipCompressor>().GetVersionAsync(CancellationToken.None));
        }
        catch (Exception ex)
        {
            Check(false, "7-Zip", ex.Message);
        }

        Check(!string.IsNullOrEmpty(secrets.ArchivePassword), "senha dos arquivos definida");
        Check(!config.Telegram.Enabled || (!string.IsNullOrEmpty(secrets.TelegramBotToken) && !string.IsNullOrEmpty(config.Telegram.ChatId)),
            "Telegram configurado");
        await provider.GetRequiredService<INotifier>().SendAsync("🧪 Teste de configuração do BackupGuard");

        try
        {
            var folders = provider.GetRequiredService<DriveFolders>();
            var quota = await (await provider.GetRequiredService<IDriveClientProvider>().GetAsync(CancellationToken.None))
                .GetQuotaAsync(CancellationToken.None);
            Check(true, "Google Drive", $"{Format.Bytes(quota.Usage)} usados" + (quota.Limit is { } l ? $" de {Format.Bytes(l)}" : ""));
            foreach (var c in config.Jobs)
            {
                await folders.ForJobAsync(c, CancellationToken.None);
                Check(true, $"pasta no Drive: {config.DriveRootFolderName}/{c.Name}");
            }
        }
        catch (Exception ex)
        {
            Check(false, "Google Drive", Core.Errors.FriendlyError.From(ex).Text);
        }

        Console.WriteLine(ok ? "\nTudo certo." : "\nHá itens a corrigir.");
        return ok ? 0 : 1;
    }

    private static async Task<int> RunOnceAsync(AppPaths paths, bool now)
    {
        using var provider = BuildProvider(paths);
        var engine = provider.GetRequiredService<BackupEngine>();
        engine.CleanupWorkDir();
        Console.WriteLine(now
            ? "Varrendo e enviando agora (sem esperar estabilidade)..."
            : "Varrendo (arquivos só são enviados após o tempo de estabilidade)...");
        await engine.RunCycleAsync(ignoreStability: now, CancellationToken.None);
        Console.WriteLine("Concluído. Veja o histórico com 'status'.");
        return 0;
    }

    private static async Task<int> SummaryAsync(AppPaths paths)
    {
        using var provider = BuildProvider(paths);
        var summary = await provider.GetRequiredService<SummaryBuilder>().BuildAsync(CancellationToken.None);
        await provider.GetRequiredService<INotifier>().SendAsync(summary);
        Console.WriteLine("Resumo enviado.");
        return 0;
    }

    private static int Status(AppPaths paths)
    {
        var config = new ConfigStore(paths).Load();
        var state = new StateStore(paths);
        foreach (var c in config.Jobs)
        {
            var cs = state.Read(s => s.Jobs.GetValueOrDefault(c.Id));
            Console.WriteLine(cs?.LastSuccessUtc is { } last
                ? $"{c.Name}: último backup {Format.Local(last)} — {cs.LastArchiveName} ({Format.Bytes(cs.LastArchiveSize ?? 0)})"
                : $"{c.Name}: nenhum backup enviado ainda");
        }
        Console.WriteLine("\nÚltimos eventos:");
        foreach (var h in new BackupHistory(paths).ReadLast(10))
            Console.WriteLine($"  {Format.Local(h.TimestampUtc)} {(h.Success ? "✔" : "✘")} {h.JobId} {Path.GetFileName(h.SourceFile)}" +
                              (h.Error is null ? "" : $" — {h.Error}"));
        return 0;
    }

    private static ServiceProvider BuildProvider(AppPaths paths)
    {
        paths.EnsureDirectories();
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Information));
        services.AddBackupGuard(paths);
        return services.BuildServiceProvider();
    }

    private static void RequireAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new UnauthorizedAccessException("Execute este comando em um terminal aberto como Administrador.");
    }

    private static string ReadHidden(string prompt)
    {
        Console.Write(prompt);
        var sb = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
                break;
            if (key.Key == ConsoleKey.Backspace)
            {
                if (sb.Length > 0)
                    sb.Length--;
            }
            else if (!char.IsControl(key.KeyChar))
            {
                sb.Append(key.KeyChar);
            }
        }
        Console.WriteLine();
        return sb.ToString();
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine("✘ " + message);
        return 1;
    }
}
