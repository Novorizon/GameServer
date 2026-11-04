using GameServer.Protocol.Framing;
using GameServer.Protocol.Messages;
using Xunit;

namespace GameServer.IntegrationTests;

public sealed class RequestContractTests
{
    [Theory]
    [InlineData(0)] [InlineData(-1)]
    public async Task ClientRequestIdsCannotUseTheNotificationSentinelOrNegativeValues(long requestId)
    {
        await using var server = await TestServer.StartAsync();
        using var socket = await server.ConnectAsync();
        await socket.GetStream().WriteAsync(TcpIntegrationTests.Encode((int)MessageId.Login, requestId, server.Login(8010)));
        using var timeout = new CancellationTokenSource(3000);
        Assert.Equal(0, await socket.GetStream().ReadAsync(new byte[1], timeout.Token));
        await server.AssertReleasedAsync();
    }
}
