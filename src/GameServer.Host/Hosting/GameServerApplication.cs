using GameServer.Gameplay;
using GameServer.Gameplay.Configuration;
using GameServer.Gameplay.Modules.TowerDefense;
using GameServer.Infrastructure;
using GameServer.Infrastructure.Authentication;
using GameServer.Infrastructure.External.Facebook;
using GameServer.Infrastructure.Persistence;
using GameServer.Infrastructure.Persistence.Tables;
using GameServer.Infrastructure.Persistence.TowerDefense;
using GameServer.Data;
using GameServer.Data.Providers;
using GameServer.Infrastructure.Storage;
using GameServer.Networking;
using GameServer.Networking.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GameServer.Host.Hosting;

public static class GameServerApplication
{
    public static IHost Build(string[] args, Action<HostApplicationBuilder>? configure = null)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });
        builder.Configuration.AddEnvironmentVariables("GAMESERVER_");
        configure?.Invoke(builder);
        if (!string.IsNullOrEmpty(builder.Configuration["Networking:CertificatePath"]))
            throw new InvalidOperationException("This independent TCP server has no TLS listener. Use Networking:Encryption instead.");
        var network = builder.Configuration.GetSection("Networking").Get<TcpServerOptions>() ?? new();
        var gameplay = builder.Configuration.GetSection("Gameplay").Get<GameplayOptions>() ?? new();
        var towerDefense = builder.Configuration.GetSection("TowerDefense").Get<TowerDefenseOptions>() ?? new();
        var auth = builder.Configuration.GetSection("Authentication").Get<AuthenticationOptions>() ?? new();
        var database = builder.Configuration.GetSection("Database").Get<DatabaseOptions>() ?? new();
        var storage = builder.Configuration.GetSection("Storage").Get<StorageOptions>() ?? new();
        var facebook = builder.Configuration.GetSection("Facebook").Get<FacebookOptions>() ?? new();
        int saveEnvelopeBytes = storage.Provider == PlayerSaveProvider.Database ? 128 : 64;
        if (storage.MaxSaveBytes + saveEnvelopeBytes > network.MaximumApplicationPacketBytes)
            throw new InvalidOperationException("Storage MaxSaveBytes must fit inside Networking MaxPacketBytes with protocol overhead.");
        // Registration defines startup order; shutdown reverses it.
        builder.Services.AddInfrastructure(auth, database, storage, facebook);
        builder.Services.AddTowerDefensePersistence(towerDefense);
        builder.Services.AddGameplay(gameplay, towerDefense);
        builder.Services.AddNetworking(network);
        builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(network.DrainTimeoutSeconds + 10));
        builder.Services.AddHostedService<ResourceTelemetryService>();
        return builder.Build();
    }
}
