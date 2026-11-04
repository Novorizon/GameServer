using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GameServer.Infrastructure.Persistence.Tables;

[Table("tcp_player_progress")]
public sealed class PlayerProgressRow
{
    [Key] public long PlayerId { get; set; }
    public int Progress { get; set; }
    public long Version { get; set; } = 1;
}
