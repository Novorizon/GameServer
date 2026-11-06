using GameServer.Data;
using GameServer.Data.Abstractions;
using GameServer.Infrastructure.Storage;
using Microsoft.Extensions.Hosting;

namespace GameServer.Infrastructure.Persistence;

public sealed class DatabaseReadinessService(IDatabase database, DatabaseOptions options, StorageOptions storage) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        await database.WithSessionAsync(async (session, token) =>
        {
            if (!options.ValidateSchemaAtStartup) { await session.ScalarAsync(new("SELECT 1"), token); return true; }
            foreach (string sql in new[] { "SELECT id,device_id FROM account LIMIT 0", "SELECT player_id,progress,version FROM tcp_player_progress LIMIT 0", "SELECT player_id,user_id FROM tcp_facebook_binding LIMIT 0" })
                await session.QueryAsync(new(sql), _ => true, 1, token);
            if (storage.Provider == PlayerSaveProvider.Database)
            {
                await session.QueryAsync(new("SELECT player_id,data,version FROM tcp_player_save LIMIT 0"), _ => true, 1, token);
                await session.QueryAsync(new("SELECT player_id,operation_id,request_hash,committed_version FROM tcp_player_save_receipt LIMIT 0"), _ => true, 1, token);
            }
            return true;
        }, ct);
    }
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
