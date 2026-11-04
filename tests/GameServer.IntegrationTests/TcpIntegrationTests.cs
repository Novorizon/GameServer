using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using GameServer.Host.Hosting;
using GameServer.Infrastructure.Authentication;
using GameServer.Infrastructure.Persistence;
using GameServer.Infrastructure.Persistence.Tables;
using GameServer.Data;
using GameServer.Data.Providers;
using GameServer.Infrastructure.Storage;
using GameServer.Gameplay.Diagnostics;
using GameServer.Gameplay.Sessions;
using GameServer.Networking.Diagnostics;
using GameServer.Networking.Memory;
using GameServer.Protocol.Framing;
using GameServer.Protocol.Messages;
using Google.Protobuf;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MySqlConnector;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace GameServer.IntegrationTests;

public sealed class TcpIntegrationTests
{
    [Fact]
    public async Task SuccessfulHeartbeatRefreshesIdleDeadlineButInvalidHeartbeatDoesNot()
    {
        await using var server = await TestServer.StartAsync(new() { ["Gameplay:IdleTimeoutSeconds"] = "1" });
        using var client = await server.ConnectAsync();
        Assert.Equal(ResultCode.Ok, (await RequestAsync(client.GetStream(), MessageId.Login, server.Login(8011))).Header.ResultCode);
        await Task.Delay(600);
        Assert.Equal(ResultCode.Ok, (await RequestAsync(client.GetStream(), MessageId.Heartbeat, null)).Header.ResultCode);
        await Task.Delay(600);
        byte[] invalid = new byte[29];
        PacketCodec.WriteHeader(invalid, new(1, 7, 0, 1));
        await client.GetStream().WriteAsync(invalid);
        Assert.Equal(ResultCode.InvalidRequest, (await ReadAsync(client.GetStream())).Header.ResultCode);
        await WaitAsync(() => server.Network.Active == 0, 1500);
        await server.AssertReleasedAsync();
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task IndependentCSharpClientCompletesLoginAndHeartbeat(bool encrypted)
    {
        string communicationKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await using var server = await TestServer.StartAsync(new()
        { ["Networking:Encryption:KeyBase64"] = communicationKey }, useDefaultEncryption: encrypted);
        string clientDll = Path.GetFullPath("../../../../GameServer.LoadTests/bin/Release/net10.0/GameServer.LoadTests.dll", AppContext.BaseDirectory);
        string dotnet = Path.Combine(Environment.GetEnvironmentVariable("DOTNET_ROOT") ?? "/workspace/.dotnet", "dotnet");
        var start = new System.Diagnostics.ProcessStartInfo(dotnet)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (string argument in new[] { clientDll, "127.0.0.1", server.Port.ToString(), "tcp-load-8010" })
            start.ArgumentList.Add(argument);
        start.Environment["GAMESERVER_CLIENT_TOKEN"] = server.Login(8010).AccessToken;
        start.Environment["GAMESERVER_Networking__Encryption__KeyBase64"] = communicationKey;
        if (encrypted) start.Environment.Remove("GAMESERVER_Networking__Encryption__Mode");
        else start.Environment["GAMESERVER_Networking__Encryption__Mode"] = "None";
        using var process = System.Diagnostics.Process.Start(start)!;
        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(process.ExitCode == 0, error);
        Assert.Contains("Heartbeat OK", output);
        await server.AssertReleasedAsync();
    }

    [Fact]
    public async Task DatabaseUnavailabilityPreventsListenerStartup()
    {
        await Assert.ThrowsAsync<DatabaseException>(() => TestServer.StartAsync(new()
        {
            ["Database:ConnectionString"] = "Server=127.0.0.1;Port=9;Database=gameserver_tcp_test;User ID=gameserver_tcp_test;SslMode=Disabled",
            ["Database:ConnectionTimeoutSeconds"] = "1"
        }));
    }

    [Fact]
    public async Task LoginFragmentedPacketsHeartbeatProgressCasAndSaveRoundtrip()
    {
        await using var server = await TestServer.StartAsync();
        using var client = await server.ConnectAsync(); Stream stream = client.GetStream();
        var login = server.Login(8001); byte[] packet = Encode((int)MessageId.Login, 1, login);
        for (int i = 0; i < packet.Length; i += 3) await stream.WriteAsync(packet.AsMemory(i, Math.Min(3, packet.Length - i)));
        var response = await ReadAsync(stream); Assert.Equal(ResultCode.Ok, response.Header.ResultCode);
        Assert.Equal(8001, LoginResponse.Parser.ParseFrom(response.Body).PlayerId);
        byte[] heartbeat = Encode((int)MessageId.Heartbeat, 2, null);
        await stream.WriteAsync(heartbeat.Concat(heartbeat).ToArray());
        Assert.Equal(2, (await ReadAsync(stream)).Header.RequestId); Assert.Equal(2, (await ReadAsync(stream)).Header.RequestId);
        var read = await RequestAsync(stream, MessageId.ProgressRead, new ProgressReadRequest());
        long version = ProgressResponse.Parser.ParseFrom(read.Body).Version;
        var write = await RequestAsync(stream, MessageId.ProgressWrite, new ProgressWriteRequest { Progress = 77, ExpectedVersion = version });
        Assert.Equal(ResultCode.Ok, write.Header.ResultCode); Assert.Equal(version + 1, ProgressResponse.Parser.ParseFrom(write.Body).Version);
        Assert.Equal(ResultCode.Conflict, (await RequestAsync(stream, MessageId.ProgressWrite, new ProgressWriteRequest { Progress = 88, ExpectedVersion = version })).Header.ResultCode);
        Assert.Equal(ResultCode.Ok, (await RequestAsync(stream, MessageId.SaveWrite, new SaveWriteRequest { Data = ByteString.CopyFromUtf8("save-data") })).Header.ResultCode);
        Assert.Equal("save-data", SaveResponse.Parser.ParseFrom((await RequestAsync(stream, MessageId.SaveRead, new SaveReadRequest())).Body).Data.ToStringUtf8());
        Assert.Equal(ResultCode.Unsupported, (await RequestAsync(stream, MessageId.FacebookBind, new FacebookBindRequest { AccessToken = "test", ExpectedUserId = "test" })).Header.ResultCode);
        client.Close(); await server.AssertReleasedAsync();
    }

    [Fact]
    public async Task DuplicateLoginOldCleanupCannotRemoveNewPlayer()
    {
        await using var server = await TestServer.StartAsync();
        using var first = await server.ConnectAsync(); Assert.Equal(ResultCode.Ok, (await RequestAsync(first.GetStream(), MessageId.Login, server.Login(8002))).Header.ResultCode);
        using var second = await server.ConnectAsync(); Assert.Equal(ResultCode.Ok, (await RequestAsync(second.GetStream(), MessageId.Login, server.Login(8002))).Header.ResultCode);
        Assert.Equal(0, await first.GetStream().ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(ResultCode.Ok, (await RequestAsync(second.GetStream(), MessageId.Heartbeat, null)).Header.ResultCode);
        Assert.Equal(1, server.Host.Services.GetRequiredService<GameplayMetrics>().Players);
        second.Close(); await server.AssertReleasedAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreAuthenticationAndInvalidTokenNeverGrantIdentity(bool invalidToken)
    {
        await using var server = await TestServer.StartAsync(); using var client = await server.ConnectAsync();
        var result = invalidToken ? await RequestAsync(client.GetStream(), MessageId.Login, new LoginRequest { DeviceId = "tcp-load-8003", AccessToken = "forged" }) :
            await RequestAsync(client.GetStream(), MessageId.Heartbeat, null);
        Assert.Equal(invalidToken ? ResultCode.InvalidCredentials : ResultCode.NotAuthenticated, result.Header.ResultCode);
        Assert.Equal(0, server.Host.Services.GetRequiredService<GameplayMetrics>().Players);
        client.Close(); await server.AssertReleasedAsync();
    }

    [Fact]
    public async Task LoginAbsoluteDeadlineSurvivesRepeatedFailedLogin()
    {
        await using var server = await TestServer.StartAsync(new() { ["Gameplay:LoginTimeoutSeconds"] = "1" });
        using var client = await server.ConnectAsync();
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(ResultCode.InvalidCredentials, (await RequestAsync(client.GetStream(), MessageId.Login, new LoginRequest { DeviceId = "tcp-load-8003", AccessToken = "bad" })).Header.ResultCode);
            await Task.Delay(200);
        }
        await WaitAsync(() => server.Network.Active == 0, 2500); await server.AssertReleasedAsync();
    }

    [Fact]
    public async Task SlowHeaderAndTruncatedLargeBodyReleaseReceiveLeases()
    {
        await using var server = await TestServer.StartAsync(new() { ["Networking:PacketTimeoutSeconds"] = "1" });
        using (var slow = await server.ConnectAsync())
        {
            await slow.GetStream().WriteAsync(new byte[] { 0x47 });
            await WaitAsync(() => server.Network.Active == 0, 3000);
        }
        using (var truncated = await server.ConnectAsync())
        {
            byte[] header = new byte[28]; PacketCodec.WriteHeader(header, new(1000, 1, 0, 200000));
            await truncated.GetStream().WriteAsync(header); truncated.Close();
        }
        await server.AssertReleasedAsync();
    }

    [Fact]
    public async Task OversizedPayloadRejectedWithoutBodyAllocation()
    {
        await using var server = await TestServer.StartAsync(); using var client = await server.ConnectAsync();
        byte[] header = new byte[28]; PacketCodec.WriteHeader(header, new(1000, 1, 0, int.MaxValue));
        await client.GetStream().WriteAsync(header);
        Assert.Equal(0, await client.GetStream().ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(3)));
        await server.AssertReleasedAsync();
    }

    [Theory]
    [InlineData("Networking:MaxConnections")]
    [InlineData("Gameplay:MaxUnauthenticatedConnections")]
    public async Task ExcessConnectionsAreClosedAndCapacityRecovers(string setting)
    {
        await using var server = await TestServer.StartAsync(new() { [setting] = "1" });
        using var first = await server.ConnectAsync(); await WaitAsync(() => server.Host.Services.GetRequiredService<GameplayMetrics>().Sessions == 1);
        using var excess = await server.ConnectAsync();
        Assert.Equal(0, await excess.GetStream().ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(3)));
        first.Close(); await server.AssertReleasedAsync();
        using var next = await server.ConnectAsync(); Assert.Equal(ResultCode.Ok, (await RequestAsync(next.GetStream(), MessageId.Login, server.Login(8004))).Header.ResultCode);
    }

    [Fact]
    public async Task RateLimitRejectsBurstThenRefills()
    {
        await using var server = await TestServer.StartAsync(new() { ["Networking:ConnectionBurst"] = "1", ["Networking:MaxNewConnectionsPerSecond"] = "1" });
        using var first = await server.ConnectAsync(); await WaitAsync(() => server.Network.AcceptedCount == 1);
        using var excess = await server.ConnectAsync(); Assert.Equal(0, await excess.GetStream().ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(1, server.Network.RejectedCount);
        first.Close(); await Task.Delay(1100);
        using var next = await server.ConnectAsync(); Assert.Equal(ResultCode.Ok, (await RequestAsync(next.GetStream(), MessageId.Login, server.Login(8005))).Header.ResultCode);
    }

    [Fact]
    public async Task MysqlCompareAndSwapAllowsExactlyOneConcurrentWriter()
    {
        await using var source = new MySqlDataSourceBuilder(TestServer.ConnectionString).Build();
        await using var database = new MySqlDatabase(new() { ConnectionString = TestServer.ConnectionString });
        await using var connection = await source.OpenConnectionAsync(); await using var command = connection.CreateCommand();
        var repository = new PlayerProgressRepository(database);
        for (long playerId = 8100; playerId < 8110; playerId++)
        {
            command.CommandText = $"DELETE FROM tcp_player_progress WHERE player_id={playerId}";
            await command.ExecuteNonQueryAsync();
            var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => repository.TryWriteAsync(playerId, i, 0, default).AsTask()));
            Assert.Equal(1, results.Count(x => x));
            Assert.Equal(1, (await repository.ReadAsync(playerId, default)).Version);
            var updates = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => repository.TryWriteAsync(playerId, i, 1, default).AsTask()));
            Assert.Equal(1, updates.Count(x => x));
            Assert.Equal(2, (await repository.ReadAsync(playerId, default)).Version);
        }
        var binding = new FacebookBindingRepository(database);
        Assert.True(await binding.TryBindAsync(8199, "test-user-8199", default)); Assert.True(await binding.TryBindAsync(8199, "test-user-8199", default));
        Assert.False(await binding.TryBindAsync(8198, "test-user-8199", default));
    }

    [Fact]
    public async Task SlowReceiverHitsSendDeadlineAndShutdownCleansAllResources()
    {
        await using var server = await TestServer.StartAsync(new() { ["Networking:SendTimeoutSeconds"] = "1" });
        using var client = await server.ConnectAsync(); client.ReceiveBufferSize = 1024;
        Assert.Equal(ResultCode.Ok, (await RequestAsync(client.GetStream(), MessageId.Login, server.Login(8006))).Header.ResultCode);
        await server.Host.Services.GetRequiredService<FilePlayerSaveStore>().WriteAsync(8006, new byte[128 * 1024], default);
        byte[] request = Encode((int)MessageId.SaveRead, 1, new SaveReadRequest());
        for (int i = 0; i < 150; i++) await client.GetStream().WriteAsync(request);
        await WaitAsync(() => server.Network.Active == 0, 5000); await server.AssertReleasedAsync();
        using var idle = await server.ConnectAsync(); await WaitAsync(() => server.Network.Active == 1);
        await server.Host.StopAsync(); await server.AssertReleasedAsync();
    }

    internal static byte[] Encode(int id, long request, IMessage? message)
    { byte[] bytes = new byte[28 + (message?.CalculateSize() ?? 0)]; PacketCodec.WriteMessage(bytes, id, request, 0, message); return bytes; }
    internal static async Task<(PacketHeader Header, byte[] Body)> ReadAsync(Stream stream)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); byte[] headerBytes = new byte[28];
        await stream.ReadExactlyAsync(headerBytes, timeout.Token); Assert.True(PacketCodec.TryReadHeader(headerBytes, 256 * 1024, out var header));
        byte[] body = new byte[header.PayloadLength]; await stream.ReadExactlyAsync(body, timeout.Token); return (header, body);
    }
    internal static async Task<(PacketHeader Header, byte[] Body)> RequestAsync(Stream stream, MessageId id, IMessage? message)
    { await stream.WriteAsync(Encode((int)id, 1, message)); return await ReadAsync(stream); }
    internal static async Task WaitAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        using var timeout = new CancellationTokenSource(timeoutMs);
        while (!condition()) await Task.Delay(10, timeout.Token);
    }
}

internal sealed class TestServer : IAsyncDisposable
{
    public const string ConnectionString = "Server=127.0.0.1;Port=13307;Database=gameserver_tcp_test;User ID=gameserver_tcp_test;SslMode=Disabled";
    public required IHost Host { get; init; }
    public required int Port { get; init; }
    public required string SaveDirectory { get; init; }
    public NetworkMetrics Network => Host.Services.GetRequiredService<NetworkMetrics>();
    public static async Task<TestServer> StartAsync(Dictionary<string, string?>? overrides = null, bool useDefaultEncryption = false)
    {
        var reservation = new TcpListener(IPAddress.Loopback, 0); reservation.Start(); int port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
        string dir = Path.Combine(Path.GetTempPath(), "gameserver-integration-" + Guid.NewGuid());
        var config = new Dictionary<string, string?>
        {
            ["Networking:ListenAddress"] = "127.0.0.1",
            ["Networking:Port"] = port.ToString(),
            ["Networking:Encryption:KeyBase64"] = "",
            ["Authentication:SigningKeyBase64"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["Database:ConnectionString"] = ConnectionString,
            ["Storage:RootPath"] = dir,
            ["Logging:LogLevel:Default"] = "Warning"
        };
        // Baseline transport tests choose plaintext explicitly. Default-mode
        // tests exercise the application configuration and encrypted client.
        if (!useDefaultEncryption) config["Networking:Encryption:Mode"] = "None";
        if (overrides != null) foreach (var pair in overrides) config[pair.Key] = pair.Value;
        var host = GameServerApplication.Build([], builder => builder.Configuration.AddInMemoryCollection(config));
        try { await host.StartAsync(); return new() { Host = host, Port = port, SaveDirectory = dir }; }
        catch { host.Dispose(); if (Directory.Exists(dir)) Directory.Delete(dir, true); throw; }
    }
    public LoginRequest Login(int id) => new() { DeviceId = $"tcp-load-{id}", AccessToken = Host.Services.GetRequiredService<JwtLoginTokenVerifier>().Issue(id, $"tcp-load-{id}") };
    public async Task<TcpClient> ConnectAsync()
    {
        long accepted = Network.AcceptedCount, rejected = Network.RejectedCount;
        var client = new TcpClient { NoDelay = true };
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, Port);
            await TcpIntegrationTests.WaitAsync(() => Network.AcceptedCount > accepted || Network.RejectedCount > rejected);
            return client;
        }
        catch { client.Dispose(); throw; }
    }
    public async Task AssertReleasedAsync()
    {
        await TcpIntegrationTests.WaitAsync(() =>
        {
            var state = Host.Services.GetRequiredService<GameplayMetrics>();
            return Network.Active == 0 && state.Sessions == 0 && state.Players == 0 && state.Queued == 0 && state.Running == 0 &&
                Host.Services.GetRequiredService<GameConnectionHandler>().UnauthenticatedCount == 0;
        });
        var pools = Host.Services.GetRequiredService<BufferPools>();
        Assert.Equal(0, pools.Receive.InUseBytes); Assert.Equal(0, pools.Send.InUseBytes);
        var metrics = Host.Services.GetRequiredService<GameplayMetrics>(); Assert.Equal(0, metrics.Players); Assert.Equal(0, metrics.Queued); Assert.Equal(0, metrics.Running);
        Assert.Equal(0, Host.Services.GetRequiredService<GameConnectionHandler>().UnauthenticatedCount);
    }
    public async ValueTask DisposeAsync()
    { await Host.StopAsync(); Host.Dispose(); if (Directory.Exists(SaveDirectory)) Directory.Delete(SaveDirectory, true); }
}
