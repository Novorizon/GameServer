using GameServer.Data;
using System.Security.Cryptography;
using GameServer.LoadTests;
using GameServer.Networking.Configuration;
using GameServer.Infrastructure.Persistence.Tables;
using GameServer.Protocol.Framing;
using GameServer.Protocol.Messages;
using Xunit;

namespace GameServer.IntegrationTests;

public sealed class SqliteServerTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task HostCanUseSqliteForRealLoginProgressAndBindingRepositories(bool encrypted)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gameserver-sqlite-" + Guid.NewGuid());
        string connection = "Data Source=" + Path.Combine(directory, "game.db");
        try
        {
            await using var database = DatabaseFactory.Create(new() { Provider = DatabaseProvider.Sqlite, ConnectionString = connection });
            string schema = Path.GetFullPath("../../../../../sql/sqlite-schema.sql", AppContext.BaseDirectory);
            await database.ExecuteAsync(new(await File.ReadAllTextAsync(schema)));
            await new AccountRepository(database).InsertAsync(new() { Id = 8062, DeviceId = "tcp-load-8062" });
            string key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var mode = encrypted ? PacketEncryptionMode.AesGcm : PacketEncryptionMode.None;
            await using var server = await TestServer.StartAsync(new()
            { ["Database:Provider"] = "Sqlite", ["Database:ConnectionString"] = connection,
              ["Networking:Encryption:Mode"] = mode.ToString(), ["Networking:Encryption:KeyBase64"] = key });
            using var socket = await server.ConnectAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var client = await ClientPacketTransport.ConnectAsync(socket.GetStream(), mode, key, timeout.Token);
            await client.ExchangeAsync(TcpIntegrationTests.Encode((int)MessageId.Login, 1, server.Login(8062)), timeout.Token);
            Assert.Equal(ResultCode.Ok, client.Header.ResultCode);
            await client.ExchangeAsync(TcpIntegrationTests.Encode((int)MessageId.ProgressWrite, 2,
                new ProgressWriteRequest { Progress = 42, ExpectedVersion = 0 }), timeout.Token);
            Assert.Equal(1, ProgressResponse.Parser.ParseFrom(client.Body.Span).Version);
            await client.ExchangeAsync(TcpIntegrationTests.Encode((int)MessageId.ProgressRead, 3, new ProgressReadRequest()), timeout.Token);
            Assert.Equal(42, ProgressResponse.Parser.ParseFrom(client.Body.Span).Progress);
            var bindings = new FacebookBindingRepository(database);
            Assert.True(await bindings.TryBindAsync(8062, "sqlite-user", default));
            Assert.True(await bindings.TryBindAsync(8062, "sqlite-user", default));
            Assert.False(await bindings.TryBindAsync(8063, "sqlite-user", default));
            socket.Close(); await server.AssertReleasedAsync();
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
}
