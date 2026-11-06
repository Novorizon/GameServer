using System.Buffers.Binary;
using System.Security.Cryptography;
using GameServer.Data;
using GameServer.Data.Abstractions;
using GameServer.Gameplay.Dispatching;
using GameServer.Gameplay.Modules.PlayerSave;
using GameServer.Protocol.Framing;

namespace GameServer.Infrastructure.Storage;

public sealed class DatabasePlayerSaveStore : IVersionedPlayerSaveStore
{
    private readonly IDatabase _database;
    private readonly int _maxSaveBytes;

    public DatabasePlayerSaveStore(IDatabase database, StorageOptions options)
    {
        options.Validate();
        _database = database;
        _maxSaveBytes = options.MaxSaveBytes;
    }

    public async ValueTask<byte[]?> ReadAsync(long playerId, CancellationToken ct) =>
        (await ReadVersionedAsync(playerId, ct)).Data;

    // An unversioned write must never silently bypass database compare-and-swap.
    public ValueTask WriteAsync(long playerId, ReadOnlyMemory<byte> data, CancellationToken ct) =>
        ValueTask.FromException(new RequestFailureException(ResultCode.InvalidRequest));

    public async ValueTask<PlayerSaveSnapshot> ReadVersionedAsync(long playerId, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(playerId);
        var rows = await _database.QueryAsync(new SqlStatement(
            "SELECT version,LENGTH(data),data FROM tcp_player_save WHERE player_id=@player", ("player", playerId)), r =>
        {
            long version = r.GetInt64(0);
            if (version < 1 || r.GetInt64(1) > _maxSaveBytes)
                throw new RequestFailureException(ResultCode.InvalidRequest);
            return new PlayerSaveSnapshot(r.GetFieldValue<byte[]>(2), version);
        }, maxRows: 1, ct: ct);
        return rows.Count == 0 ? new(null, 0) : rows[0];
    }

    public async ValueTask<long> WriteVersionedAsync(long playerId, ReadOnlyMemory<byte> data,
        long expectedVersion, string operationId, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(playerId);
        if (data.Length > _maxSaveBytes || expectedVersion is < 0 or long.MaxValue ||
            string.IsNullOrEmpty(operationId) || operationId.Length > 64 ||
            operationId.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            throw new RequestFailureException(ResultCode.InvalidRequest);
        // Freeze caller-owned memory before hashing or awaiting database work.
        byte[] payload = data.ToArray();
        byte[] versionBytes = new byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(versionBytes, expectedVersion);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(versionBytes);
        hash.AppendData(payload);
        string requestHash = Convert.ToHexString(hash.GetHashAndReset());

        // Database constraints/transactions, rather than process-local locks,
        // serialize writers across sessions, restarts, and server instances.
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await _database.InTransactionAsync(async (transaction, token) =>
                {
                    long? replay = await FindReceiptAsync(transaction, playerId, operationId, requestHash, token);
                    if (replay.HasValue) return replay.Value;
                    long committedVersion = expectedVersion + 1;
                    if (expectedVersion == 0)
                    {
                        await transaction.ExecuteAsync(new("INSERT INTO tcp_player_save(player_id,data,version) VALUES(@player,@data,1)",
                            ("player", playerId), ("data", payload)), token);
                    }
                    else
                    {
                        int changed = await transaction.ExecuteAsync(new(
                            "UPDATE tcp_player_save SET data=@data,version=@next WHERE player_id=@player AND version=@expected",
                            ("data", payload), ("next", committedVersion), ("player", playerId), ("expected", expectedVersion)), token);
                        if (changed != 1) throw new RequestFailureException(ResultCode.Conflict);
                    }
                    await transaction.ExecuteAsync(new(
                        "INSERT INTO tcp_player_save_receipt(player_id,operation_id,request_hash,committed_version) VALUES(@player,@operation,@hash,@version)",
                        ("player", playerId), ("operation", operationId), ("hash", requestHash), ("version", committedVersion)), token);
                    return committedVersion;
                }, ct: ct);
            }
            catch (DatabaseException ex) when (ex.Error is DatabaseError.UniqueConstraint or DatabaseError.ConcurrentWrite)
            {
                // The failed transaction has rolled back before reading a winner's receipt.
                long? replay = await FindReceiptAsync(_database, playerId, operationId, requestHash, ct);
                if (replay.HasValue) return replay.Value;
                if (ex.Error == DatabaseError.ConcurrentWrite && attempt < 2) continue;
                throw new RequestFailureException(ResultCode.Conflict);
            }
        }
    }

    private static async ValueTask<long?> FindReceiptAsync(IDatabaseExecutor database, long playerId,
        string operationId, string requestHash, CancellationToken ct)
    {
        var rows = await database.QueryAsync(new SqlStatement(
            "SELECT request_hash,committed_version FROM tcp_player_save_receipt WHERE player_id=@player AND operation_id=@operation",
            ("player", playerId), ("operation", operationId)), r => (Hash: r.GetString(0), Version: r.GetInt64(1)), maxRows: 1, ct: ct);
        if (rows.Count == 0) return null;
        if (!string.Equals(rows[0].Hash, requestHash, StringComparison.Ordinal))
            throw new RequestFailureException(ResultCode.Conflict);
        return rows[0].Version;
    }
}
