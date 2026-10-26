using System.ServiceProcess;

namespace BackupGuard.App.Services;

public enum ServiceRunState
{
    NotInstalled,
    Stopped,
    Starting,
    Running,
    Stopping,
}

public interface IServiceControl
{
    ServiceRunState GetState();
    Task StartAsync();
    Task StopAsync();
    Task RestartAsync();
}

/// <summary>Controla o serviço do Windows "BackupGuard" (exige administrador).</summary>
public sealed class ServiceControl : IServiceControl
{
    private const string Name = "BackupGuard";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public ServiceRunState GetState()
    {
        try
        {
            using var sc = new ServiceController(Name);
            return sc.Status switch
            {
                ServiceControllerStatus.Running => ServiceRunState.Running,
                ServiceControllerStatus.StartPending or ServiceControllerStatus.ContinuePending => ServiceRunState.Starting,
                ServiceControllerStatus.StopPending or ServiceControllerStatus.PausePending => ServiceRunState.Stopping,
                _ => ServiceRunState.Stopped,
            };
        }
        catch (InvalidOperationException)
        {
            return ServiceRunState.NotInstalled;
        }
    }

    public Task StartAsync() => Task.Run(() =>
    {
        using var sc = new ServiceController(Name);
        if (sc.Status != ServiceControllerStatus.Running)
        {
            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, Timeout);
        }
    });

    public Task StopAsync() => Task.Run(() =>
    {
        using var sc = new ServiceController(Name);
        if (sc.Status != ServiceControllerStatus.Stopped)
        {
            sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, Timeout);
        }
    });

    public async Task RestartAsync()
    {
        await StopAsync();
        await StartAsync();
    }
}

/// <summary>Serviço simulado para o modo demonstração.</summary>
public sealed class DemoServiceControl : IServiceControl
{
    private ServiceRunState _state = ServiceRunState.Running;

    public ServiceRunState GetState() => _state;

    public async Task StartAsync()
    {
        await Task.Delay(400);
        _state = ServiceRunState.Running;
    }

    public async Task StopAsync()
    {
        await Task.Delay(400);
        _state = ServiceRunState.Stopped;
    }

    public async Task RestartAsync()
    {
        await StopAsync();
        await StartAsync();
    }
}
