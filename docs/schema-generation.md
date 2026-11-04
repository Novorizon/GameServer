# 从 MySQL / SQLite 数据表生成仓储代码

`tools/GameServer.SchemaGenerator` 是独立开发工具，读取现有数据库的表结构，生成当前 `GameServer.Data.Tables.TableRepository` 可直接使用的模型和仓储。服务器启动、接收客户端连接时均不运行生成器，不增加每连接开销。

## 生成结果

每张表输出三个文件，例如 `example_inventory`：

| 文件 | 内容 | 再次生成 |
| --- | --- | --- |
| `ExampleInventoryRow.g.cs` | `[Table]`、`[Column]`、主键、自增、可空、计算列和 C# 属性 | 相同内容无需覆盖；结构变化需要 `--force` |
| `ExampleInventoryRepository.g.cs` | 继承通用仓储，提供构造函数及现有 CRUD | 同上 |
| `ExampleInventoryRepository.cs` | `partial class` 手写扩展入口 | 已存在时始终保留，包括使用 `--force` |

生成仓储自带 `GetAsync`、`InsertAsync`、`UpdateAsync`、`DeleteAsync`、`ListAsync`、`FindByAsync`、`CountAsync`。连接、事务和参数化 SQL 都复用当前数据层，不在每个表里重复封装驱动。

可编译示例位于 `examples/GameServer.DatabaseExample/Generated/MySql` 与 `Generated/Sqlite`：各有背包、玩家设置两张表。它们是从本地示例数据库读取结构后生成的，使用独立命名空间，不替换现有生产仓储。

## 使用

先按 README 配置 .NET SDK，以下命令在工程根目录执行：

```bash
dotnet build tools/GameServer.SchemaGenerator -c Release -m:1 --disable-build-servers -tl:off -p:RestoreLockedMode=true

# SQLite：已有文件，工具强制以只读方式打开，不会创建或修改数据库。
export GAMESERVER_SCHEMA_CONNECTION_STRING='Data Source=/absolute/path/game.sqlite'
dotnet tools/GameServer.SchemaGenerator/bin/Release/net10.0/GameServer.SchemaGenerator.dll \
  --provider Sqlite \
  --tables account,tcp_player_progress,tcp_facebook_binding \
  --namespace GameServer.Infrastructure.Generated \
  --output src/GameServer.Infrastructure/Generated \
  --dry-run
```

去掉 `--dry-run` 才写出代码。表结构变化后再次执行，增加 `--force` 更新生成文件；它仅覆盖带本生成器标记的 `.g.cs` 文件，不覆盖其他来源文件或手写扩展。

MySQL 改为 `--provider MySql`，连接字符串通过同一环境变量注入，例如：

```bash
export GAMESERVER_SCHEMA_CONNECTION_STRING='Server=127.0.0.1;Port=3306;Database=game;User ID=schema_reader;Password=<从凭据配置注入>'
```

使用能够读取所选库表元数据的账号。工具只查询 `information_schema`，不读业务行、不执行建表/迁移/修改数据 SQL。连接字符串不会写入生成文件或控制台；驱动异常也不直接输出，避免包含凭据。一次明确选择 1～256 张表，只处理当前 MySQL 库或 SQLite `main` 库，进程总超时 2 分钟，支持 Ctrl+C。

生成前会完整验证所选表和所有目标文件，再开始写入；单文件使用临时文件加原子替换。整个目录不是文件系统事务，意外中断后可重跑。表删除、表改名不会自动删除旧代码，请在版本控制中检查并手动移除旧文件。不要同时启动多个生成进程写同一目录。

## 调用及扩展

以下采用已生成的示例命名空间，项目需引用 `GameServer.Data`：

```csharp
using GameServer.DatabaseExample.Generated.MySql;

var items = new ExampleInventoryRepository(database); // IDatabase 或事务中的 IDatabaseSession
var row = new ExampleInventoryRow
{
    PlayerId = playerId, ItemCode = "potion", Quantity = 5
};
long id = await items.InsertAsync(row, ct);
var loaded = await items.GetAsync(id, ct);
var mine = await items.FindByAsync(nameof(ExampleInventoryRow.PlayerId), playerId, ct: ct);
```

在对应 `ExampleInventoryRepository.cs` 中加入专用方法，例如有数量条件的扣减：

```csharp
// 放在 partial class ExampleInventoryRepository 内。
public async ValueTask<bool> TryConsumeAsync(long id, long playerId, int count,
    CancellationToken ct = default)
{
    if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
    return await Database.ExecuteAsync(new GameServer.Data.SqlStatement(
        $"UPDATE {TableName} SET {Quote("quantity")}={Quote("quantity")}-@count " +
        $"WHERE {Quote("id")}=@id AND {Quote("player_id")}=@player AND {Quote("quantity")}>=@count",
        ("count", count), ("id", id), ("player", playerId)), ct) == 1;
}
```

多个表要共同提交时，使用 `database.InTransactionAsync`，在回调中用同一个 `session` 构造各仓储；不要在回调里继续用根 `database` 构造的仓储。依赖注入可注册 `services.AddSingleton<ExampleInventoryRepository>()`，前提是根容器已注册 `IDatabaseExecutor`。应用仍需自己验证玩家身份、所有权和业务条件，生成器不推断这些规则。

## 类型与边界

- 仅支持单个、非空、非计算主键；不把普通唯一索引猜成主键。复合主键、无主键、可空主键明确报错，不输出错误的 CRUD。
- 表名、列名须符合现有映射器的 `[A-Za-z_][A-Za-z0-9_]*` 限制。下划线名转为 PascalCase，保留数据库原列名注解；转换后类名或属性重名会报错。与 `Object` 成员重名的属性追加 `Value`。
- MySQL `INT` → `int`，`INT UNSIGNED` → `long`，`BIGINT` → `long`，`BIGINT UNSIGNED` → `ulong`；有符号 `TINYINT(1)` → `bool`。运行时连接请保持 `TreatTinyAsBoolean=true` 默认值；若该列实际存 0/1 以外数值，应修改表声明或手写映射。
- SQLite 常见整数声明 → `long`，避免把其 64 位值缩成 `int`；只有真正别名为 rowid 的 `INTEGER PRIMARY KEY` 标注自增，正确区分 `WITHOUT ROWID` 和 `INTEGER PRIMARY KEY DESC`。
- 字符串 → `string`，二进制 → `byte[]`，布尔 → `bool`，常见日期 → `DateTime`，MySQL `TIME` → `TimeSpan`，小数 → `decimal`，浮点数按提供程序映射；可空列加 `?`。SQLite 是动态类型数据库，已有数据仍须符合所声明类型；其 NUMERIC/REAL 存储不保证十进制精确金额，金额宜用整数最小单位。MySQL 零日期和特殊 Guid/日期转换配置需要人工处理。
- MySQL 自增键目前必须映射为 `int/long`，`BIGINT UNSIGNED AUTO_INCREMENT` 会报错；无类型的 SQLite 列、空间类型等不猜测映射。未知类型请手写模型，或扩展 `CodeGenerator.MapType` 并补测试。
- 识别 MySQL 存储/虚拟计算列和 SQLite 生成列，标注 `Computed`，查询时读取、写入时排除。自增键为默认 0 时由数据库生成，插入后回填。
- **数据库 DEFAULT 表达式不转为 C# 业务默认值。** 普通列会随实体一起插入，调用方必须赋好必需字段；`string.Empty`、`0`、`false` 不代表数据库默认值。计算列和自增键例外。
- 外键导航、索引专用查询、校验规则、缓存、权限、版本号 CAS、跨表业务和数据库迁移不会自动生成。通用 `UpdateAsync` 更新整行，不能替代有版本条件的并发更新。模型的手写派生属性请标 `[NotMapped]`。
- 每个生成文件包含元数据 SHA256，结果可重复生成。建表/迁移结束后再生成，并审查差异、编译和测试；不建议在并发 DDL 修改期间生成。

## 验证

自动测试从真实 MySQL 与 SQLite 表读取结构，将输出放进临时 C# 项目并调用 .NET 编译器，再加载生成程序集执行两种数据库的插入、查询、修改、删除；覆盖可空、BLOB、布尔、计算列与 SQL 注入样式字符串。另测生成覆盖保护、主键特殊情况和不支持结构拒绝。全套测试命令见 README。
