using System.Data.Common;
using System.Text.Json;
using GameServer.Data;
using GameServer.Data.Abstractions;
using GameServer.Gameplay.Modules.TowerDefense;
using GameServer.Protocol.Messages;
using Google.Protobuf;

namespace GameServer.Infrastructure.Persistence.TowerDefense;

public sealed class DatabaseTowerDefenseStore(IDatabase database, TimeProvider timeProvider) : ITowerDefenseStore
{
    private const int MaximumStateBytes = 16_384;

    public async ValueTask<TowerDefensePlayer> ReadAsync(long playerId, CancellationToken ct)
    {
        var rows = await database.QueryAsync(new("SELECT version,state_json FROM tcp_td_player WHERE player_id=@id", ("id", playerId)), reader =>
        {
            string json = reader.GetString(1);
            if (json.Length > MaximumStateBytes) throw new InvalidDataException("Tower defense state exceeds its bound.");
            var player = JsonSerializer.Deserialize<TowerDefensePlayer>(json) ?? throw new InvalidDataException("Missing tower defense state.");
            if (player.PlayerId != playerId || player.Version != reader.GetInt64(0))
                throw new InvalidDataException("Tower defense state identity/version mismatch.");
            return player;
        }, 1, ct);
        return rows.Count == 0 ? new() { PlayerId = playerId } : rows[0];
    }

    public async ValueTask<TowerDefenseReceipt?> ReadReceiptAsync(long playerId, string operationId, CancellationToken ct) =>
        await ReadReceiptAsync(database, playerId, operationId, ct);

    private static async ValueTask<TowerDefenseReceipt?> ReadReceiptAsync(IDatabaseExecutor executor, long playerId, string operationId, CancellationToken ct)
    {
        var rows = await executor.QueryAsync(new("SELECT fingerprint,response FROM tcp_td_receipt WHERE player_id=@id AND operation_id=@operation", ("id", playerId), ("operation", operationId)),
            ReadReceipt, 1, ct);
        return rows.Count == 0 ? null : rows[0];
    }

    private static TowerDefenseReceipt ReadReceipt(DbDataReader reader)
    {
        byte[] bytes = reader.GetFieldValue<byte[]>(1);
        if (bytes.Length > MaximumStateBytes) throw new InvalidDataException("Tower defense receipt exceeds its bound.");
        return new(reader.GetString(0), TdResponse.Parser.ParseFrom(bytes));
    }

    public async ValueTask<bool> TryCommitAsync(TowerDefenseWrite write, CancellationToken ct)
    {
        if (write.PlayerId <= 0 || write.Player.PlayerId != write.PlayerId || write.ExpectedVersion < 0 ||
            write.ExpectedVersion == long.MaxValue || write.Player.Version != write.ExpectedVersion + 1)
            throw new ArgumentException("Invalid tower defense commit.", nameof(write));
        string json = JsonSerializer.Serialize(write.Player);
        byte[] response = write.Response.ToByteArray();
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumStateBytes || response.Length > MaximumStateBytes)
            throw new InvalidOperationException("Tower defense aggregate exceeds its bound.");
        try
        {
            return await database.InTransactionAsync(async (session, token) =>
            {
                // Every command in the transaction uses this same pinned session.
                if (await ReadReceiptAsync(session, write.PlayerId, write.OperationId, token) != null) return false;
                string sql = write.ExpectedVersion == 0
                    ? "INSERT INTO tcp_td_player(player_id,version,state_json) VALUES(@id,@next,@state)"
                    : "UPDATE tcp_td_player SET version=@next,state_json=@state WHERE player_id=@id AND version=@expected";
                int count = await session.ExecuteAsync(new(sql, ("id", write.PlayerId), ("next", write.Player.Version), ("state", json), ("expected", write.ExpectedVersion)), token);
                if (count != 1) return false;
                await session.ExecuteAsync(new("INSERT INTO tcp_td_receipt(player_id,operation_id,fingerprint,response,created_at_unix_ms) VALUES(@id,@operation,@fingerprint,@response,@created)",
                    ("id", write.PlayerId), ("operation", write.OperationId), ("fingerprint", write.Fingerprint), ("response", response),
                    ("created", timeProvider.GetUtcNow().ToUnixTimeMilliseconds())), token);
                return true;
            }, ct: ct);
        }
        // A concurrent first insert/receipt must roll back its entire transaction before rereading.
        catch (DatabaseException ex) when (ex.Error is DatabaseError.UniqueConstraint or DatabaseError.ConcurrentWrite or DatabaseError.Busy)
        {
            return false;
        }
    }
}
