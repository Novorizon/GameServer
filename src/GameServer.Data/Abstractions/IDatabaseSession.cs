namespace GameServer.Data.Abstractions;

// Valid only inside its owning session/transaction callback. Commands serialize.
public interface IDatabaseSession : IDatabaseExecutor { }
