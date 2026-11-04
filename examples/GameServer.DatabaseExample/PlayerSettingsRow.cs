using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GameServer.DatabaseExample;

[Table("example_player_settings")]
public sealed class PlayerSettingsRow
{
    [Key] public long PlayerId { get; set; }
    public string Language { get; set; } = "en";
    public bool SoundEnabled { get; set; } = true;
}
