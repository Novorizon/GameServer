using GameServer.Gameplay.Dispatching;
using Microsoft.Extensions.DependencyInjection;

namespace GameServer.Gameplay.Modules.PlayerProgress;

public static class PlayerProgressModule
{
    public static IServiceCollection AddPlayerProgressModule(this IServiceCollection services)
    {
        services.AddSingleton<PlayerProgressService>();
        services.AddSingleton<IMessageHandler, ProgressReadHandler>();
        services.AddSingleton<IMessageHandler, ProgressWriteHandler>();
        return services;
    }
}
