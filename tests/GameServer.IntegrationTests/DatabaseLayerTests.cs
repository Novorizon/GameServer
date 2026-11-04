using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using GameServer.Data;
using GameServer.Data.Abstractions;
using GameServer.Data.Tables;
using Xunit;

namespace GameServer.IntegrationTests;

public sealed class DatabaseLayerTests
{
    [Theory]
    [InlineData(DatabaseProvider.MySql)] [InlineData(DatabaseProvider.Sqlite)]
    public async Task CommonCrudMapsColumnsAndProtectsValuesAndIdentifiers(DatabaseProvider provider)
    {
        await using var fixture = await Fixture.CreateAsync(provider);
        var table = new TableRepository<InventoryRow, long>(fixture.Database);
        Assert.Equal(0, await table.CountAsync());
        var first = new InventoryRow { PlayerId = 7, ItemCode = "x' OR 1=1 --", Quantity = 10, Enabled = true, Payload = new byte[] { 1, 2 } };
        long id = await table.InsertAsync(first); Assert.True(id > 0); Assert.Equal(id, first.Id);
        var second = new InventoryRow { PlayerId = 7, ItemCode = "second", Quantity = 2, Quality = 3 };
        await table.InsertAsync(second);
        await table.InsertAsync(new() { PlayerId = 8, ItemCode = "third", Quantity = 1 });
        var row = await table.GetAsync(id); Assert.NotNull(row);
        Assert.True(row.Enabled); Assert.Null(row.Quality); Assert.Equal(new byte[] { 1, 2 }, row.Payload);
        Assert.Equal(2, (await table.FindByAsync(nameof(InventoryRow.PlayerId), 7L)).Count);
        Assert.Single(await table.FindByAsync(nameof(InventoryRow.ItemCode), "x' OR 1=1 --"));
        Assert.Equal(2, (await table.FindByAsync(nameof(InventoryRow.Quality), null)).Count);
        Assert.Equal(second.Id, Assert.Single(await table.ListAsync(limit: 1, offset: 1)).Id);
        row.Quantity = 5; Assert.True(await table.UpdateAsync(row)); Assert.True(await table.UpdateAsync(row));
        Assert.Equal(5, (await table.GetAsync(id))!.Quantity);
        Assert.Equal(3, await table.CountAsync());
        await Assert.ThrowsAsync<ArgumentException>(() => table.FindByAsync("ItemCode; DROP TABLE x", "x").AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => table.ListAsync(limit: 1001).AsTask());
        Assert.True(await table.DeleteAsync(second.Id)); Assert.False(await table.DeleteAsync(second.Id));
        Assert.Null(await table.GetAsync(second.Id));
    }

    [Theory]
    [InlineData(DatabaseProvider.MySql)] [InlineData(DatabaseProvider.Sqlite)]
    public async Task TransactionCommitsOrRollsBackBothTablesAndSessionCannotEscape(DatabaseProvider provider)
    {
        await using var fixture = await Fixture.CreateAsync(provider);
        var items = new TableRepository<InventoryRow, long>(fixture.Database);
        var settings = new TableRepository<SettingsRow, long>(fixture.Database);
        var item = new InventoryRow { PlayerId = 7, ItemCode = "tx", Quantity = 1 };
        await items.InsertAsync(item); await settings.InsertAsync(new() { PlayerId = 7, Language = "en" });
        IDatabaseSession? escaped = null;
        await fixture.Database.InTransactionAsync(async (session, ct) =>
        {
            escaped = session;
            var a = new TableRepository<InventoryRow, long>(session);
            var b = new TableRepository<SettingsRow, long>(session);
            item.Quantity = 2; await a.UpdateAsync(item, ct);
            await b.UpdateAsync(new() { PlayerId = 7, Language = "zh" }, ct);
            // The wrapper serializes commands sharing this transaction connection.
            await Task.WhenAll(a.GetAsync(item.Id, ct).AsTask(), b.GetAsync(7, ct).AsTask());
            return true;
        });
        Assert.Equal(2, (await items.GetAsync(item.Id))!.Quantity);
        Assert.Equal("zh", (await settings.GetAsync(7))!.Language);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Database.InTransactionAsync<bool>(async (session, ct) =>
        {
            item.Quantity = 99;
            await new TableRepository<InventoryRow, long>(session).UpdateAsync(item, ct);
            await new TableRepository<SettingsRow, long>(session).UpdateAsync(new() { PlayerId = 7, Language = "bad" }, ct);
            throw new InvalidOperationException("rollback");
        }).AsTask());
        Assert.Equal(2, (await items.GetAsync(item.Id))!.Quantity);
        Assert.Equal("zh", (await settings.GetAsync(7))!.Language);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => escaped!.ScalarAsync(new("SELECT 1")).AsTask());
    }

    [Theory]
    [InlineData(DatabaseProvider.MySql)] [InlineData(DatabaseProvider.Sqlite)]
    public async Task UniqueConstraintHasTheSamePortableErrorAndConnectionRecovers(DatabaseProvider provider)
    {
        await using var fixture = await Fixture.CreateAsync(provider);
        var table = new TableRepository<InventoryRow, long>(fixture.Database);
        await table.InsertAsync(new() { PlayerId = 7, ItemCode = "unique", Quantity = 1 });
        var error = await Assert.ThrowsAsync<DatabaseException>(() => table.InsertAsync(new()
        { PlayerId = 7, ItemCode = "unique", Quantity = 2 }).AsTask());
        Assert.Equal(DatabaseError.UniqueConstraint, error.Error);
        Assert.Equal(1, await table.CountAsync());
    }

    [Theory]
    [InlineData(DatabaseProvider.MySql)] [InlineData(DatabaseProvider.Sqlite)]
    public async Task ConditionalUpdatesPreventConcurrentOverConsumption(DatabaseProvider provider)
    {
        await using var fixture = await Fixture.CreateAsync(provider);
        var table = new InventoryTable(fixture.Database);
        long id = await table.InsertAsync(new() { PlayerId = 7, ItemCode = "consume", Quantity = 10 });
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => table.ConsumeAsync(id).AsTask()));
        Assert.Equal(10, results.Count(x => x)); Assert.Equal(0, (await table.GetAsync(id))!.Quantity);
    }

    [Theory]
    [InlineData(DatabaseProvider.MySql)] [InlineData(DatabaseProvider.Sqlite)]
    public async Task CancellationInterruptsWorkAndSubsequentCommandsStillSucceed(DatabaseProvider provider)
    {
        await using var fixture = await Fixture.CreateAsync(provider);
        string sql = provider == DatabaseProvider.MySql ? "SELECT SLEEP(10)" :
            "WITH RECURSIVE n(x) AS (VALUES(1) UNION ALL SELECT x+1 FROM n WHERE x<100000000) SELECT SUM(x) FROM n";
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Database.ScalarAsync(new(sql), cancellation.Token).AsTask());
        Assert.Equal(1, Convert.ToInt32(await fixture.Database.ScalarAsync(new("SELECT 1")), CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(DatabaseProvider.MySql)] [InlineData(DatabaseProvider.Sqlite)]
    public async Task AdmissionRejectsExcessOperationsWithoutAnUnboundedQueue(DatabaseProvider provider)
    {
        await using var fixture = await Fixture.CreateAsync(provider, concurrency: 1, queue: 0);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holding = fixture.Database.WithSessionAsync(async (_, ct) => { entered.SetResult(); await release.Task.WaitAsync(ct); return 1; }).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        try
        {
            var busy = await Assert.ThrowsAsync<DatabaseException>(() => fixture.Database.ScalarAsync(new("SELECT 1")).AsTask());
            Assert.Equal(DatabaseError.Busy, busy.Error);
        }
        finally { release.TrySetResult(); await holding; }
        Assert.NotNull(await fixture.Database.ScalarAsync(new("SELECT 1")));
    }

    [Theory]
    [InlineData(DatabaseProvider.MySql)] [InlineData(DatabaseProvider.Sqlite)]
    public async Task QueryRowLimitDoesNotLeakTheConnection(DatabaseProvider provider)
    {
        await using var fixture = await Fixture.CreateAsync(provider);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Database.QueryAsync(
            new("SELECT 1 AS value UNION ALL SELECT 2"), r => r.GetInt32(0), maxRows: 1).AsTask());
        Assert.NotNull(await fixture.Database.ScalarAsync(new("SELECT 1")));
    }

    [Theory]
    [InlineData(DatabaseProvider.MySql)] [InlineData(DatabaseProvider.Sqlite)]
    public async Task CommandsInsideOneSessionAlsoHaveABoundedQueue(DatabaseProvider provider)
    {
        await using var fixture = await Fixture.CreateAsync(provider, concurrency: 1, queue: 0);
        await fixture.Database.WithSessionAsync(async (session, token) =>
        {
            using var cancellation = new CancellationTokenSource();
            string sql = provider == DatabaseProvider.MySql ? "SELECT SLEEP(10)" :
                "WITH RECURSIVE n(x) AS (VALUES(1) UNION ALL SELECT x+1 FROM n WHERE x<100000000) SELECT SUM(x) FROM n";
            var running = session.ScalarAsync(new(sql), cancellation.Token).AsTask();
            try
            {
                var busy = await Assert.ThrowsAsync<DatabaseException>(() => session.ScalarAsync(new("SELECT 1"), token).AsTask());
                Assert.Equal(DatabaseError.Busy, busy.Error);
            }
            finally { cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running); }
            Assert.NotNull(await session.ScalarAsync(new("SELECT 1"), token));
            return true;
        });
    }

    [Theory]
    [InlineData(DatabaseProvider.MySql)] [InlineData(DatabaseProvider.Sqlite)]
    public async Task SwallowingACommandErrorCannotCommitAPartialTransaction(DatabaseProvider provider)
    {
        await using var fixture = await Fixture.CreateAsync(provider);
        var error = await Assert.ThrowsAsync<DatabaseException>(() => fixture.Database.InTransactionAsync(async (session, ct) =>
        {
            var table = new TableRepository<InventoryRow, long>(session);
            await table.InsertAsync(new() { PlayerId = 7, ItemCode = "same", Quantity = 1 }, ct);
            try { await table.InsertAsync(new() { PlayerId = 7, ItemCode = "same", Quantity = 2 }, ct); }
            catch (DatabaseException duplicate) when (duplicate.Error == DatabaseError.UniqueConstraint) { }
            return true;
        }).AsTask());
        Assert.Equal(DatabaseError.TransactionAborted, error.Error);
        Assert.Equal(0, await new TableRepository<InventoryRow, long>(fixture.Database).CountAsync());
    }

    [Theory]
    [InlineData(DatabaseProvider.MySql)] [InlineData(DatabaseProvider.Sqlite)]
    public async Task DisposalCancelsInflightWorkAndRepeatedDisposalIsSafe(DatabaseProvider provider)
    {
        var database = DatabaseFactory.Create(new()
        { Provider = provider, ConnectionString = provider == DatabaseProvider.MySql ? TestServer.ConnectionString : "Data Source=:memory:" });
        string sql = provider == DatabaseProvider.MySql ? "SELECT SLEEP(10)" :
            "WITH RECURSIVE n(x) AS (VALUES(1) UNION ALL SELECT x+1 FROM n WHERE x<100000000) SELECT SUM(x) FROM n";
        var running = database.ScalarAsync(new(sql)).AsTask();
        await Task.Delay(100);
        var disposal = database.DisposeAsync().AsTask();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        await database.DisposeAsync(); database.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => database.ScalarAsync(new("SELECT 1")).AsTask());
    }

    [Table("gs_test_inventory")]
    public sealed class InventoryRow
    {
        [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)] public long Id { get; set; }
        public long PlayerId { get; set; }
        public string ItemCode { get; set; } = "";
        public int Quantity { get; set; }
        public int? Quality { get; set; }
        [Column("is_enabled")] public bool Enabled { get; set; }
        public byte[]? Payload { get; set; }
    }
    [Table("gs_test_settings")]
    public sealed class SettingsRow
    {
        [Key] public long PlayerId { get; set; }
        public string Language { get; set; } = "en";
    }
    private sealed class InventoryTable(IDatabaseExecutor database) : TableRepository<InventoryRow, long>(database)
    {
        public async ValueTask<bool> ConsumeAsync(long id) => await Database.ExecuteAsync(new(
            $"UPDATE {TableName} SET quantity=quantity-1 WHERE id=@id AND quantity>=1", ("id", id))) == 1;
    }

    private sealed class Fixture(IDatabase database) : IAsyncDisposable
    {
        public IDatabase Database { get; } = database;
        public static async Task<Fixture> CreateAsync(DatabaseProvider provider, int concurrency = 2, int queue = 128)
        {
            var database = DatabaseFactory.Create(new()
            {
                Provider = provider,
                ConnectionString = provider == DatabaseProvider.MySql ? TestServer.ConnectionString : "Data Source=:memory:",
                MaximumPoolSize = 2, MaxConcurrentOperations = concurrency, MaxQueuedOperations = queue
            });
            try
            {
                string identity = provider == DatabaseProvider.MySql ? "BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY" : "INTEGER PRIMARY KEY AUTOINCREMENT";
                await database.ExecuteAsync(new("DROP TABLE IF EXISTS gs_test_inventory; DROP TABLE IF EXISTS gs_test_settings;"));
                await database.ExecuteAsync(new($"CREATE TABLE gs_test_inventory(id {identity},player_id BIGINT NOT NULL,item_code VARCHAR(128) NOT NULL,quantity INT NOT NULL,quality INT NULL,is_enabled BOOLEAN NOT NULL,payload BLOB NULL,UNIQUE(player_id,item_code)); CREATE TABLE gs_test_settings(player_id BIGINT NOT NULL PRIMARY KEY,language VARCHAR(16) NOT NULL);"));
                return new(database);
            }
            catch { await database.DisposeAsync(); throw; }
        }
        public async ValueTask DisposeAsync()
        {
            try { await Database.ExecuteAsync(new("DROP TABLE IF EXISTS gs_test_inventory; DROP TABLE IF EXISTS gs_test_settings;")); }
            finally { await Database.DisposeAsync(); }
        }
    }
}
