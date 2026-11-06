using GameServer.Gameplay.Dispatching;
using GameServer.Gameplay.Players;
using GameServer.Gameplay.Sessions;
using GameServer.Protocol.Framing;
using GameServer.Protocol.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GameServer.Gameplay.Modules.TowerDefense;

public static class TowerDefenseModule
{
    public static IServiceCollection AddTowerDefenseModule(this IServiceCollection services, TowerDefenseOptions options)
    {
        options.Validate();
        if (!options.Enabled) return services;
        services.AddSingleton(options);
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<TowerDefenseService>();
        foreach (var action in Enum.GetValues<TowerDefenseAction>())
            services.AddSingleton<IMessageHandler>(provider => new TowerDefenseHandler(action, provider.GetRequiredService<TowerDefenseService>(), provider.GetRequiredService<PlayerManager>()));
        return services;
    }
}

public sealed class TowerDefenseHandler(TowerDefenseAction action, TowerDefenseService service, PlayerManager players)
    : MessageHandler<TdRequest>((int)action, TdRequest.Parser)
{
    protected override async ValueTask HandleMessageAsync(GameSession session, PacketHeader header, TdRequest message, CancellationToken ct)
    {
        var response = await players.Get(session).ExecuteAsync(session, token => service.ExecuteAsync(session.PlayerId, action, message, token), ct);
        await session.SendAsync(header, ResultCode.Ok, response, ct);
    }
}
