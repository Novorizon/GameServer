using GameServer.LoadTests;
using GameServer.Networking.Configuration;
using System.Net.Sockets;
using GameServer.Protocol.Framing;
using GameServer.Protocol.Messages;
using Google.Protobuf;

if (args.FirstOrDefault() == "--serve")
{
    using var host = GameServer.Host.Hosting.GameServerApplication.Build(args[1..],
        builder => Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<GameServer.Gameplay.Dispatching.IMessageHandler, LoadEchoHandler>(builder.Services));
    await Microsoft.Extensions.Hosting.HostingAbstractionsHostExtensions.RunAsync(host);
    return 0;
}
if (args.FirstOrDefault() == "--benchmark") return await BenchmarkClient.RunAsync(args[1..]);
if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: client <host> <port> <device-id> [--plain|--aes-gcm]; default AES-GCM; token from GAMESERVER_CLIENT_TOKEN");
    return 2;
}
string token = Environment.GetEnvironmentVariable("GAMESERVER_CLIENT_TOKEN") ?? throw new InvalidOperationException("Missing client token.");
if (args.Contains("--plain") && args.Contains("--aes-gcm"))
    throw new ArgumentException("Select only one encryption mode.");
var encryption = Enum.Parse<PacketEncryptionMode>(Environment.GetEnvironmentVariable("GAMESERVER_Networking__Encryption__Mode") ?? "AesGcm", ignoreCase: true);
if (args.Contains("--plain")) encryption = PacketEncryptionMode.None;
if (args.Contains("--aes-gcm")) encryption = PacketEncryptionMode.AesGcm;
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
using var client = new TcpClient { NoDelay = true };
await client.ConnectAsync(args[0], int.Parse(args[1]), timeout.Token);
await using Stream stream = client.GetStream();
using var transport = await ClientPacketTransport.ConnectAsync(stream, encryption, timeout.Token);
var login = new LoginRequest { DeviceId = args[2], AccessToken = token };
byte[] packet = new byte[28 + login.CalculateSize()];
PacketCodec.WriteMessage(packet, (int)MessageId.Login, 1, 0, login);
await transport.ExchangeAsync(packet, timeout.Token);
Console.WriteLine($"Player: {LoginResponse.Parser.ParseFrom(transport.Body.Span).PlayerId}");
packet = new byte[28];
PacketCodec.WriteMessage(packet, (int)MessageId.Heartbeat, 2, 0, null);
await transport.ExchangeAsync(packet, timeout.Token);
if (transport.Body.Length != 0) throw new IOException("Invalid heartbeat body.");
Console.WriteLine("Heartbeat OK");
return 0;
