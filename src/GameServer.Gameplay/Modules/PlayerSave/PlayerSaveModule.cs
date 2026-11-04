using GameServer.Gameplay.Dispatching;
using Microsoft.Extensions.DependencyInjection;

namespace GameServer.Gameplay.Modules.PlayerSave;

public static class PlayerSaveModule
{
    public static IServiceCollection AddPlayerSaveModule(this IServiceCollection services)
    {
        services.AddSingleton<PlayerSaveService>();
        services.AddSingleton<IMessageHandler, SaveReadHandler>();
        services.AddSingleton<IMessageHandler, SaveWriteHandler>();
        return services;
    }
}
