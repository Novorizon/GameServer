using System.Text.Json;
using GameServer.Gameplay.Dispatching;
using GameServer.Gameplay.Modules.TowerDefense;
using GameServer.Protocol.Framing;
using GameServer.Protocol.Messages;
using Xunit;

namespace GameServer.UnitTests;

public sealed class TowerDefenseTests
{
    [Fact]
    public async Task DurableReplayReturnsOriginalOfferAndRejectsDifferentContent()
    {
        var h = new Harness();
        var request = new TdRequest { OperationId = "start", StageId = 1 };
        var started = await h.Service.ExecuteAsync(1, TowerDefenseAction.StartRun, request);
        Assert.Equal(3, started.Run.Offer.CardIds.Distinct(StringComparer.Ordinal).Count());
        var chosen = await h.Choose(started);
        Assert.Null(chosen.Run.Offer);

        var restartedService = h.NewService();
        var replay = await restartedService.ExecuteAsync(1, TowerDefenseAction.StartRun, request);
        Assert.Equal(started, replay);
        Assert.Equal(started.Run.Offer, replay.Run.Offer);
        var current = await restartedService.ExecuteAsync(1, TowerDefenseAction.GetRun, new());
        Assert.Equal(chosen, current);
        var changed = request.Clone(); changed.StageId = 2;
        await Failure(ResultCode.Conflict, () => restartedService.ExecuteAsync(1, TowerDefenseAction.StartRun, changed));
        Assert.Equal(2, h.Store.ReceiptCount);
    }

    [Fact]
    public async Task ConcurrentChoicesUseCasAndOnlyOneCardIsGranted()
    {
        var h = new Harness();
        var started = await h.Start();
        var first = Command(started); first.OfferId = started.Run.Offer.OfferId; first.CardId = started.Run.Offer.CardIds[0];
        var second = first.Clone(); second.OperationId = "second"; second.CardId = started.Run.Offer.CardIds[1];
        h.Store.ArmReadBarrier(2);
        Task<ResultCode> a = Attempt(h.Service, first);
        Task<ResultCode> b = Attempt(h.NewService(), second);
        var results = await Task.WhenAll(a, b);
        Assert.Single(results, r => r == ResultCode.Ok);
        Assert.Single(results, r => r == ResultCode.Conflict);
        var current = await h.Get();
        Assert.Equal(2, current.Profile.Version);
        Assert.Single(current.Run.Cards);
        Assert.Equal(1, current.Run.Cards[0].Rank);
        Assert.Equal(2, h.Store.ReceiptCount);
    }

    [Fact]
    public async Task ConcurrentIdenticalCommandsReplayExactlyOneCommit()
    {
        var h = new Harness();
        var started = await h.Start();
        var request = Command(started); request.OfferId = started.Run.Offer.OfferId; request.CardId = started.Run.Offer.CardIds[0];
        h.Store.ArmReadBarrier(2);
        var a = h.Service.ExecuteAsync(1, TowerDefenseAction.ChooseCard, request).AsTask();
        var b = h.NewService().ExecuteAsync(1, TowerDefenseAction.ChooseCard, request.Clone()).AsTask();
        var responses = await Task.WhenAll(a, b);
        Assert.Equal(responses[0], responses[1]);
        Assert.Equal(2, h.Store.ReceiptCount);
        Assert.Equal(2, (await h.Get()).Profile.Version);
    }

    [Fact]
    public async Task OffersOwnershipFundsSlotsAndPhasesAreEnforced()
    {
        var h = new Harness();
        var state = await h.Start();
        var invalidCard = Command(state); invalidCard.OfferId = state.Run.Offer.OfferId; invalidCard.CardId = "forged";
        await Failure(ResultCode.Conflict, () => h.Service.ExecuteAsync(1, TowerDefenseAction.ChooseCard, invalidCard));
        var foreign = Command(state);
        foreign.ExpectedVersion = 0;
        await Failure(ResultCode.NotFound, () => h.Service.ExecuteAsync(2, TowerDefenseAction.AbandonRun, foreign));
        await Failure(ResultCode.Conflict, () => h.Service.ExecuteAsync(1, TowerDefenseAction.StartRun, new() { StageId = 1, ExpectedVersion = state.Profile.Version, OperationId = "parallel-run" }));

        state = await h.Choose(state, avoidBounty: true);
        for (int slot = 0; slot < 3; slot++)
        {
            var build = Command(state); build.Slot = slot;
            state = await h.Service.ExecuteAsync(1, TowerDefenseAction.BuildTower, build);
        }
        Assert.Equal(0, state.Run.Gold);
        var noFunds = Command(state); noFunds.Slot = 3;
        await Failure(ResultCode.Conflict, () => h.Service.ExecuteAsync(1, TowerDefenseAction.BuildTower, noFunds));
        var badSlot = Command(state); badSlot.Slot = 5;
        await Failure(ResultCode.InvalidRequest, () => h.Service.ExecuteAsync(1, TowerDefenseAction.BuildTower, badSlot));
        var occupied = Command(state); occupied.Slot = 0;
        await Failure(ResultCode.Conflict, () => h.Service.ExecuteAsync(1, TowerDefenseAction.BuildTower, occupied));
        var upgrade = Command(state); upgrade.TowerId = state.Run.Towers[0].TowerId;
        await Failure(ResultCode.Conflict, () => h.Service.ExecuteAsync(1, TowerDefenseAction.UpgradeTower, upgrade));
        state = await h.Service.ExecuteAsync(1, TowerDefenseAction.StartWave, Command(state));
        await Failure(ResultCode.Conflict, () => h.Service.ExecuteAsync(1, TowerDefenseAction.BuildTower, Command(state)));
        Assert.Equal(state, await h.Get());
    }

    [Fact]
    public async Task OnlyElapsedServerWavesGrantRewardsAndVictoryCreditsOnceAcrossRestart()
    {
        var h = new Harness();
        var state = await h.Start();
        for (int wave = 1; wave <= 3; wave++)
        {
            Assert.Equal(wave, state.Run.Wave);
            state = await h.Choose(state);
            if (wave == 1)
            {
                for (int slot = 0; slot < 3; slot++)
                {
                    var build = Command(state); build.Slot = slot;
                    state = await h.Service.ExecuteAsync(1, TowerDefenseAction.BuildTower, build);
                }
            }
            state = await h.Service.ExecuteAsync(1, TowerDefenseAction.StartWave, Command(state));
            Assert.Equal(0, state.Profile.WalletCoins);
            h.Clock.Advance(TimeSpan.FromMilliseconds(999));
            Assert.Equal(state, await h.Get());
            h.Clock.Advance(TimeSpan.FromMilliseconds(1));
            state = await h.NewService().ExecuteAsync(1, TowerDefenseAction.GetRun, new());
            Assert.Equal(wave, state.Run.LastWave.Wave);
            Assert.Equal(state, await h.Get());
            if (wave < 3) Assert.Equal(3, state.Run.Offer.CardIds.Distinct(StringComparer.Ordinal).Count());
        }
        Assert.True(state.Settlement.Victory);
        Assert.Equal(100, state.Profile.WalletCoins);
        Assert.Equal(100, state.Settlement.CoinsReward);
        Assert.Equal(1, state.Profile.HighestStage);
        Assert.Equal(3, state.Settlement.WavesCleared);
        var settlement = await h.NewService().ExecuteAsync(1, TowerDefenseAction.GetSettlement, new() { RunId = state.Run.RunId });
        Assert.Equal(state, settlement);
        h.Clock.Advance(TimeSpan.FromDays(10));
        Assert.Equal(state, await h.Get());
    }

    [Fact]
    public async Task AbandonHasNoRewardAndNewRunDoesNotReplaceAnActiveRun()
    {
        var h = new Harness();
        var state = await h.Start();
        var abandon = Command(state);
        state = await h.Service.ExecuteAsync(1, TowerDefenseAction.AbandonRun, abandon);
        Assert.True(state.Settlement.Abandoned);
        Assert.False(state.Settlement.Victory);
        Assert.Equal(0, state.Profile.WalletCoins);
        var next = await h.Service.ExecuteAsync(1, TowerDefenseAction.StartRun, new() { OperationId = "next", StageId = 1, ExpectedVersion = state.Profile.Version });
        Assert.NotEqual(state.Run.RunId, next.Run.RunId);
        Assert.Equal(state, await h.Service.ExecuteAsync(1, TowerDefenseAction.AbandonRun, abandon));
        Assert.Equal(next, await h.Get());
    }

    [Fact]
    public async Task ServerDefeatGrantsNothingAndVictoryReportsActualWalletCredit()
    {
        var h = new Harness();
        var started = await h.Start();
        h.Store.Edit(1, player =>
        {
            var run = player.Run!;
            run.Phase = TowerDefensePhase.WaveRunning;
            run.Wave = 3;
            run.BaseHp = 1;
            run.Offer = null;
            run.WaveEndsAtUnixMs = h.Clock.GetUtcNow().ToUnixTimeMilliseconds();
            run.Towers.Add(new() { Id = "fixture", Slot = 0, Level = 1 });
        });
        var defeated = await h.Get();
        Assert.False(defeated.Settlement.Victory);
        Assert.False(defeated.Settlement.Abandoned);
        Assert.Equal(0, defeated.Run.BaseHp);
        Assert.Equal(0, defeated.Settlement.CoinsReward);
        Assert.Equal(0, defeated.Profile.WalletCoins);
        Assert.Equal(defeated, await h.Get());

        var next = await h.Service.ExecuteAsync(1, TowerDefenseAction.StartRun,
            new() { OperationId = "capped", ExpectedVersion = defeated.Profile.Version, StageId = 1 });
        h.Store.Edit(1, player =>
        {
            player.WalletCoins = 999_999_950;
            var run = player.Run!;
            run.Phase = TowerDefensePhase.WaveRunning;
            run.Wave = 3;
            run.Offer = null;
            run.WaveEndsAtUnixMs = h.Clock.GetUtcNow().ToUnixTimeMilliseconds();
            for (int slot = 0; slot < 5; slot++) run.Towers.Add(new() { Id = $"fixture{slot}", Slot = slot, Level = 3 });
        });
        var victory = await h.Get();
        Assert.Equal(next.Run.RunId, victory.Run.RunId);
        Assert.NotEqual(started.Run.RunId, victory.Run.RunId);
        Assert.True(victory.Settlement.Victory);
        Assert.Equal(50, victory.Settlement.CoinsReward);
        Assert.Equal(1_000_000_000, victory.Profile.WalletCoins);
    }

    [Fact]
    public async Task TowerUpgradeCapsAndStaleVersionsDoNotSpendGold()
    {
        var h = new Harness();
        var state = await h.Choose(await h.Start(), avoidBounty: true);
        state = await h.Service.ExecuteAsync(1, TowerDefenseAction.BuildTower, Command(state));
        var stale = Command(state);
        stale.TowerId = state.Run.Towers[0].TowerId;
        state = await h.Service.ExecuteAsync(1, TowerDefenseAction.UpgradeTower, stale);
        Assert.Equal(2, state.Run.Towers[0].Level);
        var changedOperation = stale.Clone(); changedOperation.OperationId = "stale";
        await Failure(ResultCode.Conflict, () => h.Service.ExecuteAsync(1, TowerDefenseAction.UpgradeTower, changedOperation));
        var upgrade = Command(state); upgrade.TowerId = state.Run.Towers[0].TowerId;
        state = await h.Service.ExecuteAsync(1, TowerDefenseAction.UpgradeTower, upgrade);
        Assert.Equal(3, state.Run.Towers[0].Level);
        Assert.Equal(25, state.Run.Gold);
        var maxed = Command(state); maxed.TowerId = state.Run.Towers[0].TowerId;
        await Failure(ResultCode.Conflict, () => h.Service.ExecuteAsync(1, TowerDefenseAction.UpgradeTower, maxed));
        Assert.Equal(state, await h.Get());
    }

    [Fact]
    public async Task UnsupportedRestoredRulesAndCanceledCommandsCannotChangeState()
    {
        var h = new Harness();
        var state = await h.Start();
        h.Store.Edit(1, p => p.Run!.RulesVersion = 2);
        await Failure(ResultCode.Unsupported, () => h.Service.ExecuteAsync(1, TowerDefenseAction.AbandonRun, Command(state)));
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Service.ExecuteAsync(1, TowerDefenseAction.StartRun,
            new() { OperationId = "canceled", StageId = 1 }, canceled.Token).AsTask());
        Assert.Equal(1, h.Store.ReceiptCount);
        Assert.Equal(1, (await h.Get()).Profile.Version);
    }

    private static TdRequest Command(TdResponse state) => new()
    {
        OperationId = Guid.NewGuid().ToString("N"), ExpectedVersion = state.Profile.Version, RunId = state.Run.RunId
    };

    private static async Task Failure(ResultCode expected, Func<ValueTask<TdResponse>> action)
    {
        var exception = await Assert.ThrowsAsync<RequestFailureException>(() => action().AsTask());
        Assert.Equal(expected, exception.Result);
    }

    private static async Task<ResultCode> Attempt(TowerDefenseService service, TdRequest request)
    {
        try { await service.ExecuteAsync(1, TowerDefenseAction.ChooseCard, request); return ResultCode.Ok; }
        catch (RequestFailureException ex) { return ex.Result; }
    }

    private sealed class Harness
    {
        public FakeStore Store { get; } = new();
        public ManualClock Clock { get; } = new();
        public TowerDefenseService Service { get; }
        public Harness() => Service = NewService();
        public TowerDefenseService NewService() => new(Store, new() { Enabled = true, WaveDurationSeconds = 1 }, Clock);
        public ValueTask<TdResponse> Start() => Service.ExecuteAsync(1, TowerDefenseAction.StartRun, new() { OperationId = "start", StageId = 1 });
        public ValueTask<TdResponse> Get() => Service.ExecuteAsync(1, TowerDefenseAction.GetRun, new());
        public ValueTask<TdResponse> Choose(TdResponse state, bool avoidBounty = false)
        {
            var command = Command(state);
            command.OfferId = state.Run.Offer.OfferId;
            command.CardId = state.Run.Offer.CardIds.First(c => !avoidBounty || c != "bounty");
            return Service.ExecuteAsync(1, TowerDefenseAction.ChooseCard, command);
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed class FakeStore : ITowerDefenseStore
    {
        private readonly object _gate = new();
        private readonly Dictionary<long, TowerDefensePlayer> _players = [];
        private readonly Dictionary<(long Player, string Operation), TowerDefenseReceipt> _receipts = [];
        private int _readsLeft;
        private TaskCompletionSource? _readBarrier;
        public int ReceiptCount { get { lock (_gate) return _receipts.Count; } }

        public void ArmReadBarrier(int readers)
        {
            lock (_gate) { _readsLeft = readers; _readBarrier = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        }

        public async ValueTask<TowerDefensePlayer> ReadAsync(long playerId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            TowerDefensePlayer result;
            TaskCompletionSource? wait = null;
            lock (_gate)
            {
                result = _players.TryGetValue(playerId, out var state) ? Clone(state) : new() { PlayerId = playerId };
                if (_readsLeft > 0)
                {
                    wait = _readBarrier;
                    if (--_readsLeft == 0) { _readBarrier!.SetResult(); _readBarrier = null; }
                }
            }
            if (wait != null) await wait.Task.WaitAsync(ct);
            return result;
        }

        public ValueTask<TowerDefenseReceipt?> ReadReceiptAsync(long playerId, string operationId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
                return ValueTask.FromResult(_receipts.TryGetValue((playerId, operationId), out var receipt)
                    ? new TowerDefenseReceipt(receipt.Fingerprint, receipt.Response.Clone()) : null);
        }

        public ValueTask<bool> TryCommitAsync(TowerDefenseWrite write, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                long version = _players.GetValueOrDefault(write.PlayerId)?.Version ?? 0;
                if (version != write.ExpectedVersion || _receipts.ContainsKey((write.PlayerId, write.OperationId))) return ValueTask.FromResult(false);
                _players[write.PlayerId] = Clone(write.Player);
                _receipts[(write.PlayerId, write.OperationId)] = new(write.Fingerprint, write.Response.Clone());
                return ValueTask.FromResult(true);
            }
        }

        public void Edit(long playerId, Action<TowerDefensePlayer> change) { lock (_gate) change(_players[playerId]); }
        private static TowerDefensePlayer Clone(TowerDefensePlayer player) => JsonSerializer.Deserialize<TowerDefensePlayer>(JsonSerializer.Serialize(player))!;
    }
}
