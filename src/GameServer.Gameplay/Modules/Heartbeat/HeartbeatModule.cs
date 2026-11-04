using GameServer.Gameplay.Dispatching;
using Microsoft.Extensions.DependencyInjection;

namespace GameServer.Gameplay.Modules.Heartbeat;

public static class HeartbeatModule
{
    public static IServiceCollection AddHeartbeatModule(this IServiceCollection services)
    {
        services.AddSingleton<IMessageHandler, HeartbeatHandler>();
        return services;
    }
}
