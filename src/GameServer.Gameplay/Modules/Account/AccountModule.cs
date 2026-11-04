using GameServer.Gameplay.Dispatching;
using Microsoft.Extensions.DependencyInjection;

namespace GameServer.Gameplay.Modules.Account;

public static class AccountModule
{
    public static IServiceCollection AddAccountModule(this IServiceCollection services)
    {
        services.AddSingleton<AccountService>();
        services.AddSingleton<IMessageHandler, LoginHandler>();
        return services;
    }
}
