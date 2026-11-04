using System.Net.Sockets;
using GameServer.Gameplay.Dispatching;
using GameServer.Gameplay.Players;
using GameServer.Protocol.Framing;
using GameServer.Protocol.Messages;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace GameServer.Gameplay.Notifications;

public sealed class PlayerNotificationService : IPlayerNotifier, IDisposable
{
    private readonly PlayerManager _players;
    private readonly NotificationOptions _options;
    private readonly ILogger<PlayerNotificationService> _logger;
    private readonly SemaphoreSlim _broadcasts;

    public PlayerNotificationService(PlayerManager players, NotificationOptions options, ILogger<PlayerNotificationService> logger)
    {
        options.Validate();
        _players = players;
        _options = options;
        _logger = logger;
        _broadcasts = new(options.MaxConcurrentBroadcasts, options.MaxConcurrentBroadcasts);
    }

    public async ValueTask<bool> NotifyAsync(long playerId, MessageId messageId, IMessage notification, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(playerId);
        ValidateNotification(messageId, notification);
        ct.ThrowIfCancellationRequested();
        if (!_players.TryGetCurrentSession(playerId, out var session)) return false;
        try
        {
            await session.SendNotificationAsync(messageId, notification, ct);
            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return false; }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            ct.ThrowIfCancellationRequested();
            _logger.LogDebug("Notification {MessageId} to player {PlayerId} ended ({ExceptionType})", messageId, playerId, ex.GetType().Name);
            return false;
        }
    }

    public async Task<NotificationBroadcastResult> NotifyManyAsync(IReadOnlyCollection<long> playerIds, MessageId messageId,
        IMessage notification, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(playerIds);
        ValidateNotification(messageId, notification);
        ct.ThrowIfCancellationRequested();
        if (playerIds.Count > _options.MaxBroadcastRecipients)
            throw new ArgumentOutOfRangeException(nameof(playerIds));
        if (!_broadcasts.Wait(0)) throw new RequestFailureException(ResultCode.ServerBusy);
        try
        {
            var targets = new HashSet<long>();
            foreach (long id in playerIds)
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
                targets.Add(id);
                if (targets.Count > _options.MaxBroadcastRecipients) throw new ArgumentOutOfRangeException(nameof(playerIds));
            }
            int sent = 0;
            await Parallel.ForEachAsync(targets, new ParallelOptions
            { MaxDegreeOfParallelism = _options.BroadcastConcurrency, CancellationToken = ct }, async (id, token) =>
            {
                if (await NotifyAsync(id, messageId, notification, token)) Interlocked.Increment(ref sent);
            });
            return new(targets.Count, sent, targets.Count - sent);
        }
        finally { _broadcasts.Release(); }
    }

    private static void ValidateNotification(MessageId messageId, IMessage notification)
    {
        if (!PacketCodec.IsNotificationMessageId((int)messageId)) throw new ArgumentOutOfRangeException(nameof(messageId));
        ArgumentNullException.ThrowIfNull(notification);
    }

    public void Dispose() => _broadcasts.Dispose();
}
