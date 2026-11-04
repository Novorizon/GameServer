namespace GameServer.Gameplay.Notifications;

public readonly record struct NotificationBroadcastResult(int TargetCount, int SentCount, int UnavailableCount);
