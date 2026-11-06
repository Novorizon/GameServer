using System.Globalization;
using GameServer.Data;
using GameServer.Infrastructure.Authentication;
using GameServer.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;

namespace GameServer.Host.Hosting;

public static class AccountOperatorCommand
{
    public static bool IsOperatorCommand(string[] args) =>
        args.Length > 0 && args[0] is "provision-player" or "issue-login-token";

    public static async Task<int> RunAsync(string[] args, CancellationToken ct = default)
    {
        // Secrets come from the same protected environment/configuration as the server,
        // never command-line flags, source code, or game-client configuration.
        var configuration = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables("GAMESERVER_").Build();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 1; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || args[i] is not ("--player-id" or "--device-id" or "--lifetime-seconds") ||
                !values.TryAdd(args[i], args[i + 1])) return Usage();
        }
        if (!values.TryGetValue("--player-id", out string? idText) ||
            !long.TryParse(idText, NumberStyles.None, CultureInfo.InvariantCulture, out long playerId) || playerId <= 0 ||
            !values.TryGetValue("--device-id", out string? deviceId) || string.IsNullOrWhiteSpace(deviceId) || deviceId.Length > 255)
            return Usage();
        int lifetime = 300;
        if (values.TryGetValue("--lifetime-seconds", out string? lifetimeText) &&
            (!int.TryParse(lifetimeText, NumberStyles.None, CultureInfo.InvariantCulture, out lifetime) || lifetime < 1))
            return Usage();
        if (args[0] == "provision-player" && values.ContainsKey("--lifetime-seconds")) return Usage();

        DatabaseOptions databaseOptions = configuration.GetSection("Database").Get<DatabaseOptions>() ?? new();
        await using var database = DatabaseFactory.Create(databaseOptions);
        var provisioner = new TrustedAccountProvisioner(database);
        if (args[0] == "provision-player")
        {
            await provisioner.ProvisionAsync(playerId, deviceId, ct);
            Console.Out.WriteLine("Player account provisioned.");
        }
        else
        {
            var auth = configuration.GetSection("Authentication").Get<AuthenticationOptions>() ?? new();
            var issuer = new JwtLoginTokenVerifier(auth);
            if (await provisioner.FindExistingAsync(playerId, deviceId, ct) == null)
                throw new InvalidOperationException("Provision the account before issuing a login token.");
            // This explicitly requested token is the only stdout content for piping to
            // a trusted authentication delivery channel. Never log it on the server.
            Console.Out.WriteLine(issuer.Issue(playerId, deviceId, lifetime));
        }
        return 0;
    }

    private static int Usage()
    {
        Console.Error.WriteLine("Usage: GameServer.Host provision-player|issue-login-token --player-id <positive-id> --device-id <device> [--lifetime-seconds <seconds>]");
        return 2;
    }
}
