using GameServer.Gameplay.Notifications;

namespace GameServer.Gameplay.Configuration;

public sealed class GameplayOptions
{
    public int MaxUnauthenticatedConnections { get; set; } = 1024;
    public int LoginTimeoutSeconds { get; set; } = 10;
    public int IdleTimeoutSeconds { get; set; } = 120;
    public int LoginConcurrency { get; set; } = 32;
    public int LoginQueueCapacity { get; set; } = 64;
    public int RequestConcurrency { get; set; } = 128;
    public int RequestQueueCapacity { get; set; } = 256;
    public int QueueTimeoutSeconds { get; set; } = 2;
    public int RequestTimeoutSeconds { get; set; } = 5;
    public NotificationOptions Notifications { get; set; } = new();
    public void Validate()
    {
        Notifications.Validate();
        if (MaxUnauthenticatedConnections < 1 || LoginTimeoutSeconds is < 1 or > 3600 || IdleTimeoutSeconds is < 1 or > 86400 ||
            LoginConcurrency is < 1 or > 1024 || RequestConcurrency is < 1 or > 1024 || LoginQueueCapacity is < 1 or > 10000 ||
            RequestQueueCapacity is < 1 or > 10000 || QueueTimeoutSeconds is < 1 or > 60 || RequestTimeoutSeconds is < 1 or > 300)
            throw new InvalidOperationException("Invalid Gameplay resource limits.");
    }
}
