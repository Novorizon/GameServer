namespace GameServer.Data;

public class DatabaseConnectionOptions
{
    public DatabaseProvider Provider { get; set; } = DatabaseProvider.MySql;
    public string ConnectionString { get; set; } = "";
    public uint MaximumPoolSize { get; set; } = 64;
    public uint ConnectionTimeoutSeconds { get; set; } = 3;
    public uint CommandTimeoutSeconds { get; set; } = 3;
    // Zero selects a provider default: MySQL pool size, SQLite four workers.
    public int MaxConcurrentOperations { get; set; }
    public int MaxQueuedOperations { get; set; } = 128;

    public virtual void Validate()
    {
        if (!Enum.IsDefined(Provider) || string.IsNullOrWhiteSpace(ConnectionString) ||
            MaximumPoolSize is < 1 or > 1024 || ConnectionTimeoutSeconds is < 1 or > 30 ||
            CommandTimeoutSeconds is < 1 or > 60 || MaxConcurrentOperations is < 0 or > 1024 ||
            MaxQueuedOperations is < 0 or > 100000)
            throw new InvalidOperationException("Invalid database connection options.");
    }
}
