using GameServer.Gameplay.Modules.Account;
using GameServer.Gameplay.Modules.PlayerProgress;
using GameServer.Gameplay.Modules.PlayerSave;
using GameServer.Gameplay.Modules.Facebook;
using GameServer.Infrastructure.Authentication;
using GameServer.Infrastructure.Persistence;
using GameServer.Infrastructure.Persistence.Tables;
using GameServer.Data;
using GameServer.Data.Abstractions;
using GameServer.Data.Tables;
using GameServer.Infrastructure.Storage;
using GameServer.Infrastructure.External.Facebook;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GameServer.Infrastructure;

public static class InfrastructureRegistration
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, AuthenticationOptions authentication,
        DatabaseOptions database, StorageOptions storage, FacebookOptions facebook)
    {
        _ = authentication.ValidateAndGetKey();
        database.Validate();
        storage.Validate();
        facebook.Validate();
        services.AddSingleton(authentication);
        services.AddSingleton(database);
        services.AddSingleton(storage);
        services.AddSingleton(facebook);
        services.AddSingleton<JwtLoginTokenVerifier>();
        services.AddSingleton<ILoginTokenVerifier>(sp => sp.GetRequiredService<JwtLoginTokenVerifier>());
        services.AddSingleton<IDatabase>(_ => DatabaseFactory.Create(database));
        services.AddSingleton<IDatabaseExecutor>(sp => sp.GetRequiredService<IDatabase>());
        services.AddSingleton<AccountRepository>();
        services.AddSingleton<IAccountRepository>(sp => sp.GetRequiredService<AccountRepository>());
        services.AddSingleton<ITableRepository<AccountRow, long>>(sp => sp.GetRequiredService<AccountRepository>());
        services.AddSingleton<PlayerProgressRepository>();
        services.AddSingleton<IPlayerProgressRepository>(sp => sp.GetRequiredService<PlayerProgressRepository>());
        services.AddSingleton<ITableRepository<PlayerProgressRow, long>>(sp => sp.GetRequiredService<PlayerProgressRepository>());
        services.AddSingleton<FacebookBindingRepository>();
        services.AddSingleton<IFacebookBindingRepository>(sp => sp.GetRequiredService<FacebookBindingRepository>());
        services.AddSingleton<ITableRepository<FacebookBindingRow, long>>(sp => sp.GetRequiredService<FacebookBindingRepository>());
        services.AddSingleton<FilePlayerSaveStore>();
        services.AddSingleton<DatabasePlayerSaveStore>();
        services.AddSingleton<IPlayerSaveStore>(sp => storage.Provider == PlayerSaveProvider.Database
            ? sp.GetRequiredService<DatabasePlayerSaveStore>()
            : sp.GetRequiredService<FilePlayerSaveStore>());
        // Tokens appear in the Graph request URI. Disable framework request URI
        // logging for this client; application logs never contain token bodies.
        services.AddHttpClient("Facebook", client => client.Timeout = TimeSpan.FromSeconds(4)).RemoveAllLoggers();
        services.AddSingleton<IFacebookTokenVerifier, FacebookTokenVerifier>();
        services.AddSingleton<IHostedService, DatabaseReadinessService>();
        return services;
    }
}
