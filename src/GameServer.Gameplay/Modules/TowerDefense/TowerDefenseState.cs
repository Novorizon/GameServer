using GameServer.Protocol.Messages;

namespace GameServer.Gameplay.Modules.TowerDefense;

// This bounded aggregate is server-owned. None of these fields come from a save upload.
public sealed class TowerDefensePlayer
{
    public long PlayerId { get; set; }
    public long Version { get; set; }
    public long WalletCoins { get; set; }
    public int HighestStage { get; set; }
    public TowerDefenseRun? Run { get; set; }
    public TowerDefenseSettlement? Settlement { get; set; }
}

public enum TowerDefensePhase { ChoosingCard = 1, Preparation = 2, WaveRunning = 3, Completed = 4 }

public sealed class TowerDefenseRun
{
    public string Id { get; set; } = "";
    public int StageId { get; set; }
    public int RulesVersion { get; set; } = 1;
    public TowerDefensePhase Phase { get; set; }
    public int Wave { get; set; } = 1;
    public int Gold { get; set; } = 150;
    public int BaseHp { get; set; } = 100;
    public List<TowerDefenseTower> Towers { get; set; } = [];
    public Dictionary<string, int> Cards { get; set; } = new(StringComparer.Ordinal);
    public TowerDefenseOffer? Offer { get; set; }
    public long WaveEndsAtUnixMs { get; set; }
    public TowerDefenseWave? LastWave { get; set; }
}

public sealed class TowerDefenseTower
{
    public string Id { get; set; } = "";
    public int Slot { get; set; }
    public int Level { get; set; } = 1;
}

public sealed class TowerDefenseOffer
{
    public string Id { get; set; } = "";
    public List<string> CardIds { get; set; } = [];
}

public sealed class TowerDefenseWave
{
    public int Wave { get; set; }
    public int Defense { get; set; }
    public int Threat { get; set; }
    public int Damage { get; set; }
    public int GoldReward { get; set; }
}

public sealed class TowerDefenseSettlement
{
    public string RunId { get; set; } = "";
    public bool Victory { get; set; }
    public bool Abandoned { get; set; }
    public int WavesCleared { get; set; }
    public int CoinsReward { get; set; }
}

public sealed record TowerDefenseReceipt(string Fingerprint, TdResponse Response);

public sealed record TowerDefenseWrite(long PlayerId, long ExpectedVersion, TowerDefensePlayer Player,
    string OperationId, string Fingerprint, TdResponse Response);

public interface ITowerDefenseStore
{
    ValueTask<TowerDefensePlayer> ReadAsync(long playerId, CancellationToken ct);
    ValueTask<TowerDefenseReceipt?> ReadReceiptAsync(long playerId, string operationId, CancellationToken ct);

    // Atomically CAS the aggregate and insert the immutable receipt. False changes neither.
    ValueTask<bool> TryCommitAsync(TowerDefenseWrite write, CancellationToken ct);
}
