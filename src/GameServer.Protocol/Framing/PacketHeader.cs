namespace GameServer.Protocol.Framing;

public readonly record struct PacketHeader(int MessageId, long RequestId, ResultCode ResultCode, int PayloadLength);

public enum ResultCode
{
    Ok = 0, InvalidRequest = 1, NotAuthenticated = 2, NotFound = 3,
    ServerBusy = 4, RequestTimeout = 5, Conflict = 6, InternalError = 7,
    Unsupported = 8, SessionReplaced = 9, InvalidCredentials = 100
}
