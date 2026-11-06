using System.Security.Cryptography;
using GameServer.Host.Hosting;
using GameServer.Protocol.Framing;
using GameServer.Protocol.Messages;
using Google.Protobuf;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GameServer.UnitTests;

public sealed class PlayerSaveEnvelopeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DatabaseSaveReservesSpaceForMaximumOperationIdAndVersion(bool encrypted)
    {
        const int packetBytes = 1024;
        int encryptionBytes = encrypted ? 16 : 0;
        int safeSaveBytes = packetBytes - encryptionBytes - 128;
        using var host = GameServerApplication.Build([], builder => builder.Configuration.AddInMemoryCollection(
            Config(encrypted, safeSaveBytes)));
        var request = new SaveWriteRequest
        {
            Data = ByteString.CopyFrom(new byte[safeSaveBytes]),
            ExpectedVersion = long.MaxValue - 1,
            OperationId = new string('a', 64)
        };
        Assert.True(PacketCodec.HeaderSize + request.CalculateSize() + encryptionBytes <= packetBytes);

        var error = Assert.Throws<InvalidOperationException>(() => GameServerApplication.Build([],
            builder => builder.Configuration.AddInMemoryCollection(Config(encrypted, packetBytes - encryptionBytes - 64))));
        Assert.Contains("MaxSaveBytes", error.Message);
    }

    private static Dictionary<string, string?> Config(bool encrypted, int size) => new()
    {
        ["Database:Provider"] = "Sqlite",
        ["Database:ConnectionString"] = "Data Source=:memory:",
        ["Authentication:SigningKeyBase64"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        ["Networking:Encryption:Mode"] = encrypted ? "AesGcm" : "None",
        ["Networking:Encryption:KeyBase64"] = encrypted ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) : "",
        ["Networking:MaxPacketBytes"] = "1024",
        ["Networking:MaxRetainedBufferBytes"] = "1024",
        ["Storage:Provider"] = "Database",
        ["Storage:MaxSaveBytes"] = size.ToString(System.Globalization.CultureInfo.InvariantCulture)
    };
}
