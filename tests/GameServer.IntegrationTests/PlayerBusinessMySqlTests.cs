using System.Security.Cryptography;
using GameServer.Data;
using GameServer.Data.Abstractions;
using GameServer.Gameplay.Dispatching;
using GameServer.Gameplay.Modules.TowerDefense;
using GameServer.Infrastructure.Persistence.TowerDefense;
using GameServer.Protocol.Framing;
using GameServer.Protocol.Messages;
using Xunit;

namespace GameServer.IntegrationTests;

public sealed class PlayerBusinessMySqlTests
{
    [Fact]
    public async Task MysqlPersistsAggregateAndContentBoundReceiptsAcrossIndependentPools()
    {
        long playerId = NewPlayerId();
        await using var database = CreateDatabase();
        await InitializeAsync(database);
        try
        {
            var clock = new PlayerBusinessClock();
            var service = CreateService(database, clock);
            var start = new TdRequest { OperationId = "mysql-start", ExpectedVersion = 0, StageId = 1 };
            TdResponse started = await service.ExecuteAsync(playerId, TowerDefenseAction.StartRun, start);
            var choice = new TdRequest
            {
                OperationId = "mysql-choice", ExpectedVersion = started.Profile.Version, RunId = started.Run.RunId,
                OfferId = started.Run.Offer.OfferId, CardId = started.Run.Offer.CardIds.First(id => id != "bounty")
            };
            TdResponse chosen = await service.ExecuteAsync(playerId, TowerDefenseAction.ChooseCard, choice);

            await using var independentDatabase = CreateDatabase();
            var restoredService = CreateService(independentDatabase, clock);
            Assert.Equal(chosen, await restoredService.ExecuteAsync(playerId, TowerDefenseAction.GetRun, new()));
            Assert.Equal(started, await restoredService.ExecuteAsync(playerId, TowerDefenseAction.StartRun, start));
            Assert.Equal(chosen, await restoredService.ExecuteAsync(playerId, TowerDefenseAction.GetRun, new()));
            TdRequest changedReplay = start.Clone(); changedReplay.ExpectedVersion = 1;
            await AssertConflictAsync(() => restoredService.ExecuteAsync(playerId, TowerDefenseAction.StartRun, changedReplay).AsTask());
            await AssertConflictAsync(() => restoredService.ExecuteAsync(playerId, TowerDefenseAction.BuildTower,
                new() { OperationId = "mysql-stale", ExpectedVersion = 1, RunId = chosen.Run.RunId, Slot = 0 }).AsTask());
            Assert.Equal(0L, Convert.ToInt64(await database.ScalarAsync(new(
                "SELECT COUNT(*) FROM tcp_td_receipt WHERE player_id=@id AND operation_id=@operation", ("id", playerId), ("operation", "mysql-stale")))));

            var build = new TdRequest { OperationId = "mysql-build", ExpectedVersion = chosen.Profile.Version, RunId = chosen.Run.RunId, Slot = 0 };
            TdResponse built = await restoredService.ExecuteAsync(playerId, TowerDefenseAction.BuildTower, build);
            Assert.Equal(3, built.Profile.Version);
            Assert.Equal(100, built.Run.Gold);
            Assert.Single(built.Run.Towers);
            Assert.Equal(chosen, await service.ExecuteAsync(playerId, TowerDefenseAction.ChooseCard, choice));
            Assert.Equal(built, await service.ExecuteAsync(playerId, TowerDefenseAction.GetRun, new()));
            Assert.Equal(3L, Convert.ToInt64(await database.ScalarAsync(new("SELECT COUNT(*) FROM tcp_td_receipt WHERE player_id=@id", ("id", playerId)))));
        }
        finally { await CleanupAsync(database, playerId); }
    }

    [Fact]
    public async Task MysqlConcurrentCasCommitsOneTowerAndNoLosingReceipts()
    {
        long playerId = NewPlayerId();
        await using var database = CreateDatabase();
        await InitializeAsync(database);
        try
        {
            var clock = new PlayerBusinessClock();
            var service = CreateService(database, clock);
            TdResponse started = await service.ExecuteAsync(playerId, TowerDefenseAction.StartRun,
                new() { OperationId = "race-start", ExpectedVersion = 0, StageId = 1 });
            TdResponse chosen = await service.ExecuteAsync(playerId, TowerDefenseAction.ChooseCard, new()
            {
                OperationId = "race-choice", ExpectedVersion = started.Profile.Version, RunId = started.Run.RunId,
                OfferId = started.Run.Offer.OfferId, CardId = started.Run.Offer.CardIds.First(id => id != "bounty")
            });
            bool[] outcomes = await Task.WhenAll(Enumerable.Range(0, 12).Select(async index =>
            {
                try
                {
                    await service.ExecuteAsync(playerId, TowerDefenseAction.BuildTower, new()
                    {
                        OperationId = "race-build-" + index, ExpectedVersion = chosen.Profile.Version,
                        RunId = chosen.Run.RunId, Slot = 0
                    });
                    return true;
                }
                catch (RequestFailureException ex) when (ex.Result == ResultCode.Conflict) { return false; }
            }));
            Assert.Equal(1, outcomes.Count(success => success));
            TdResponse current = await service.ExecuteAsync(playerId, TowerDefenseAction.GetRun, new());
            Assert.Equal(chosen.Profile.Version + 1, current.Profile.Version);
            Assert.Equal(chosen.Run.Gold - 50, current.Run.Gold);
            Assert.Single(current.Run.Towers);
            Assert.Equal(1L, Convert.ToInt64(await database.ScalarAsync(new(
                "SELECT COUNT(*) FROM tcp_td_receipt WHERE player_id=@id AND operation_id LIKE 'race-build-%'", ("id", playerId)))));

            int winner = Array.FindIndex(outcomes, success => success);
            TdResponse retried = await service.ExecuteAsync(playerId, TowerDefenseAction.BuildTower, new()
            {
                OperationId = "race-build-" + winner, ExpectedVersion = chosen.Profile.Version,
                RunId = chosen.Run.RunId, Slot = 0
            });
            Assert.Equal(current, retried);
            Assert.Equal(current, await service.ExecuteAsync(playerId, TowerDefenseAction.GetRun, new()));
        }
        finally { await CleanupAsync(database, playerId); }
    }

    private static IDatabase CreateDatabase() => DatabaseFactory.Create(new()
    {
        Provider = DatabaseProvider.MySql, ConnectionString = TestServer.ConnectionString,
        MaximumPoolSize = 16, CommandTimeoutSeconds = 10
    });

    private static TowerDefenseService CreateService(IDatabase database, TimeProvider clock) =>
        new(new DatabaseTowerDefenseStore(database, clock), new() { Enabled = true, WaveDurationSeconds = 5 }, clock);

    private static long NewPlayerId() => checked(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000 + RandomNumberGenerator.GetInt32(1_000));

    private static async Task InitializeAsync(IDatabase database)
    {
        string path = Path.GetFullPath("../../../../../sql/tower-defense-mysql.sql", AppContext.BaseDirectory);
        await database.ExecuteAsync(new(await File.ReadAllTextAsync(path)));
    }

    private static async Task CleanupAsync(IDatabase database, long playerId)
    {
        await database.ExecuteAsync(new("DELETE FROM tcp_td_receipt WHERE player_id=@id", ("id", playerId)));
        await database.ExecuteAsync(new("DELETE FROM tcp_td_player WHERE player_id=@id", ("id", playerId)));
    }

    private static async Task AssertConflictAsync(Func<Task<TdResponse>> operation)
    {
        RequestFailureException error = await Assert.ThrowsAsync<RequestFailureException>(operation);
        Assert.Equal(ResultCode.Conflict, error.Result);
    }
}
