using System.Security.Cryptography;
using System.Text;
using GameServer.Gameplay.Dispatching;
using GameServer.Protocol.Framing;
using GameServer.Protocol.Messages;
using Google.Protobuf;

namespace GameServer.Gameplay.Modules.TowerDefense;

public sealed class TowerDefenseService(ITowerDefenseStore store, TowerDefenseOptions options, TimeProvider timeProvider)
{
    private static readonly string[] CardPool = ["attack", "haste", "armor", "repair", "bounty", "veteran"];

    public async ValueTask<TdResponse> ExecuteAsync(long playerId, TowerDefenseAction action, TdRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (playerId <= 0) throw Failure(ResultCode.NotAuthenticated);
        ValidateShape(request);
        if (action is TowerDefenseAction.GetProfile or TowerDefenseAction.GetRun or TowerDefenseAction.GetSettlement)
        {
            var current = await ReadAndResolveAsync(playerId, ct);
            if (action == TowerDefenseAction.GetRun && request.RunId.Length > 0)
                RequireRun(current, request.RunId);
            if (action == TowerDefenseAction.GetSettlement &&
                (request.RunId.Length == 0 || current.Settlement?.RunId != request.RunId))
                throw Failure(ResultCode.NotFound);
            return Snapshot(current);
        }

        if (!Enum.IsDefined(action)) throw Failure(ResultCode.InvalidRequest);
        if (request.OperationId.Length is < 1 or > 64 ||
            request.OperationId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_') ||
            request.ExpectedVersion is < 0 or long.MaxValue)
            throw Failure(ResultCode.InvalidRequest);

        string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(((int)action).ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Concat(request.ToByteArray()).ToArray()));
        var replay = await store.ReadReceiptAsync(playerId, request.OperationId, ct);
        if (replay != null) return Replay(replay, fingerprint);

        // Expired waves are advanced only by queries. Mutation callers refresh GetRun first.
        var player = await store.ReadAsync(playerId, ct);
        if (player.Version != request.ExpectedVersion) throw Failure(ResultCode.Conflict);
        Apply(player, action, request);
        player.Version++;
        var response = Snapshot(player);
        if (await store.TryCommitAsync(new(playerId, request.ExpectedVersion, player, request.OperationId, fingerprint, response), ct))
            return response;
        replay = await store.ReadReceiptAsync(playerId, request.OperationId, ct);
        if (replay != null) return Replay(replay, fingerprint);
        throw Failure(ResultCode.Conflict);
    }

    private async ValueTask<TowerDefensePlayer> ReadAndResolveAsync(long playerId, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            var player = await store.ReadAsync(playerId, ct);
            var run = player.Run;
            if (run == null || run.Phase != TowerDefensePhase.WaveRunning || timeProvider.GetUtcNow().ToUnixTimeMilliseconds() < run.WaveEndsAtUnixMs)
                return player;
            long version = player.Version;
            if (version == long.MaxValue) throw Failure(ResultCode.Conflict);
            string operationId = $"wave:{run.Id}:{run.Wave}";
            ResolveWave(player, run);
            player.Version++;
            if (await store.TryCommitAsync(new(playerId, version, player, operationId, operationId, Snapshot(player)), ct))
                return player;
        }
        throw Failure(ResultCode.Conflict);
    }

    private void Apply(TowerDefensePlayer player, TowerDefenseAction action, TdRequest request)
    {
        if (action == TowerDefenseAction.StartRun)
        {
            if (request.StageId != 1) throw Failure(ResultCode.InvalidRequest);
            if (player.Run != null && player.Run.Phase != TowerDefensePhase.Completed) throw Failure(ResultCode.Conflict);
            player.Run = new() { Id = NewId(), StageId = 1, Phase = TowerDefensePhase.ChoosingCard, Offer = NewOffer(new Dictionary<string, int>()) };
            return;
        }

        var run = RequireRun(player, request.RunId);
        RequireSupportedRules(run);
        if (run.Phase == TowerDefensePhase.Completed) throw Failure(ResultCode.Conflict);
        switch (action)
        {
            case TowerDefenseAction.ChooseCard:
                if (run.Phase != TowerDefensePhase.ChoosingCard || run.Offer == null || run.Offer.Id != request.OfferId ||
                    !run.Offer.CardIds.Contains(request.CardId, StringComparer.Ordinal) || Rank(run, request.CardId) >= 3)
                    throw Failure(ResultCode.Conflict);
                run.Cards[request.CardId] = Rank(run, request.CardId) + 1;
                if (request.CardId == "repair") run.BaseHp = Math.Min(100, run.BaseHp + 25);
                if (request.CardId == "bounty") run.Gold += 35;
                run.Offer = null;
                run.Phase = TowerDefensePhase.Preparation;
                break;
            case TowerDefenseAction.BuildTower:
                RequirePreparation(run);
                if (request.Slot is < 0 or >= 5) throw Failure(ResultCode.InvalidRequest);
                if (run.Gold < 50 || run.Towers.Any(t => t.Slot == request.Slot)) throw Failure(ResultCode.Conflict);
                run.Gold -= 50;
                run.Towers.Add(new() { Id = NewId(), Slot = request.Slot });
                break;
            case TowerDefenseAction.UpgradeTower:
                RequirePreparation(run);
                var tower = run.Towers.SingleOrDefault(t => t.Id == request.TowerId) ?? throw Failure(ResultCode.NotFound);
                int cost = tower.Level * 25;
                if (tower.Level >= 3 || run.Gold < cost) throw Failure(ResultCode.Conflict);
                run.Gold -= cost;
                tower.Level++;
                break;
            case TowerDefenseAction.StartWave:
                RequirePreparation(run);
                if (run.Towers.Count == 0) throw Failure(ResultCode.Conflict);
                run.WaveEndsAtUnixMs = timeProvider.GetUtcNow().AddSeconds(options.WaveDurationSeconds).ToUnixTimeMilliseconds();
                run.Phase = TowerDefensePhase.WaveRunning;
                break;
            case TowerDefenseAction.AbandonRun:
                run.Phase = TowerDefensePhase.Completed;
                run.Offer = null;
                run.WaveEndsAtUnixMs = 0;
                player.Settlement = new() { RunId = run.Id, Abandoned = true, WavesCleared = run.Wave - 1 };
                break;
            default:
                throw Failure(ResultCode.InvalidRequest);
        }
    }

    private static void ResolveWave(TowerDefensePlayer player, TowerDefenseRun run)
    {
        RequireSupportedRules(run);
        int threat = run.Wave switch { 1 => 18, 2 => 28, _ => 42 };
        int defense = run.Towers.Sum(t => t.Level * 10) + run.Towers.Count * (Rank(run, "attack") * 4 + Rank(run, "haste") * 3) + Rank(run, "veteran") * 6;
        int damage = Math.Max(0, threat - defense) * 3 * (100 - Rank(run, "armor") * 20) / 100;
        run.BaseHp = Math.Max(0, run.BaseHp - damage);
        run.LastWave = new() { Wave = run.Wave, Defense = defense, Threat = threat, Damage = damage, GoldReward = run.BaseHp > 0 ? 35 : 0 };
        run.Gold += run.LastWave.GoldReward;
        run.WaveEndsAtUnixMs = 0;
        if (run.BaseHp == 0 || run.Wave == 3)
        {
            bool victory = run.BaseHp > 0;
            int reward = victory ? (int)Math.Clamp(1_000_000_000L - player.WalletCoins, 0, 100) : 0;
            player.WalletCoins += reward;
            if (victory) player.HighestStage = Math.Max(player.HighestStage, run.StageId);
            player.Settlement = new() { RunId = run.Id, Victory = victory, WavesCleared = victory ? 3 : run.Wave - 1, CoinsReward = reward };
            run.Phase = TowerDefensePhase.Completed;
            run.Offer = null;
        }
        else
        {
            run.Wave++;
            run.Phase = TowerDefensePhase.ChoosingCard;
            run.Offer = NewOffer(run.Cards);
        }
    }

    private static TowerDefenseOffer NewOffer(IReadOnlyDictionary<string, int> cards)
    {
        var candidates = CardPool.Where(id => !cards.TryGetValue(id, out int rank) || rank < 3).ToArray();
        RandomNumberGenerator.Shuffle(candidates.AsSpan());
        return new() { Id = NewId(), CardIds = candidates.Take(3).ToList() };
    }

    private static TowerDefenseRun RequireRun(TowerDefensePlayer player, string runId)
    {
        if (runId.Length == 0 || player.Run?.Id != runId) throw Failure(ResultCode.NotFound);
        return player.Run;
    }

    private static void RequirePreparation(TowerDefenseRun run)
    {
        if (run.Phase != TowerDefensePhase.Preparation) throw Failure(ResultCode.Conflict);
    }

    private static void RequireSupportedRules(TowerDefenseRun run)
    {
        if (run.RulesVersion != 1) throw Failure(ResultCode.Unsupported);
    }

    private static int Rank(TowerDefenseRun run, string cardId) => run.Cards.GetValueOrDefault(cardId);
    private static string NewId() => Guid.NewGuid().ToString("N");
    private static RequestFailureException Failure(ResultCode code) => new(code);
    private static TdResponse Replay(TowerDefenseReceipt receipt, string fingerprint) =>
        receipt.Fingerprint == fingerprint ? receipt.Response.Clone() : throw Failure(ResultCode.Conflict);

    private static void ValidateShape(TdRequest request)
    {
        if (request.OperationId.Length > 64 || request.RunId.Length > 32 || request.OfferId.Length > 32 ||
            request.CardId.Length > 32 || request.TowerId.Length > 32)
            throw Failure(ResultCode.InvalidRequest);
    }

    public static TdResponse Snapshot(TowerDefensePlayer player)
    {
        var response = new TdResponse { Profile = new() { Version = player.Version, WalletCoins = player.WalletCoins, HighestStage = player.HighestStage } };
        if (player.Run is { } run)
        {
            response.Run = new() { RunId = run.Id, StageId = run.StageId, Phase = (TdRunPhase)(int)run.Phase, Wave = run.Wave,
                Gold = run.Gold, BaseHp = run.BaseHp, WaveEndsAtUnixMs = run.WaveEndsAtUnixMs, RulesVersion = run.RulesVersion };
            response.Run.Towers.AddRange(run.Towers.Select(t => new TdTower { TowerId = t.Id, Slot = t.Slot, Level = t.Level }));
            response.Run.Cards.AddRange(run.Cards.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => new TdCardRank { CardId = c.Key, Rank = c.Value }));
            if (run.Offer is { } offer)
            {
                response.Run.Offer = new() { OfferId = offer.Id };
                response.Run.Offer.CardIds.AddRange(offer.CardIds);
            }
            if (run.LastWave is { } wave)
                response.Run.LastWave = new() { Wave = wave.Wave, Defense = wave.Defense, Threat = wave.Threat, Damage = wave.Damage, GoldReward = wave.GoldReward };
        }
        if (player.Settlement is { } settlement)
            response.Settlement = new() { RunId = settlement.RunId, Victory = settlement.Victory, Abandoned = settlement.Abandoned,
                WavesCleared = settlement.WavesCleared, CoinsReward = settlement.CoinsReward };
        return response;
    }
}
