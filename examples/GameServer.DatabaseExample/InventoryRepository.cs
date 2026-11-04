using GameServer.Data;
using GameServer.Data.Abstractions;
using GameServer.Data.Tables;

namespace GameServer.DatabaseExample;

// CRUD and paging are inherited. Add only table-specific business queries.
public sealed class InventoryRepository(IDatabaseExecutor database) : TableRepository<InventoryItemRow, long>(database)
{
    public ValueTask<IReadOnlyList<InventoryItemRow>> ForPlayerAsync(long playerId, CancellationToken ct = default) =>
        FindByAsync(nameof(InventoryItemRow.PlayerId), playerId, ct: ct);

    public async ValueTask<InventoryItemRow?> FindItemAsync(long playerId, string itemCode, CancellationToken ct = default)
    {
        var rows = await QueryRowsAsync(new($"SELECT {SelectColumns} FROM {TableName} WHERE player_id=@player AND item_code=@code LIMIT 1",
            ("player", playerId), ("code", itemCode)), 1, ct);
        return rows.Count == 0 ? null : rows[0];
    }

    public async ValueTask<bool> TryConsumeAsync(long id, int quantity, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        return await Database.ExecuteAsync(new($"UPDATE {TableName} SET quantity=quantity-@quantity WHERE id=@id AND quantity>=@quantity",
            ("quantity", quantity), ("id", id)), ct) == 1;
    }
}
