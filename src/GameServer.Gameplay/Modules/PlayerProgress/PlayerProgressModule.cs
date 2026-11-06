using GameServer.Gameplay.Dispatching;
using Microsoft.Extensions.DependencyInjection;

namespace GameServer.Gameplay.Modules.PlayerProgress;

public static class PlayerProgressModule
{
    public static IServiceCollection AddPlayerProgressModule(this IServiceCollection services, bool allowClientWrites = true)
    {
        services.AddSingleton<PlayerProgressService>();
        services.AddSingleton<IMessageHandler, ProgressReadHandler>();
        if (allowClientWrites) services.AddSingleton<IMessageHandler, ProgressWriteHandler>();
        return services;
    }
}
