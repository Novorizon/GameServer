using GameServer.Networking.Abstractions;
using GameServer.Networking.Configuration;
using GameServer.Networking.Diagnostics;
using GameServer.Networking.Limits;
using GameServer.Networking.Memory;
using GameServer.Networking.Tcp;
using GameServer.Networking.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GameServer.Networking;

public static class NetworkingRegistration
{
    public static IServiceCollection AddNetworking(this IServiceCollection services, TcpServerOptions options)
    {
        options.Validate();
        services.AddSingleton(options);
        services.AddSingleton<BufferPools>();
        services.AddSingleton<IConnectionMessageSender, ConnectionMessageSender>();
        services.AddSingleton<NetworkMetrics>();
        services.AddSingleton<ConnectionAdmissionController>();
        services.AddSingleton<TcpPacketReader>();
        services.AddSingleton<ConnectionCipherFactory>();
        services.AddSingleton<TcpServer>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<TcpServer>());
        return services;
    }
}
