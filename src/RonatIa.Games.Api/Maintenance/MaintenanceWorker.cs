using Microsoft.Extensions.Options;
using RonatIa.Games.Application.Maintenance;
using RonatIa.Games.Application.Options;

namespace RonatIa.Games.Api.Maintenance;

/// <summary>
/// Roda a limpeza (<see cref="MaintenanceService"/>) de tempos em tempos, enquanto a API está acordada. Falhas são registradas
/// e a próxima rodada tenta de novo: a limpeza nunca derruba nem atrasa a API.
/// </summary>
public sealed class MaintenanceWorker(
    IServiceScopeFactory scopes,
    IOptions<MaintenanceOptions> options,
    ILogger<MaintenanceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled || settings.Interval <= TimeSpan.Zero)
        {
            logger.LogInformation("Limpeza automática desligada");
            return;
        }

        try
        {
            // Dá um tempo para a API e o banco acordarem antes da primeira rodada.
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, settings.Interval.TotalSeconds)), stoppingToken);

            using var timer = new PeriodicTimer(settings.Interval);
            do
            {
                await RunOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // desligando
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var report = await scope.ServiceProvider.GetRequiredService<MaintenanceService>().RunAsync(cancellationToken);
            if (!report.IsEmpty)
            {
                logger.LogInformation(
                    "Limpeza: {Lobbies} lobbies e {Games} partidas abandonadas canceladas, {Events} eventos e {Logins} sessões de login apagados",
                    report.LobbiesCancelled,
                    report.GamesCancelled,
                    report.EventsDeleted,
                    report.LoginsDeleted);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Falha na limpeza automática; tentaremos de novo na próxima rodada");
        }
    }
}
