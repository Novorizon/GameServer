using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GameServer.DatabaseExample;

[Table("example_inventory")]
public sealed class InventoryItemRow
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)] public long Id { get; set; }
    public long PlayerId { get; set; }
    public string ItemCode { get; set; } = "";
    public int Quantity { get; set; }
    public int? Quality { get; set; }
}
