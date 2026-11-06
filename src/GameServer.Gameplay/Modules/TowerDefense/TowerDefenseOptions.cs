namespace GameServer.Gameplay.Modules.TowerDefense;

public sealed class TowerDefenseOptions
{
    public bool Enabled { get; set; }
    public int WaveDurationSeconds { get; set; } = 10;

    public void Validate()
    {
        if (WaveDurationSeconds is < 1 or > 300)
            throw new InvalidOperationException("TowerDefense:WaveDurationSeconds must be between 1 and 300.");
    }
}

public enum TowerDefenseAction
{
    GetProfile = 2000000101,
    StartRun = 2000000102,
    GetRun = 2000000103,
    ChooseCard = 2000000104,
    BuildTower = 2000000105,
    UpgradeTower = 2000000106,
    StartWave = 2000000107,
    AbandonRun = 2000000108,
    GetSettlement = 2000000109
}
