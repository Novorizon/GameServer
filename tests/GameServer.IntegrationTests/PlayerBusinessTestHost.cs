using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using GameServer.Data;
using GameServer.Gameplay.Diagnostics;
using GameServer.Gameplay.Sessions;
using GameServer.Host.Hosting;
using GameServer.Infrastructure.Authentication;
using GameServer.Infrastructure.Persistence.Tables;
using GameServer.LoadTests;
using GameServer.Networking.Configuration;
using GameServer.Networking.Diagnostics;
using GameServer.Networking.Memory;
using GameServer.Protocol.Framing;
using GameServer.Protocol.Messages;
using Google.Protobuf;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GameServer.IntegrationTests;

// Uses the real Host, SQLite stores, authenticated TCP framing and optional AES-GCM.
// A controlled business clock makes a timed wave test independent of wall-clock sleeps.
internal sealed class PlayerBusinessFixture : IAsyncDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "gameserver-player-business-" + Guid.NewGuid());
    private readonly string _signingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    internal string CommunicationKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    internal PlayerBusinessClock Clock { get; } = new();
    private string ConnectionString => "Data Source=" + Path.Combine(_directory, "game.db");

    internal async Task InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        await using var database = DatabaseFactory.Create(new() { Provider = DatabaseProvider.Sqlite, ConnectionString = ConnectionString });
        foreach (string name in new[] { "sqlite-schema.sql", "player-save-sqlite.sql", "tower-defense-sqlite.sql" })
        {
            string path = Path.GetFullPath("../../../../../sql/" + name, AppContext.BaseDirectory);
            await database.ExecuteAsync(new(await File.ReadAllTextAsync(path)));
        }
        var accounts = new AccountRepository(database);
        foreach (int playerId in new[] { 8401, 8402 })
            await accounts.InsertAsync(new() { Id = playerId, DeviceId = "business-device-" + playerId });
    }

    internal async Task<PlayerBusinessHost> StartAsync(bool encrypted)
    {
        var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        var mode = encrypted ? PacketEncryptionMode.AesGcm : PacketEncryptionMode.None;
        var configuration = new Dictionary<string, string?>
        {
            ["Networking:ListenAddress"] = "127.0.0.1",
            ["Networking:Port"] = port.ToString(),
            ["Networking:Encryption:Mode"] = mode.ToString(),
            ["Networking:Encryption:KeyBase64"] = CommunicationKey,
            ["Authentication:SigningKeyBase64"] = _signingKey,
            ["Database:Provider"] = "Sqlite",
            ["Database:ConnectionString"] = ConnectionString,
            ["Storage:Provider"] = "Database",
            ["Storage:RootPath"] = Path.Combine(_directory, "legacy-saves"),
            ["TowerDefense:Enabled"] = "true",
            ["TowerDefense:WaveDurationSeconds"] = "5",
            ["Logging:LogLevel:Default"] = "Warning"
        };
        IHost host = GameServerApplication.Build([], builder =>
        {
            builder.Configuration.AddInMemoryCollection(configuration);
            builder.Services.AddSingleton<TimeProvider>(Clock);
        });
        try
        {
            await host.StartAsync();
            return new(host, port, mode, CommunicationKey);
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        return ValueTask.CompletedTask;
    }
}

internal sealed class PlayerBusinessClock : TimeProvider
{
    private long _unixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(Interlocked.Read(ref _unixMilliseconds));
    internal void Advance(TimeSpan elapsed) => Interlocked.Add(ref _unixMilliseconds, checked((long)elapsed.TotalMilliseconds));
}

internal sealed class PlayerBusinessHost(IHost host, int port, PacketEncryptionMode mode, string communicationKey) : IAsyncDisposable
{
    internal async Task<PlayerBusinessClient> ConnectAsync(int? playerId = null)
    {
        var socket = new TcpClient { NoDelay = true };
        try
        {
            await socket.ConnectAsync(IPAddress.Loopback, port);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var transport = await ClientPacketTransport.ConnectAsync(socket.GetStream(), mode, communicationKey, timeout.Token);
            var client = new PlayerBusinessClient(socket, transport);
            if (playerId is { } id)
            {
                string device = "business-device-" + id;
                var request = new LoginRequest { DeviceId = device, AccessToken = host.Services.GetRequiredService<JwtLoginTokenVerifier>().Issue(id, device) };
                var response = LoginResponse.Parser.ParseFrom(await client.SendAsync((int)MessageId.Login, request));
                Assert.Equal(id, response.PlayerId);
                Assert.Equal(device, response.DeviceId);
            }
            return client;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    internal async Task AssertReleasedAsync()
    {
        var network = host.Services.GetRequiredService<NetworkMetrics>();
        var gameplay = host.Services.GetRequiredService<GameplayMetrics>();
        await TcpIntegrationTests.WaitAsync(() => network.Active == 0 && gameplay.Players == 0 && gameplay.Sessions == 0 &&
            gameplay.Queued == 0 && gameplay.Running == 0 && host.Services.GetRequiredService<GameConnectionHandler>().UnauthenticatedCount == 0);
        var pools = host.Services.GetRequiredService<BufferPools>();
        Assert.Equal(0, pools.Receive.InUseBytes);
        Assert.Equal(0, pools.Send.InUseBytes);
    }

    public async ValueTask DisposeAsync()
    {
        await host.StopAsync();
        await AssertReleasedAsync();
        host.Dispose();
    }
}

internal sealed class PlayerBusinessClient(TcpClient socket, ClientPacketTransport transport) : IDisposable
{
    private long _requestId;
    private bool _disposed;

    internal async Task<byte[]> SendAsync(int messageId, IMessage request, ResultCode expected = ResultCode.Ok)
    {
        long requestId = ++_requestId;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await transport.ExchangeAsync(TcpIntegrationTests.Encode(messageId, requestId, request), timeout.Token);
        }
        catch (IOException) when (expected != ResultCode.Ok && transport.Header.RequestId == requestId && transport.Header.MessageId == messageId && transport.Header.ResultCode == expected)
        {
            // The sample transport throws for non-OK replies after reading and authenticating them.
        }
        Assert.Equal(messageId, transport.Header.MessageId);
        Assert.Equal(requestId, transport.Header.RequestId);
        Assert.Equal(expected, transport.Header.ResultCode);
        return transport.Body.ToArray();
    }

    internal async Task<TdResponse> BusinessAsync(TowerDefenseMessageId messageId, TdRequest? request = null) =>
        TdResponse.Parser.ParseFrom(await SendAsync((int)messageId, request ?? new TdRequest()));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        transport.Dispose();
        socket.Dispose();
    }
}
