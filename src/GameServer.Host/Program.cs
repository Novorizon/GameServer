using GameServer.Host.Hosting;
using Microsoft.Extensions.Hosting;

try
{
    if (AccountOperatorCommand.IsOperatorCommand(args))
        return await AccountOperatorCommand.RunAsync(args);
    using IHost host = GameServerApplication.Build(args);
    await host.RunAsync();
    return 0;
}
catch (Exception ex)
{
    // Provider exceptions can contain connection strings; expose only type here.
    Console.Error.WriteLine($"GameServer startup/runtime failure ({ex.GetType().Name}). Check configuration and dependency readiness.");
    return 1;
}
