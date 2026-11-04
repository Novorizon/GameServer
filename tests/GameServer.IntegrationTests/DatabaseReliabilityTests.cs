using System.Globalization;
using GameServer.Infrastructure.Persistence;
using GameServer.Infrastructure.Persistence.Tables;
using GameServer.Data;
using GameServer.Data.Providers;
using MySqlConnector;
using Xunit;

namespace GameServer.IntegrationTests;

public sealed class DatabaseReliabilityTests
{
    [Fact]
    public async Task CancelledQueryReturnsItsConnectionToASingleSlotPool()
    {
        await using var database = new MySqlDatabase(new()
        { ConnectionString = TestServer.ConnectionString, MaximumPoolSize = 1, MaxConcurrentOperations = 1, CommandTimeoutSeconds = 10 });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => database.ScalarAsync(new("SELECT SLEEP(10)"), cancellation.Token).AsTask());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        Assert.Equal(1, Convert.ToInt32(await database.ScalarAsync(new("SELECT 1"), timeout.Token), CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task CancelledCasWaitingForARowLockDoesNotChangeTheVersion()
    {
        await using var source = new MySqlDataSourceBuilder(TestServer.ConnectionString).Build();
        await using var database = new MySqlDatabase(new() { ConnectionString = TestServer.ConnectionString });
        await using var connection = await source.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM tcp_player_progress WHERE player_id=8170";
        await command.ExecuteNonQueryAsync();
        var repository = new PlayerProgressRepository(database);
        Assert.True(await repository.TryWriteAsync(8170, 10, 0, default));
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            command.Transaction = transaction;
            command.CommandText = "UPDATE tcp_player_progress SET progress=progress WHERE player_id=8170";
            await command.ExecuteNonQueryAsync();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                repository.TryWriteAsync(8170, 99, 1, cancellation.Token).AsTask());
            await transaction.RollbackAsync();
            command.Transaction = null;
        }
        var unchanged = await repository.ReadAsync(8170, default);
        Assert.Equal(10, unchanged.Progress); Assert.Equal(1, unchanged.Version);
        Assert.True(await repository.TryWriteAsync(8170, 20, 1, default));
        Assert.Equal(2, (await repository.ReadAsync(8170, default)).Version);
    }

    [Fact]
    public async Task ConcurrentBindingIsIdempotentAndOneExternalAccountHasOneOwner()
    {
        await using var source = new MySqlDataSourceBuilder(TestServer.ConnectionString).Build();
        await using var database = new MySqlDatabase(new() { ConnectionString = TestServer.ConnectionString });
        await using var connection = await source.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM tcp_facebook_binding WHERE player_id IN (8171,8172)";
        await command.ExecuteNonQueryAsync();
        var repository = new FacebookBindingRepository(database);
        string userId = "audit-" + Guid.NewGuid().ToString("N");
        var repeated = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => repository.TryBindAsync(8171, userId, default).AsTask()));
        Assert.All(repeated, value => Assert.True(value));
        Assert.False(await repository.TryBindAsync(8172, userId, default));
        Assert.False(await repository.TryBindAsync(8171, userId + "-other", default));
        command.CommandText = "DELETE FROM tcp_facebook_binding WHERE player_id IN (8171,8172)";
        await command.ExecuteNonQueryAsync();
        var competing = await Task.WhenAll(repository.TryBindAsync(8171, userId, default).AsTask(),
            repository.TryBindAsync(8172, userId, default).AsTask());
        Assert.Equal(1, competing.Count(x => x));
    }
}
