using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GameServer.Infrastructure.Persistence.Tables;

[Table("tcp_facebook_binding")]
public sealed class FacebookBindingRow
{
    [Key] public long PlayerId { get; set; }
    public string UserId { get; set; } = "";
}
