using System.Diagnostics;
using System.Reflection;
using System.Security;
using GameServer.Data;
using GameServer.Data.Abstractions;
using GameServer.SchemaGenerator;
using Xunit;

namespace GameServer.IntegrationTests;

public sealed class SchemaGeneratorTests
{
    [Fact]
    public async Task GeneratedCodeCompilesAndRunsCrudAgainstBothProviders()
    {
        string root = Path.Combine(Path.GetTempPath(), "tcp-codegen-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var databases = new List<IDatabase>();
        try
        {
            foreach (var provider in new[] { DatabaseProvider.MySql, DatabaseProvider.Sqlite })
            {
                var database = DatabaseFactory.Create(new()
                {
                    Provider = provider, ConnectionString = provider == DatabaseProvider.MySql
                        ? TestServer.ConnectionString : "Data Source=:memory:"
                });
                databases.Add(database);
                await database.ExecuteAsync(new("DROP TABLE IF EXISTS gs_codegen_inventory"));
                await database.ExecuteAsync(new(provider == DatabaseProvider.MySql ? """
                    CREATE TABLE gs_codegen_inventory (
                        id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                        player_id BIGINT NOT NULL, item_code VARCHAR(80) NOT NULL,
                        quantity INT NOT NULL, quality INT NULL, enabled BOOLEAN NOT NULL,
                        payload BLOB NULL, get_type TEXT NULL,
                        doubled BIGINT GENERATED ALWAYS AS (quantity * 2) STORED)
                    """ : """
                    CREATE TABLE gs_codegen_inventory (
                        id INTEGER PRIMARY KEY, player_id INTEGER NOT NULL, item_code TEXT NOT NULL,
                        quantity INTEGER NOT NULL, quality INTEGER NULL, enabled BOOLEAN NOT NULL,
                        payload BLOB NULL, get_type TEXT NULL,
                        doubled INTEGER GENERATED ALWAYS AS (quantity * 2) STORED)
                    """));
                var tables = await SchemaReader.ReadAsync(database, ["gs_codegen_inventory"]);
                Assert.True(tables[0].Columns.Single(c => c.Name == "id").Identity);
                Assert.True(tables[0].Columns.Single(c => c.Name == "doubled").Computed);
                var files = CodeGenerator.Generate(provider, tables, "Generated." + provider);
                Assert.Contains("GetTypeValue", files.Single(f => f.Name.EndsWith("Row.g.cs", StringComparison.Ordinal)).Content);
                GenerationWriter.Write(Path.Combine(root, provider.ToString()), files);
            }
            string dataProject = Path.Combine(RepositoryRoot(), "src/GameServer.Data/GameServer.Data.csproj");
            await File.WriteAllTextAsync(Path.Combine(root, "GeneratedProbe.csproj"), $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                <PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup>
                <ItemGroup><ProjectReference Include="{{SecurityElement.Escape(dataProject)}}" /></ItemGroup>
                </Project>
                """);
            var process = new ProcessStartInfo(Dotnet()) { WorkingDirectory = root, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (string arg in new[] { "build", "GeneratedProbe.csproj", "-c", "Release", "-m:1", "--disable-build-servers", "-tl:off", "-p:NuGetAudit=false", "--ignore-failed-sources" })
                process.ArgumentList.Add(arg);
            using var compiler = Process.Start(process)!;
            Task<string> stdout = compiler.StandardOutput.ReadToEndAsync(), stderr = compiler.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            try { await compiler.WaitForExitAsync(timeout.Token); }
            catch { compiler.Kill(entireProcessTree: true); throw; }
            Assert.True(compiler.ExitCode == 0, await stdout + await stderr);
            var assembly = Assembly.LoadFrom(Path.Combine(root, "bin/Release/net10.0/GeneratedProbe.dll"));
            foreach (var database in databases)
            {
                string ns = "Generated." + database.Provider;
                dynamic row = Activator.CreateInstance(assembly.GetType(ns + ".GsCodegenInventoryRow")!)!;
                row.PlayerId = 7L; row.ItemCode = "x' OR 1=1 --"; row.Quantity = 3;
                row.Enabled = true; row.Payload = new byte[] { 1, 2 };
                dynamic repository = Activator.CreateInstance(assembly.GetType(ns + ".GsCodegenInventoryRepository")!, database)!;
                long id = await repository.InsertAsync(row, CancellationToken.None);
                Assert.True(id > 0);
                dynamic loaded = (await repository.GetAsync(id, CancellationToken.None))!;
                Assert.Equal("x' OR 1=1 --", (string)loaded.ItemCode);
                Assert.True((bool)loaded.Enabled); Assert.Null((object?)loaded.Quality);
                Assert.Equal(new byte[] { 1, 2 }, (byte[])loaded.Payload);
                Assert.Equal(6, Convert.ToInt64(loaded.Doubled));
                loaded.Quantity = 4;
                Assert.True((bool)await repository.UpdateAsync(loaded, CancellationToken.None));
                loaded = (await repository.GetAsync(id, CancellationToken.None))!;
                Assert.Equal(8, Convert.ToInt64(loaded.Doubled));
                Assert.True((bool)await repository.DeleteAsync(id, CancellationToken.None));
                Assert.Null((object?)await repository.GetAsync(id, CancellationToken.None));
            }
        }
        finally
        {
            foreach (var database in databases)
            {
                try { await database.ExecuteAsync(new("DROP TABLE IF EXISTS gs_codegen_inventory")); }
                finally { await database.DisposeAsync(); }
            }
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void RegenerationPreservesCustomCodeAndRefusesUnownedFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), "tcp-codegen-write-" + Guid.NewGuid().ToString("N"));
        var original = CodeGenerator.Generate(DatabaseProvider.Sqlite, [SimpleTable()], "Generated.Tables");
        var changed = CodeGenerator.Generate(DatabaseProvider.Sqlite,
            [SimpleTable() with { Columns = [.. SimpleTable().Columns, new("nickname", "TEXT", true, false)] }], "Generated.Tables");
        try
        {
            GenerationWriter.Write(root, original);
            string custom = Path.Combine(root, "ItemsRepository.cs");
            File.WriteAllText(custom, "// user code");
            GenerationWriter.Write(root, original); // Idempotent without --force.
            Assert.Throws<SchemaGenerationException>(() => GenerationWriter.Write(root, changed));
            Assert.DoesNotContain("Nickname", File.ReadAllText(Path.Combine(root, "ItemsRow.g.cs")));
            GenerationWriter.Write(root, changed, force: true);
            Assert.Contains("string? Nickname", File.ReadAllText(Path.Combine(root, "ItemsRow.g.cs")));
            Assert.Equal("// user code", File.ReadAllText(custom));
            string generatedPath = Path.Combine(root, "ItemsRow.g.cs");
            File.WriteAllText(generatedPath, File.ReadAllText(generatedPath).Replace("\n", "\r\n", StringComparison.Ordinal));
            GenerationWriter.Write(root, changed, force: true); // Recognize generated files after an editor changes line endings.
            Assert.DoesNotContain("\r", File.ReadAllText(generatedPath));
            File.WriteAllText(Path.Combine(root, "ItemsRow.g.cs"), "// unrelated file");
            Assert.Throws<SchemaGenerationException>(() => GenerationWriter.Write(root, original, force: true));
            Assert.Equal("// unrelated file", File.ReadAllText(Path.Combine(root, "ItemsRow.g.cs")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("no-key")] [InlineData("composite")] [InlineData("nullable-key")]
    [InlineData("unsupported-type")] [InlineData("property-collision")]
    [InlineData("unsafe-column")] [InlineData("unsigned-identity")]
    public void UnsupportedSchemasFailBeforeEmittingMisleadingRepositories(string scenario)
    {
        ColumnSchema key = new("id", "BIGINT", false, true);
        ColumnSchema[] columns = scenario switch
        {
            "no-key" => [key with { PrimaryKey = false }],
            "composite" => [key, new("other", "BIGINT", false, true)],
            "nullable-key" => [key with { Nullable = true }],
            "unsupported-type" => [key, new("shape", "geometry", false, false)],
            "property-collision" => [key, new("item_code", "TEXT", true, false), new("ItemCode", "TEXT", true, false)],
            "unsafe-column" => [key, new("name\n", "TEXT", true, false)],
            "unsigned-identity" => [key with { StoreType = "BIGINT UNSIGNED", Identity = true }],
            _ => throw new InvalidOperationException()
        };
        Assert.Throws<SchemaGenerationException>(() => CodeGenerator.Generate(DatabaseProvider.MySql, [new("items", columns)], "Generated.Tables"));
    }

    [Fact]
    public async Task SqliteIdentityDetectionHandlesWithoutRowIdAndDescendingPrimaryKey()
    {
        await using var database = DatabaseFactory.Create(new() { Provider = DatabaseProvider.Sqlite, ConnectionString = "Data Source=:memory:" });
        await database.ExecuteAsync(new("CREATE TABLE normal(id INTEGER PRIMARY KEY, label TEXT)"));
        await database.ExecuteAsync(new("CREATE TABLE descending(id INTEGER PRIMARY KEY DESC NOT NULL, label TEXT)"));
        await database.ExecuteAsync(new("CREATE TABLE no_rowid(id INTEGER PRIMARY KEY, label TEXT) WITHOUT ROWID"));
        var tables = await SchemaReader.ReadAsync(database, ["normal", "descending", "no_rowid"]);
        Assert.True(tables[0].Columns[0].Identity);
        Assert.False(tables[1].Columns[0].Identity);
        Assert.False(tables[2].Columns[0].Identity);
        Assert.False(tables[2].Columns[0].Nullable);
    }

    [Fact]
    public async Task MetadataSelectionRejectsViewsDuplicateAndInvalidNames()
    {
        await using var database = DatabaseFactory.Create(new() { Provider = DatabaseProvider.Sqlite, ConnectionString = "Data Source=:memory:" });
        await database.ExecuteAsync(new("CREATE VIEW sample AS SELECT 1 AS id"));
        await Assert.ThrowsAsync<SchemaGenerationException>(() => SchemaReader.ReadAsync(database, ["sample"]));
        await Assert.ThrowsAsync<SchemaGenerationException>(() => SchemaReader.ReadAsync(database, ["sample", "sample"]));
        await Assert.ThrowsAsync<SchemaGenerationException>(() => SchemaReader.ReadAsync(database, ["sample;DROP TABLE sample"]));
    }

    [Fact]
    public void TablesWithCollidingClassNamesAreRejectedTogether()
    {
        Assert.Throws<SchemaGenerationException>(() => CodeGenerator.Generate(DatabaseProvider.Sqlite,
            [SimpleTable() with { Name = "some_table" }, SimpleTable() with { Name = "SomeTable" }], "Generated.Tables"));
    }

    private static TableSchema SimpleTable() => new("items", [new("id", "INTEGER", false, true, Identity: true)]);
    private static string Dotnet() => Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ??
        (Environment.GetEnvironmentVariable("DOTNET_ROOT") is string root ? Path.Combine(root, "dotnet") : "dotnet");
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "TcpGameServer.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Cannot find repository root.");
    }
}
