using GameServer.Protocol.Messages;
using Google.Protobuf;

namespace GameServer.Gameplay.Notifications;

public interface IPlayerNotifier
{
    // True means a complete frame was sent, not that the client acknowledged it.
    ValueTask<bool> NotifyAsync(long playerId, MessageId messageId, IMessage notification, CancellationToken ct = default);
    // Recipients must be selected and authorized by the calling business service.
    Task<NotificationBroadcastResult> NotifyManyAsync(IReadOnlyCollection<long> playerIds, MessageId messageId,
        IMessage notification, CancellationToken ct = default);
}
