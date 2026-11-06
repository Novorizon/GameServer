using GameServer.Data;
using GameServer.Data.Abstractions;
using GameServer.Gameplay.Dispatching;
using GameServer.Infrastructure.Authentication;
using GameServer.Infrastructure.Storage;
using GameServer.Protocol.Framing;
using Xunit;

namespace GameServer.IntegrationTests;

public sealed class LoginSaveDatabaseTests
{
    [Theory]
    [InlineData(DatabaseProvider.Sqlite)] [InlineData(DatabaseProvider.MySql)]
    public async Task CompareAndSwapAndContentBoundReceiptsSurviveNewConnections(DatabaseProvider provider)
    {
        await using var fixture = await Fixture.CreateAsync(provider);
        var store = fixture.Store();
        Assert.Null((await store.ReadVersionedAsync(fixture.PlayerId, default)).Data);
        Assert.Equal(1, await store.WriteVersionedAsync(fixture.PlayerId, new byte[] { 1 }, 0, "first", default));
        Assert.Equal(2, await store.WriteVersionedAsync(fixture.PlayerId, new byte[] { 2 }, 1, "next", default));
        await using var reopened = DatabaseFactory.Create(fixture.Options);
        var durable = new DatabasePlayerSaveStore(reopened, new() { MaxSaveBytes = 64 });
        Assert.Equal(1, await durable.WriteVersionedAsync(fixture.PlayerId, new byte[] { 1 }, 0, "first", default));
        var state = await durable.ReadVersionedAsync(fixture.PlayerId, default);
        Assert.Equal(2, state.Version); Assert.Equal(new byte[] { 2 }, state.Data);
        await FailureAsync(ResultCode.Conflict, () => durable.WriteVersionedAsync(fixture.PlayerId, new byte[] { 9 }, 0, "first", default).AsTask());
        await FailureAsync(ResultCode.Conflict, () => durable.WriteVersionedAsync(fixture.PlayerId, new byte[] { 1 }, 1, "first", default).AsTask());
        await FailureAsync(ResultCode.Conflict, () => durable.WriteVersionedAsync(fixture.PlayerId, new byte[] { 3 }, 1, "stale", default).AsTask());
        Assert.Equal(1, await durable.WriteVersionedAsync(fixture.PlayerId + 1, new byte[] { 8 }, 0, "first", default));
        Assert.Equal(new byte[] { 2 }, await durable.ReadAsync(fixture.PlayerId, default));
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)] [InlineData(DatabaseProvider.MySql)]
    public async Task ConcurrentStoresCommitOneVersionAndReplayOnlyOneOperation(DatabaseProvider provider)
    {
        await using var fixture = await Fixture.CreateAsync(provider);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async i =>
        {
            try { await fixture.Store().WriteVersionedAsync(fixture.PlayerId, new byte[] { (byte)i }, 0, "create-" + i, default); return true; }
            catch (RequestFailureException ex) when (ex.Result == ResultCode.Conflict) { return false; }
        }));
        Assert.Single(results, x => x);
        Assert.Equal(1, (await fixture.Store().ReadVersionedAsync(fixture.PlayerId, default)).Version);
        var replays = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            fixture.Store().WriteVersionedAsync(fixture.PlayerId, new byte[] { 99 }, 1, "same-update", default).AsTask()));
        Assert.All(replays, version => Assert.Equal(2, version));
        Assert.Equal(new byte[] { 99 }, (await fixture.Store().ReadVersionedAsync(fixture.PlayerId, default)).Data);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)] [InlineData(DatabaseProvider.MySql)]
    public async Task InvalidOrCanceledWritesDoNotCreatePayloadsOrReceipts(DatabaseProvider provider)
    {
        await using var fixture = await Fixture.CreateAsync(provider);
        var store = fixture.Store();
        await FailureAsync(ResultCode.InvalidRequest, () => store.WriteAsync(fixture.PlayerId, new byte[] { 1 }, default).AsTask());
        await FailureAsync(ResultCode.InvalidRequest, () => store.WriteVersionedAsync(fixture.PlayerId, new byte[65], 0, "too-large", default).AsTask());
        await FailureAsync(ResultCode.InvalidRequest, () => store.WriteVersionedAsync(fixture.PlayerId, new byte[1], -1, "negative", default).AsTask());
        await FailureAsync(ResultCode.InvalidRequest, () => store.WriteVersionedAsync(fixture.PlayerId, new byte[1], long.MaxValue, "overflow", default).AsTask());
        await FailureAsync(ResultCode.InvalidRequest, () => store.WriteVersionedAsync(fixture.PlayerId, new byte[1], 0, "", default).AsTask());
        await FailureAsync(ResultCode.InvalidRequest, () => store.WriteVersionedAsync(fixture.PlayerId, new byte[1], 0, "bad/id", default).AsTask());
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.WriteVersionedAsync(fixture.PlayerId, new byte[1], 0, "cancel", canceled.Token).AsTask());
        Assert.Null((await store.ReadVersionedAsync(fixture.PlayerId, default)).Data);
        Assert.Equal(1, await store.WriteVersionedAsync(fixture.PlayerId, Array.Empty<byte>(), 0, "cancel", default));
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)] [InlineData(DatabaseProvider.MySql)]
    public async Task TrustedProvisioningIsIdempotentAndNeverReassignsAnAccount(DatabaseProvider provider)
    {
        await using var fixture = await Fixture.CreateAsync(provider);
        var provisioner = new TrustedAccountProvisioner(fixture.Database);
        string device = "operator-" + fixture.PlayerId;
        var account = await provisioner.ProvisionAsync(fixture.PlayerId, device, default);
        Assert.Equal(account, await provisioner.ProvisionAsync(fixture.PlayerId, device, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => provisioner.ProvisionAsync(fixture.PlayerId, device + "-other", default).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => provisioner.ProvisionAsync(fixture.PlayerId + 1, device, default).AsTask());
        var verifier = new JwtLoginTokenVerifier(new()
        { SigningKeyBase64 = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)) });
        var identity = await verifier.VerifyAsync(verifier.Issue(account.PlayerId, account.DeviceId), default);
        Assert.Equal(account.PlayerId, identity!.PlayerId); Assert.Equal(device, identity.DeviceId);
    }

    private static async Task FailureAsync(ResultCode code, Func<Task> action) =>
        Assert.Equal(code, (await Assert.ThrowsAsync<RequestFailureException>(action)).Result);

    private sealed class Fixture : IAsyncDisposable
    {
        public required IDatabase Database { get; init; }
        public required DatabaseConnectionOptions Options { get; init; }
        public required string Directory { get; init; }
        public long PlayerId { get; } = Random.Shared.NextInt64(10_000_000, 1_000_000_000);
        public DatabasePlayerSaveStore Store() => new(Database, new() { MaxSaveBytes = 64 });
        public static async Task<Fixture> CreateAsync(DatabaseProvider provider)
        {
            string directory = Path.Combine(Path.GetTempPath(), "login-save-" + Guid.NewGuid());
            var options = new DatabaseConnectionOptions { Provider = provider,
                ConnectionString = provider == DatabaseProvider.Sqlite ? "Data Source=" + Path.Combine(directory, "game.db") : TestServer.ConnectionString };
            var database = DatabaseFactory.Create(options);
            string file = provider == DatabaseProvider.Sqlite ? "sqlite-schema.sql" : "mysql-new-database.sql";
            try
            {
                await database.ExecuteAsync(new(await File.ReadAllTextAsync(Path.GetFullPath("../../../../../sql/" + file, AppContext.BaseDirectory))));
                return new() { Database = database, Options = options, Directory = directory };
            }
            catch { await database.DisposeAsync(); throw; }
        }
        public async ValueTask DisposeAsync()
        {
            foreach (string table in new[] { "tcp_player_save_receipt", "tcp_player_save", "account" })
                await Database.ExecuteAsync(new($"DELETE FROM {table} WHERE {(table == "account" ? "id" : "player_id")} IN (@first,@second)", ("first", PlayerId), ("second", PlayerId + 1)));
            await Database.DisposeAsync();
            if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, recursive: true);
        }
    }
}
