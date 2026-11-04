namespace GameServer.Data;

public enum DatabaseError { UniqueConstraint, Constraint, ConcurrentWrite, Busy, Timeout, Unavailable, TransactionAborted, Other }

public sealed class DatabaseException(DatabaseError error, Exception? inner = null)
    : Exception($"Database operation failed ({error}).", inner)
{
    public DatabaseError Error { get; } = error;
}
