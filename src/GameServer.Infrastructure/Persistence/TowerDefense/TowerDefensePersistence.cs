using GameServer.Data.Abstractions;
using GameServer.Gameplay.Modules.TowerDefense;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GameServer.Infrastructure.Persistence.TowerDefense;

public static class TowerDefensePersistence
{
    public static IServiceCollection AddTowerDefensePersistence(this IServiceCollection services, TowerDefenseOptions options)
    {
        options.Validate();
        if (!options.Enabled) return services;
        services.AddSingleton<ITowerDefenseStore, DatabaseTowerDefenseStore>();
        services.AddSingleton<IHostedService, TowerDefenseReadinessService>();
        return services;
    }
}

// Validate optional tables only when this feature is enabled. Migrations remain explicit.
public sealed class TowerDefenseReadinessService(IDatabase database, DatabaseOptions options) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.ValidateSchemaAtStartup) return;
        await database.WithSessionAsync(async (session, token) =>
        {
            await session.QueryAsync(new("SELECT player_id,version,state_json FROM tcp_td_player LIMIT 0"), _ => true, 1, token);
            await session.QueryAsync(new("SELECT player_id,operation_id,fingerprint,response,created_at_unix_ms FROM tcp_td_receipt LIMIT 0"), _ => true, 1, token);
            return true;
        }, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
