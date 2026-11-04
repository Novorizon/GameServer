using GameServer.Data;
using GameServer.Data.Abstractions;
using Microsoft.Extensions.Hosting;

namespace GameServer.Infrastructure.Persistence;

public sealed class DatabaseReadinessService(IDatabase database, DatabaseOptions options) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        await database.WithSessionAsync(async (session, token) =>
        {
            if (!options.ValidateSchemaAtStartup) { await session.ScalarAsync(new("SELECT 1"), token); return true; }
            foreach (string sql in new[] { "SELECT id,device_id FROM account LIMIT 0", "SELECT player_id,progress,version FROM tcp_player_progress LIMIT 0", "SELECT player_id,user_id FROM tcp_facebook_binding LIMIT 0" })
                await session.QueryAsync(new(sql), _ => true, 1, token);
            return true;
        }, ct);
    }
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
