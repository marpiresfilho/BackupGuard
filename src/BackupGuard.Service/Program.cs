using BackupGuard.Core.Config;
using BackupGuard.Service;
using Serilog;

var paths = new AppPaths();

// Com argumentos: comandos de configuração (init, auth, secret, test...). Sem: roda como serviço.
if (args.Length > 0)
    return await Cli.RunAsync(args, paths);

paths.EnsureDirectories();
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", Serilog.Events.LogEventLevel.Information)
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(paths.LogsDir, "service-.log"),
        rollingInterval: RollingInterval.Day, retainedFileCountLimit: 60,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    // Executado pelo Windows a partir de C:\Windows\System32; fixa a pasta do executável.
    Directory.SetCurrentDirectory(AppContext.BaseDirectory);

    var builder = Host.CreateApplicationBuilder();
    builder.Services.AddWindowsService(o => o.ServiceName = "BackupGuard");
    builder.Services.AddSerilog();
    builder.Services.AddBackupGuard(paths);
    builder.Services.AddHostedService<BackupWorker>();
    builder.Services.AddHostedService<MaintenanceWorker>();
    builder.Services.AddHostedService<UpdateWorker>();
    builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(30));

    await builder.Build().RunAsync();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "O serviço não conseguiu iniciar");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
