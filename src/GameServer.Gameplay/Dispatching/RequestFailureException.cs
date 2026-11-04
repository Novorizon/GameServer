using GameServer.Protocol.Framing;

namespace GameServer.Gameplay.Dispatching;

public sealed class RequestFailureException(ResultCode result) : Exception(result.ToString())
{
    public ResultCode Result { get; } = result;
}
