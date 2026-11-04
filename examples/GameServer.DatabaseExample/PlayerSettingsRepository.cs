using GameServer.Data.Abstractions;
using GameServer.Data.Tables;

namespace GameServer.DatabaseExample;

public sealed class PlayerSettingsRepository(IDatabaseExecutor database) : TableRepository<PlayerSettingsRow, long>(database);
