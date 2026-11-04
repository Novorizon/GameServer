using GameServer.Gameplay.Dispatching;
using Microsoft.Extensions.DependencyInjection;

namespace GameServer.Gameplay.Modules.Facebook;

public static class FacebookModule
{
    public static IServiceCollection AddFacebookModule(this IServiceCollection services)
    {
        services.AddSingleton<FacebookBindingService>();
        services.AddSingleton<IMessageHandler, FacebookBindHandler>();
        return services;
    }
}
