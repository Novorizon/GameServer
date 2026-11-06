using GameServer.Protocol.Framing;
using GameServer.Protocol.Messages;
using Google.Protobuf;
using Xunit;

namespace GameServer.IntegrationTests;

public sealed class PlayerBusinessTcpTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthenticatedIdentityOwnsRunsAndCloudSavesCannotGrantBusinessRewards(bool encrypted)
    {
        await using var fixture = new PlayerBusinessFixture();
        await fixture.InitializeAsync();
        await using var server = await fixture.StartAsync(encrypted);
        using var anonymous = await server.ConnectAsync();
        await anonymous.SendAsync((int)TowerDefenseMessageId.TdGetProfile, new TdRequest(), ResultCode.NotAuthenticated);

        using var first = await server.ConnectAsync(8401);
        using var second = await server.ConnectAsync(8402);
        TdResponse initial = await first.BusinessAsync(TowerDefenseMessageId.TdGetProfile);
        Assert.Equal(0, initial.Profile.Version);
        Assert.Equal(0, initial.Profile.WalletCoins);
        Assert.Null(initial.Run);
        TdResponse started = await first.BusinessAsync(TowerDefenseMessageId.TdStartRun,
            new() { OperationId = "owner-start", ExpectedVersion = initial.Profile.Version, StageId = 1 });

        await second.SendAsync((int)TowerDefenseMessageId.TdGetRun, new TdRequest { RunId = started.Run.RunId }, ResultCode.NotFound);
        await second.SendAsync((int)TowerDefenseMessageId.TdChooseCard, new TdRequest
        {
            OperationId = "steal-card", ExpectedVersion = 0, RunId = started.Run.RunId,
            OfferId = started.Run.Offer.OfferId, CardId = started.Run.Offer.CardIds[0]
        }, ResultCode.NotFound);
        TdResponse other = await second.BusinessAsync(TowerDefenseMessageId.TdGetProfile);
        Assert.Equal(0, other.Profile.Version);
        Assert.Equal(0, other.Profile.WalletCoins);
        Assert.Null(other.Run);

        var claimedReward = ByteString.CopyFromUtf8("{\"walletCoins\":999999999,\"highestStage\":999,\"victory\":true,\"runId\":\"" + started.Run.RunId + "\"}");
        SaveResponse saved = SaveResponse.Parser.ParseFrom(await first.SendAsync((int)MessageId.SaveWrite,
            new SaveWriteRequest { Data = claimedReward, ExpectedVersion = 0, OperationId = "opaque-client-save" }));
        Assert.Equal(1, saved.Version);
        SaveResponse reread = SaveResponse.Parser.ParseFrom(await first.SendAsync((int)MessageId.SaveRead, new SaveReadRequest()));
        Assert.Equal(claimedReward, reread.Data);
        Assert.Equal(1, reread.Version);
        Assert.Equal(started, await first.BusinessAsync(TowerDefenseMessageId.TdGetRun, new() { RunId = started.Run.RunId }));
        await second.SendAsync((int)MessageId.SaveRead, new SaveReadRequest(), ResultCode.NotFound);
        await second.SendAsync((int)MessageId.SaveWrite,
            new SaveWriteRequest { Data = ByteString.CopyFromUtf8("other-player"), ExpectedVersion = 0, OperationId = "opaque-client-save" });
        Assert.Equal(claimedReward, SaveResponse.Parser.ParseFrom(await first.SendAsync((int)MessageId.SaveRead, new SaveReadRequest())).Data);

        // The legacy progress writer is disabled when the authoritative business module is enabled.
        await first.SendAsync((int)MessageId.ProgressWrite,
            new ProgressWriteRequest { Progress = int.MaxValue, ExpectedVersion = 0 }, ResultCode.Unsupported);
        first.Dispose();
        using var reconnected = await server.ConnectAsync(8401);
        Assert.Equal(started, await reconnected.BusinessAsync(TowerDefenseMessageId.TdGetProfile));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ThreeCardTowerLoopEnforcesVersionsTimersAndExactlyOneVictoryReward(bool encrypted)
    {
        await using var fixture = new PlayerBusinessFixture();
        await fixture.InitializeAsync();
        await using var server = await fixture.StartAsync(encrypted);
        using var client = await server.ConnectAsync(8401);
        var startRequest = new TdRequest { OperationId = "start", ExpectedVersion = 0, StageId = 1 };
        TdResponse current = await client.BusinessAsync(TowerDefenseMessageId.TdStartRun, startRequest);
        Assert.Equal(1, current.Profile.Version);
        AssertOffer(current.Run);
        Assert.Equal(current, await client.BusinessAsync(TowerDefenseMessageId.TdStartRun, startRequest));
        await client.SendAsync((int)TowerDefenseMessageId.TdStartRun,
            new TdRequest { OperationId = "start", ExpectedVersion = 1, StageId = 1 }, ResultCode.Conflict);
        await client.SendAsync((int)TowerDefenseMessageId.TdStartRun,
            new TdRequest { OperationId = "stale-start", ExpectedVersion = 0, StageId = 1 }, ResultCode.Conflict);
        await client.SendAsync((int)TowerDefenseMessageId.TdStartRun,
            new TdRequest { OperationId = "second-active-run", ExpectedVersion = 1, StageId = 1 }, ResultCode.Conflict);
        await client.SendAsync((int)TowerDefenseMessageId.TdStartWave, Mutation(current, "before-choice"), ResultCode.Conflict);
        await client.SendAsync((int)TowerDefenseMessageId.TdChooseCard,
            Choice(current, "forged-card", "not-an-offered-card"), ResultCode.Conflict);

        TdRequest choice = Choice(current, "choose-first");
        current = await client.BusinessAsync(TowerDefenseMessageId.TdChooseCard, choice);
        Assert.Equal(current, await client.BusinessAsync(TowerDefenseMessageId.TdChooseCard, choice));
        Assert.Equal(TdRunPhase.TdPreparation, current.Run.Phase);
        Assert.Null(current.Run.Offer);
        Assert.Equal(choice.CardId, Assert.Single(current.Run.Cards).CardId);
        Assert.Equal(1, current.Run.Cards[0].Rank);
        await client.SendAsync((int)TowerDefenseMessageId.TdBuildTower,
            new TdRequest { OperationId = "stale-build", ExpectedVersion = current.Profile.Version - 1, RunId = current.Run.RunId, Slot = 0 }, ResultCode.Conflict);
        TdRequest invalidSlot = Mutation(current, "invalid-slot"); invalidSlot.Slot = 5;
        await client.SendAsync((int)TowerDefenseMessageId.TdBuildTower, invalidSlot, ResultCode.InvalidRequest);
        current = await BuildThreeTowersAsync(client, current);
        TdRequest duplicateSlot = Mutation(current, "occupied-slot"); duplicateSlot.Slot = 0;
        await client.SendAsync((int)TowerDefenseMessageId.TdBuildTower, duplicateSlot, ResultCode.Conflict);

        string runId = current.Run.RunId;
        for (int wave = 1; wave <= 3; wave++)
        {
            if (wave > 1)
            {
                AssertOffer(current.Run);
                current = await client.BusinessAsync(TowerDefenseMessageId.TdChooseCard, Choice(current, "choose-" + wave));
                if (wave == 2)
                {
                    TdRequest upgrade = Mutation(current, "upgrade"); upgrade.TowerId = current.Run.Towers[0].TowerId;
                    int gold = current.Run.Gold;
                    current = await client.BusinessAsync(TowerDefenseMessageId.TdUpgradeTower, upgrade);
                    Assert.Equal(2, current.Run.Towers[0].Level);
                    Assert.Equal(gold - 25, current.Run.Gold);
                }
            }
            Assert.Equal(wave, current.Run.Wave);
            long previousVersion = current.Profile.Version;
            TdRequest startWave = Mutation(current, "wave-" + wave);
            current = await client.BusinessAsync(TowerDefenseMessageId.TdStartWave, startWave);
            Assert.Equal(previousVersion + 1, current.Profile.Version);
            Assert.Equal(TdRunPhase.TdWaveRunning, current.Run.Phase);
            Assert.Equal(fixture.Clock.GetUtcNow().AddSeconds(5).ToUnixTimeMilliseconds(), current.Run.WaveEndsAtUnixMs);
            Assert.Equal(current, await client.BusinessAsync(TowerDefenseMessageId.TdGetRun, new() { RunId = runId }));
            await client.SendAsync((int)TowerDefenseMessageId.TdStartWave, Mutation(current, "duplicate-wave-" + wave), ResultCode.Conflict);
            fixture.Clock.Advance(TimeSpan.FromSeconds(4));
            Assert.Equal(current, await client.BusinessAsync(TowerDefenseMessageId.TdGetRun, new() { RunId = runId }));
            fixture.Clock.Advance(TimeSpan.FromSeconds(1));
            int goldBeforeResolution = current.Run.Gold;
            long runningVersion = current.Profile.Version;
            current = await client.BusinessAsync(TowerDefenseMessageId.TdGetRun, new() { RunId = runId });
            Assert.Equal(runningVersion + 1, current.Profile.Version);
            Assert.Equal(0, current.Run.WaveEndsAtUnixMs);
            Assert.Equal(wave, current.Run.LastWave.Wave);
            Assert.Equal(35, current.Run.LastWave.GoldReward);
            Assert.Equal(goldBeforeResolution + 35, current.Run.Gold);
            Assert.True(current.Run.BaseHp > 0);
            Assert.Equal(wave == 3 ? TdRunPhase.TdCompleted : TdRunPhase.TdChoosingCard, current.Run.Phase);
            Assert.Equal(wave == 3 ? 100 : 0, current.Profile.WalletCoins);
            Assert.Equal(current, await client.BusinessAsync(TowerDefenseMessageId.TdGetRun, new() { RunId = runId }));
        }
        Assert.Equal(1, current.Profile.HighestStage);
        Assert.True(current.Settlement.Victory);
        Assert.False(current.Settlement.Abandoned);
        Assert.Equal(3, current.Settlement.WavesCleared);
        Assert.Equal(100, current.Settlement.CoinsReward);
        for (int retry = 0; retry < 3; retry++)
            Assert.Equal(current, await client.BusinessAsync(TowerDefenseMessageId.TdGetSettlement, new() { RunId = runId }));

        TdResponse next = await client.BusinessAsync(TowerDefenseMessageId.TdStartRun,
            new() { OperationId = "next-run", ExpectedVersion = current.Profile.Version, StageId = 1 });
        Assert.NotEqual(runId, next.Run.RunId);
        Assert.Equal(100, next.Profile.WalletCoins);
        TdResponse originalReplay = await client.BusinessAsync(TowerDefenseMessageId.TdStartRun, startRequest);
        Assert.Equal(runId, originalReplay.Run.RunId);
        Assert.Equal(1, originalReplay.Profile.Version);
        Assert.Equal(next, await client.BusinessAsync(TowerDefenseMessageId.TdGetRun));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SqliteRestartRestoresOfferActiveWaveReceiptsSavesAndFinalReward(bool encrypted)
    {
        await using var fixture = new PlayerBusinessFixture();
        await fixture.InitializeAsync();
        TdResponse preparing;
        TdResponse running;
        TdRequest startWave;
        TdRequest buildReplay;
        TdResponse buildResponse;
        var saveRequest = new SaveWriteRequest { Data = ByteString.CopyFromUtf8("durable-client-save"), ExpectedVersion = 0, OperationId = "persist-save" };
        await using (var server = await fixture.StartAsync(encrypted))
        {
            using var first = await server.ConnectAsync(8401);
            preparing = await first.BusinessAsync(TowerDefenseMessageId.TdStartRun,
                new() { OperationId = "persist-start", ExpectedVersion = 0, StageId = 1 });
            TdCardOffer originalOffer = preparing.Run.Offer.Clone();
            first.Dispose();
            using var reconnected = await server.ConnectAsync(8401);
            TdResponse restored = await reconnected.BusinessAsync(TowerDefenseMessageId.TdGetRun);
            Assert.Equal(preparing, restored);
            Assert.Equal(originalOffer, restored.Run.Offer);
            preparing = await reconnected.BusinessAsync(TowerDefenseMessageId.TdChooseCard, Choice(restored, "persist-choice"));
            buildReplay = Mutation(preparing, "persist-build"); buildReplay.Slot = 0;
            buildResponse = await reconnected.BusinessAsync(TowerDefenseMessageId.TdBuildTower, buildReplay);
            preparing = buildResponse;
            for (int slot = 1; slot < 3; slot++)
            {
                TdRequest build = Mutation(preparing, "persist-build-" + slot); build.Slot = slot;
                preparing = await reconnected.BusinessAsync(TowerDefenseMessageId.TdBuildTower, build);
            }
            startWave = Mutation(preparing, "persist-wave-1");
            running = await reconnected.BusinessAsync(TowerDefenseMessageId.TdStartWave, startWave);
            await reconnected.SendAsync((int)MessageId.SaveWrite, saveRequest);
        }

        // Restart before the deadline: persisted wave and mutation receipts are unchanged.
        await using (var server = await fixture.StartAsync(encrypted))
        {
            using var client = await server.ConnectAsync(8401);
            Assert.Equal(running, await client.BusinessAsync(TowerDefenseMessageId.TdGetRun));
            Assert.Equal(buildResponse, await client.BusinessAsync(TowerDefenseMessageId.TdBuildTower, buildReplay));
            Assert.Equal(running, await client.BusinessAsync(TowerDefenseMessageId.TdStartWave, startWave));
            Assert.Equal(running, await client.BusinessAsync(TowerDefenseMessageId.TdGetRun));
            TdRequest alteredReplay = buildReplay.Clone(); alteredReplay.Slot = 4;
            await client.SendAsync((int)TowerDefenseMessageId.TdBuildTower, alteredReplay, ResultCode.Conflict);
            SaveResponse saved = SaveResponse.Parser.ParseFrom(await client.SendAsync((int)MessageId.SaveRead, new SaveReadRequest()));
            Assert.Equal(saveRequest.Data, saved.Data);
            Assert.Equal(1, saved.Version);
            SaveResponse replayedSave = SaveResponse.Parser.ParseFrom(await client.SendAsync((int)MessageId.SaveWrite, saveRequest));
            Assert.Equal(1, replayedSave.Version);
        }

        // An elapsed wave resolves on the next request even while its player was offline.
        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        TdResponse completed;
        await using (var server = await fixture.StartAsync(encrypted))
        {
            using var client = await server.ConnectAsync(8401);
            TdResponse current = await client.BusinessAsync(TowerDefenseMessageId.TdGetRun);
            Assert.Equal(running.Profile.Version + 1, current.Profile.Version);
            Assert.Equal(TdRunPhase.TdChoosingCard, current.Run.Phase);
            Assert.Equal(2, current.Run.Wave);
            Assert.Equal(3, current.Run.Towers.Count);
            Assert.Equal(preparing.Run.Gold + 35, current.Run.Gold);
            Assert.Equal(running, await client.BusinessAsync(TowerDefenseMessageId.TdStartWave, startWave));
            Assert.Equal(current, await client.BusinessAsync(TowerDefenseMessageId.TdGetRun));
            for (int wave = 2; wave <= 3; wave++)
            {
                current = await client.BusinessAsync(TowerDefenseMessageId.TdChooseCard, Choice(current, "offline-choice-" + wave));
                current = await client.BusinessAsync(TowerDefenseMessageId.TdStartWave, Mutation(current, "offline-wave-" + wave));
                fixture.Clock.Advance(TimeSpan.FromSeconds(5));
                current = await client.BusinessAsync(TowerDefenseMessageId.TdGetRun);
            }
            completed = current;
            Assert.True(completed.Settlement.Victory);
            Assert.Equal(100, completed.Profile.WalletCoins);
        }

        await using (var server = await fixture.StartAsync(encrypted))
        {
            using var client = await server.ConnectAsync(8401);
            Assert.Equal(completed, await client.BusinessAsync(TowerDefenseMessageId.TdGetSettlement, new() { RunId = completed.Run.RunId }));
            fixture.Clock.Advance(TimeSpan.FromDays(1));
            Assert.Equal(completed, await client.BusinessAsync(TowerDefenseMessageId.TdGetProfile));
            TdResponse next = await client.BusinessAsync(TowerDefenseMessageId.TdStartRun,
                new() { OperationId = "abandon-next-start", ExpectedVersion = completed.Profile.Version, StageId = 1 });
            TdRequest abandon = Mutation(next, "abandon-next");
            TdResponse abandoned = await client.BusinessAsync(TowerDefenseMessageId.TdAbandonRun, abandon);
            Assert.True(abandoned.Settlement.Abandoned);
            Assert.False(abandoned.Settlement.Victory);
            Assert.Equal(0, abandoned.Settlement.CoinsReward);
            Assert.Equal(100, abandoned.Profile.WalletCoins);
            Assert.Equal(abandoned, await client.BusinessAsync(TowerDefenseMessageId.TdAbandonRun, abandon));
            Assert.Equal(abandoned, await client.BusinessAsync(TowerDefenseMessageId.TdGetSettlement, new() { RunId = next.Run.RunId }));
        }
    }

    private static TdRequest Mutation(TdResponse current, string operationId) =>
        new() { OperationId = operationId, ExpectedVersion = current.Profile.Version, RunId = current.Run.RunId };

    private static TdRequest Choice(TdResponse current, string operationId, string? cardId = null)
    {
        AssertOffer(current.Run);
        TdRequest request = Mutation(current, operationId);
        request.OfferId = current.Run.Offer.OfferId;
        // Avoid the bounty card so economy assertions do not depend on a random offer.
        request.CardId = cardId ?? current.Run.Offer.CardIds.First(id => id != "bounty");
        return request;
    }

    private static void AssertOffer(TdRun run)
    {
        Assert.Equal(TdRunPhase.TdChoosingCard, run.Phase);
        Assert.NotNull(run.Offer);
        Assert.NotEmpty(run.Offer.OfferId);
        Assert.Equal(3, run.Offer.CardIds.Count);
        Assert.Equal(3, run.Offer.CardIds.Distinct(StringComparer.Ordinal).Count());
    }

    private static async Task<TdResponse> BuildThreeTowersAsync(PlayerBusinessClient client, TdResponse current)
    {
        for (int slot = 0; slot < 3; slot++)
        {
            int previousGold = current.Run.Gold;
            long previousVersion = current.Profile.Version;
            TdRequest build = Mutation(current, "build-" + slot); build.Slot = slot;
            current = await client.BusinessAsync(TowerDefenseMessageId.TdBuildTower, build);
            Assert.Equal(previousVersion + 1, current.Profile.Version);
            Assert.Equal(previousGold - 50, current.Run.Gold);
            Assert.Equal(slot + 1, current.Run.Towers.Count);
            Assert.Equal(1, current.Run.Towers.Single(t => t.Slot == slot).Level);
        }
        Assert.Equal(3, current.Run.Towers.Select(t => t.TowerId).Distinct(StringComparer.Ordinal).Count());
        return current;
    }
}
