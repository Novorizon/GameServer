using GameServer.Gameplay.Configuration;
using GameServer.Gameplay.Administration;
using GameServer.Gameplay.Diagnostics;
using GameServer.Gameplay.Dispatching;
using GameServer.Gameplay.Modules.Account;
using GameServer.Gameplay.Modules.Heartbeat;
using GameServer.Gameplay.Modules.PlayerProgress;
using GameServer.Gameplay.Modules.PlayerSave;
using GameServer.Gameplay.Modules.Facebook;
using GameServer.Gameplay.Modules.TowerDefense;
using GameServer.Gameplay.Players;
using GameServer.Gameplay.Notifications;
using GameServer.Gameplay.Sessions;
using GameServer.Networking.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GameServer.Gameplay;

public static class GameplayRegistration
{
    public static IServiceCollection AddGameplay(this IServiceCollection services, GameplayOptions options,
        TowerDefenseOptions? towerDefense = null)
    {
        options.Validate();
        services.AddSingleton(options);
        services.AddSingleton<GameplayMetrics>();
        services.AddSingleton<SessionManager>();
        services.AddSingleton<PlayerManager>();
        services.AddSingleton(options.Notifications);
        services.AddSingleton<IPlayerNotifier, PlayerNotificationService>();
        services.AddSingleton<IPlayerAdministrationService, PlayerAdministrationService>();
        towerDefense ??= new();
        services.AddAccountModule().AddHeartbeatModule()
            .AddPlayerProgressModule(allowClientWrites: !towerDefense.Enabled)
            .AddPlayerSaveModule().AddFacebookModule().AddTowerDefenseModule(towerDefense);
        services.AddSingleton<RequestAdmissionController>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<RequestAdmissionController>());
        services.AddSingleton<PacketDispatcher>();
        services.AddSingleton<GameConnectionHandler>();
        services.AddSingleton<IConnectionHandler>(sp => sp.GetRequiredService<GameConnectionHandler>());
        return services;
    }
}
