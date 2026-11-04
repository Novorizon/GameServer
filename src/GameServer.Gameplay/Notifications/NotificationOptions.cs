namespace GameServer.Gameplay.Notifications;

public sealed class NotificationOptions
{
    public int MaxBroadcastRecipients { get; set; } = 8000;
    public int BroadcastConcurrency { get; set; } = 16;
    public int MaxConcurrentBroadcasts { get; set; } = 2;

    public void Validate()
    {
        if (MaxBroadcastRecipients is < 1 or > 100000 || BroadcastConcurrency is < 1 or > 256 ||
            MaxConcurrentBroadcasts is < 1 or > 32)
            throw new InvalidOperationException("Invalid Gameplay Notifications limits.");
    }
}
