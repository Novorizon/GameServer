using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GameServer.Infrastructure.Persistence.Tables;

[Table("account")]
public sealed class AccountRow
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }
    public string DeviceId { get; set; } = "";
}
