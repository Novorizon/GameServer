using GameServer.Data;
using GameServer.SchemaGenerator;
using Microsoft.Data.Sqlite;
using MySqlConnector;

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    if (args.Length == 0 || args.Contains("--help"))
    {
        Console.WriteLine("""
            Usage: GameServer.SchemaGenerator --provider MySql|Sqlite --tables account,inventory
                --namespace GameServer.Infrastructure.Generated --output ./Generated [--force] [--dry-run]
            Connection string: environment variable GAMESERVER_SCHEMA_CONNECTION_STRING (never command-line).
            Reads schema only. SQLite opens an existing file read-only. Custom .cs extensions are preserved.
            """);
        return 0;
    }
    using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    try
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        bool force = false, dryRun = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--force") { force = true; continue; }
            if (args[i] == "--dry-run") { dryRun = true; continue; }
            if (args[i] is not ("--provider" or "--tables" or "--namespace" or "--output") ||
                i + 1 == args.Length || !values.TryAdd(args[i], args[++i]))
                throw new SchemaGenerationException("Unknown, duplicate, or incomplete option. See --help.");
        }
        if (values.Count != 4 || !Enum.TryParse<DatabaseProvider>(values["--provider"], true, out var provider) || !Enum.IsDefined(provider))
            throw new SchemaGenerationException("Specify --provider, --tables, --namespace, and --output. See --help.");
        string connection = Environment.GetEnvironmentVariable("GAMESERVER_SCHEMA_CONNECTION_STRING") ?? "";
        if (string.IsNullOrWhiteSpace(connection)) throw new SchemaGenerationException("Set GAMESERVER_SCHEMA_CONNECTION_STRING first.");
        // Normalize provider switches that affect CLR types and prevent accidental SQLite file creation.
        if (provider == DatabaseProvider.Sqlite)
        {
            var settings = new SqliteConnectionStringBuilder(connection);
            if (settings.Mode == SqliteOpenMode.Memory || settings.DataSource is "" or ":memory:")
                throw new SchemaGenerationException("CLI generation requires an existing SQLite file.");
            settings.Mode = SqliteOpenMode.ReadOnly;
            connection = settings.ConnectionString;
        }
        else
        {
            var settings = new MySqlConnectionStringBuilder(connection) { TreatTinyAsBoolean = true, AllowZeroDateTime = false };
            connection = settings.ConnectionString;
        }
        await using var database = DatabaseFactory.Create(new() { Provider = provider, ConnectionString = connection, MaximumPoolSize = 1 });
        string[] tables = values["--tables"].Split(',', StringSplitOptions.TrimEntries);
        var schema = await SchemaReader.ReadAsync(database, tables, cancellation.Token);
        var files = CodeGenerator.Generate(provider, schema, values["--namespace"]);
        if (!dryRun) GenerationWriter.Write(values["--output"], files, force);
        Console.WriteLine($"{(dryRun ? "Preview" : "Generated")}: {schema.Count} tables, {files.Count} files; database schema/data unchanged.");
        foreach (var file in files) Console.WriteLine(file.Name + (file.Handwritten ? " (custom extension; existing file preserved)" : ""));
        return 0;
    }
    catch (SchemaGenerationException error) { Console.Error.WriteLine(error.Message); return 2; }
    catch (OperationCanceledException) { Console.Error.WriteLine("Schema generation canceled or timed out."); return 3; }
    catch (Exception)
    {
        // Provider exception messages can include connection-string values; never print them from this CLI.
        Console.Error.WriteLine("Schema generation failed. Check database access, metadata permissions, connection options and output permissions. No migration was executed.");
        return 1;
    }
}
