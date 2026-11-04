using GameServer.Data;
using GameServer.DatabaseExample;

DatabaseProvider provider = args.Contains("--mysql") ? DatabaseProvider.MySql : DatabaseProvider.Sqlite;
string? configured = Environment.GetEnvironmentVariable("GAMESERVER_DATABASE_EXAMPLE_CONNECTION_STRING");
string connection = configured ?? (provider == DatabaseProvider.Sqlite ? "Data Source=:memory:" :
    throw new InvalidOperationException("Set a DEVELOPMENT MySQL connection string for the example."));
if (args.Contains("--initialize-schema") && string.IsNullOrEmpty(configured))
    throw new InvalidOperationException("Schema initialization requires an explicit DEVELOPMENT connection string.");
await using var database = DatabaseFactory.Create(new() { Provider = provider, ConnectionString = connection });
if (args.Contains("--initialize-schema"))
{
    string script = provider == DatabaseProvider.Sqlite ? "sqlite-schema.sql" : "mysql-new-database.sql";
    await database.ExecuteAsync(new(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "schema", script))));
    Console.WriteLine($"{provider}: development game schema initialized. Existing tables were not migrated.");
    return;
}
string identity = provider == DatabaseProvider.MySql ? "BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY" : "INTEGER PRIMARY KEY AUTOINCREMENT";
await database.ExecuteAsync(new($"CREATE TABLE IF NOT EXISTS example_inventory(id {identity},player_id BIGINT NOT NULL,item_code VARCHAR(128) NOT NULL,quantity INT NOT NULL,quality INT NULL,UNIQUE(player_id,item_code)); CREATE TABLE IF NOT EXISTS example_player_settings(player_id BIGINT NOT NULL PRIMARY KEY,language VARCHAR(16) NOT NULL,sound_enabled BOOLEAN NOT NULL);"));

long playerId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
var inventory = new InventoryRepository(database);
var settings = new PlayerSettingsRepository(database);
var item = new InventoryItemRow { PlayerId = playerId, ItemCode = "potion", Quantity = 10 };
long id = await inventory.InsertAsync(item);
if ((await inventory.FindItemAsync(playerId, "potion"))?.Id != id) throw new InvalidOperationException("Lookup failed.");
await settings.InsertAsync(new() { PlayerId = playerId, Language = "en" });
item.Quantity = 8; await inventory.UpdateAsync(item);
if (!await inventory.TryConsumeAsync(id, 3)) throw new InvalidOperationException("Consume failed.");

await database.InTransactionAsync(async (session, ct) =>
{
    var itemsInTransaction = new InventoryRepository(session);
    var settingsInTransaction = new PlayerSettingsRepository(session);
    var row = await itemsInTransaction.GetAsync(id, ct) ?? throw new InvalidOperationException("Item missing.");
    row.Quantity = 4; await itemsInTransaction.UpdateAsync(row, ct);
    await settingsInTransaction.UpdateAsync(new() { PlayerId = playerId, Language = "zh", SoundEnabled = false }, ct);
    return true;
});
try
{
    await database.InTransactionAsync<bool>(async (session, ct) =>
    {
        var table = new InventoryRepository(session);
        var row = await table.GetAsync(id, ct) ?? throw new InvalidOperationException("Item missing.");
        row.Quantity = 999; await table.UpdateAsync(row, ct);
        throw new InvalidOperationException("example rollback");
    });
}
catch (InvalidOperationException ex) when (ex.Message == "example rollback") { }
if ((await inventory.GetAsync(id))?.Quantity != 4) throw new InvalidOperationException("Rollback failed.");
Console.WriteLine($"{provider}: CRUD, generated key, parameterized filter, atomic consume, transaction commit and rollback OK.");
await inventory.DeleteAsync(id); await settings.DeleteAsync(playerId);
