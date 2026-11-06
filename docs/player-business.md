# 玩家登录、云存档与塔防业务

本轮增加了可运行的服务端业务闭环，保留原有 TCP／AES-GCM、JWT 和数据层。登录与存档细节见 [player-login-and-saves.md](player-login-and-saves.md)。本功能没有部署到线上。

## 功能范围

- 可信管理命令预置玩家、签发短期登录 JWT；TCP 登录验证账号与 device 的精确绑定。
- 玩家档案：永久金币、已通关关卡、全局业务版本。
- 数据库云存档：独立的客户端字节数据，带 CAS 版本和持久化幂等回执。
- 单人塔防局：一关三波、五个塔位、建塔／升级、开局及波次间三选一卡、胜负、放弃和一次性奖励。
- 所有权取自已认证 session。客户端存档、旧进度字段、客户端传来的奖励／伤害数据均不能修改权威战局和钱包。
- 战局及回执存入数据库，重连／重启后读取同一张候选卡面板与已提交操作结果。

这是简化规则的业务样板：没有逐怪物路径模拟、Unity 物理、实时帧推送、多人房间、公开 HTTP 注册／密码登录或支付功能。波次由服务器设定截止时间，客户端 GetRun 时按已存状态惰性计算当前波次结果；不会自动连续推进多波。断线不删除战局，也不会重置波次截止时间。

## 开启前显式迁移

默认 `TowerDefense:Enabled=false`、`Storage:Provider=File`，所以现有服务不需要立即升级客户端或迁移数据库。

新建 SQLite 开发库示例（需要 sqlite3 工具）：

```bash
sqlite3 /absolute/path/dev-game.db < sql/sqlite-schema.sql
sqlite3 /absolute/path/dev-game.db < sql/tower-defense-sqlite.sql
```

已有 SQLite 库只需显式应用 `sql/player-save-sqlite.sql` 与 `sql/tower-defense-sqlite.sql`；已有 MySQL 库应用对应 `*-mysql.sql`。新 MySQL 开发库可用 `sql/mysql-new-database.sql` 再应用塔防迁移。以上都不是自动升级生产库的命令，旧文件存档也不会自动导入数据库。

数据库存档增加 `tcp_player_save`、`tcp_player_save_receipt`；塔防增加 `tcp_td_player`、`tcp_td_receipt`。塔防使用一个大小有界的服务端 JSON 聚合保存档案与当前局，版本 CAS、永久奖励与操作回执在同一个事务中提交。内部回执表使用显式复合主键 SQL，不交给仅支持单主键的表生成器。

配置示例：

```bash
export GAMESERVER_Database__Provider=Sqlite
export GAMESERVER_Database__ConnectionString='Data Source=/absolute/path/dev-game.db'
export GAMESERVER_Storage__Provider=Database
export GAMESERVER_TowerDefense__Enabled=true
export GAMESERVER_TowerDefense__WaveDurationSeconds=10
# 再从受保护的环境配置注入 Authentication__SigningKeyBase64
# 与 Networking__Encryption__KeyBase64；两者用途不同，不交给客户端 JWT 签名密钥。
```

依 README 使用 .NET 10.0.403 构建后，按登录文档执行 `provision-player`、`issue-login-token`，再启动 Host。管理命令不会监听游戏端口；它们用于受信任的账号引导，不替代生产认证服务。

开启塔防后，旧 `ProgressWrite` Handler 不再注册，发送会收到 Unsupported 并关闭连接，避免客户端任意写进度。`ProgressRead` 仍是原表读取；新游戏的解锁和奖励应以 TdProfile 为准。客户端 SaveWrite 保留为非权威存档，数据库模式必须提供版本及 operation_id。

## 协议与典型流程

新增定义见 `protos/tower_defense.proto`。所有塔防消息要求先通过原 Login 消息认证，且使用同一个 `TdRequest`，响应为 `TdResponse`。

| 消息 | ID | 主要输入 |
|---|---|---|
| GetProfile | 2000000101 | 无，取得 Profile.Version |
| StartRun | 2000000102 | stage_id=1、operation_id、expected_version |
| GetRun | 2000000103 | 自己的 run_id，可省略以读取当前局 |
| ChooseCard | 2000000104 | run_id、offer_id、card_id、operation_id、expected_version |
| BuildTower | 2000000105 | run_id、slot（0..4）、operation_id、expected_version |
| UpgradeTower | 2000000106 | run_id、tower_id、operation_id、expected_version |
| StartWave | 2000000107 | run_id、operation_id、expected_version |
| AbandonRun | 2000000108 | run_id、operation_id、expected_version |
| GetSettlement | 2000000109 | 最近一次已完成局的 run_id |

每次响应取新的 `Profile.Version`；新玩家为 0。所有变更都使用该版本，而不是另有一份 Run.Version。operation_id 为 1–64 个 ASCII 字母、数字、`-` 或 `_`，同一玩家的每个新逻辑操作都使用新 ID。

```text
Login
  → GetProfile（version=0）
  → StartRun（expected_version=0，operation_id=start-001，stage_id=1）
  ← TdResponse：runId、三张候选、offerId、Profile.Version
  → ChooseCard（该 offerId 中的一张 cardId，新的 operation_id 与当前版本）
  → BuildTower / UpgradeTower
  → StartWave
  → 截止时间后 GetRun（服务端计算结果并提交新版本）
  → 新一波三选一卡 → 准备 → StartWave
  → 第三波终局 → GetSettlement
```

客户端可以在截止时间前查询，不会提前获得金币或奖励。GetProfile、GetRun、GetSettlement 在需要时会推进已到期的当前波次，因此查询响应版本可能变化；下一次变更必须使用最新版本。若变更遇到 Conflict，先查询状态，再决定是否发新操作；若不确定上一操作是否提交，使用原 ID、版本和内容重试。

相同操作 ID 和相同请求返回原始回执，不重复扣钱／选卡／发奖励；相同 ID 改内容返回 Conflict。回执返回的是那次操作当时的快照，不会把当前状态回滚。需要最新状态时再 GetRun。GetSettlement 仅提供最近一次终局；更早的变更回执仍保留，但本版没有历史战绩列表接口。

没有新增选卡／战局通知：本版使用请求应答和轮询，正式客户端仍应保留统一接收循环、心跳和断线重连。客户端导出脚本现在会导出所有 proto，包括 Game.cs 和 TowerDefense.cs：

```bash
python3 scripts/generate-protobuf.py --output /absolute/path/client/Generated
```

压测适配器的接收缓冲区只有 4 KiB，不是生产客户端的完整大存档接收实现。游戏客户端需遵守完整帧上限和 AES-GCM 初始化格式，不能把服务端 net10.0 DLL 直接放入 Unity。

## 已实现的规则

- 当前只允许 stage_id=1，每个玩家同一时间一局；未完成时重复开局被拒绝。
- 开局 150 局内金币、基地 100 HP。建塔 50 金币，同一格不能重建；升级费用为当前塔等级×25，最高 3 级。
- 三张不同卡由服务端从六种候选中随机生成并持久化；卡牌最高 3 级。只有三个选卡机会，卡池足以保证三张候选。
- 卡牌：attack（每塔每级防御力+4）、haste（每塔每级+3）、veteran（全局每级+6）、armor（每级减伤20%）、repair（立即回复25 HP，不超过100）、bounty（立即获得35局内金币）。卡牌上限、选择权和效果均由服务端验证。
- 三波威胁分别为 18、28、42。防御力＝塔等级总和×10＋塔数量×(attack等级×4＋haste等级×3)＋veteran等级×6。基地伤害按威胁与防御差计算，再应用 armor 减伤。
- 非致死波次获得35局内金币。通关最多授予100永久金币，并更新 HighestStage；钱包上限10亿，回执的奖励是实际入账金额。失败或放弃不授予永久金币。
- RulesVersion=1 随战局保存；遇到未来不支持的版本时拒绝推进，不能默默用另一套规则解释旧存档。

以上数值是可运行示例，当前固化在规则实现中。后续要做配置表和版本迁移，再扩展卡牌／地图；不要把客户端 JSON 上传当作配置发布或可信结算。

## 一致性与运行边界

Gameplay 的 Service 只依赖 ITowerDefenseStore，数据库实现留在 Infrastructure；每个事务的命令都使用同一个 session。数据库 CAS 和回执保证跨实例／重启的一致性，玩家锁只用于当前会话的请求协调。

收到请求后断线或超时，并不证明数据库没有提交。客户端必须按 operation_id 恢复结果。回执永久保留用于确切重试；本版没有自动删除回执、历史战绩分页或过期战局回收，运维需要计量表大小，后续做有明确客户端重试窗口的归档方案，不能随意删除幂等记录。

功能关闭／使用 File 模式时保持原行为。生产启用前先迁移开发库、验证客户端协议并备份数据库；本轮没有在当前阿里云 GameServer 上执行迁移、重启或部署，也没有进行生产压测。

## 验证入口

本地独立 MySQL 夹具与完整测试命令沿用 README：

```bash
bash scripts/prepare-tcp-test-db.sh
dotnet restore TcpGameServer.sln --locked-mode --disable-parallel -m:1 -nr:false
dotnet build TcpGameServer.sln -c Release --no-restore -m:1 --disable-build-servers -tl:off
dotnet test TcpGameServer.sln -c Release --no-build --no-restore -m:1 --disable-build-servers -tl:off
```

新增测试覆盖真实 SQLite 的明文／AES TCP 登录与业务、数据库云存档、旧写进度入口隔离、重连／重启、三波胜负、奖励一次、版本争用和回执内容冲突；存储层另在 MySQL 上验证实际 CAS／回执事务。单元测试使用可推进时钟，避免依赖长时间等待来判断波次是否到期。

本轮实际验证：.NET SDK 10.0.403，锁文件还原成功；Release 构建 0 警告、0 错误；59 项单元测试及 84 项集成测试通过，共 143 项、0 跳过。可信管理 CLI 在独立 SQLite 库上的账号预置／令牌签发冒烟通过；客户端导出实际生成了 Game.cs 与 TowerDefense.cs。以上没有使用生产数据库，也没有进行游戏容量压测。
