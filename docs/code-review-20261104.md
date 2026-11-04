# TCP 全部代码复查与数据库层实现

本轮检查当前 Protocol、Networking、Gameplay、Infrastructure、Host 及新增 Data 基础库，覆盖帧长度与缓冲区、AES 计数器与认证、连接生命周期、发送所有权、请求队列、玩家替换、通知/管理、数据库与存档。依据明确的新要求，实现了 MySQL/SQLite 驱动封装和可扩展的表仓储层。

## 已完成

1. **修复一个接收预算边界问题。** 原来扩容同时持有起始数组和最大帧数组，当接收预算恰为一个最大帧时，合法帧会错误触发预算不足。已先在栈上保存 28 字节头、归还旧租借，再获取新数组；没有引入 unsafe 或额外堆数组。新增明文/AES 两个测试先复现失败，修复后通过。
2. **数据库驱动集中到 GameServer.Data。** 封装 MySqlConnector 2.6.2 与 Microsoft.Data.Sqlite 10.0.14，SQLite 原生包锁定 SQLitePCLRaw.bundle_e_sqlite3 3.0.5。Infrastructure 不再直接调用 MySqlConnection/Command；连接释放、参数绑定、结果读取、错误分类与事务统一。
3. **实现通用表层。** 标准 Table/Column/Key/DatabaseGenerated 映射，缓存属性访问器；ITableRepository 提供 Get、Insert、Update、Delete、List、FindBy、Count。三个真实业务表仓储与两个可运行扩展示例已经提供。
4. **跨仓储事务明确绑定同一个 session。** 命令串行且等待有上限，回调结束不能继续使用 session；数据库命令失败会标记事务失败，不能 catch 后误提交部分数据。没有盲目重试写入。
5. **MySQL 和 SQLite 有受控的并发与生命周期。** MySQL 共享限额连接池，SQLite 以有界执行处理同步原生 I/O、支持内存库锚连接和取消中断；dispose 取消正在运行的工作并等待其退出。
6. **修正测试接入时序。** TCP 连接建立不等于服务端已执行 accept；测试 helper 现在等待接入或拒绝计数变化，再检查完整生命周期归零。这不是把一次计数瞬态误报为服务器资源泄漏。

数据库接口、五个参考仓储、初始化命令和新增表流程详见 [开发指南](database.md)。玩家存档当前仍是文件实现；它的 MD5 格式只用于兼容与损坏检查，不是数据库或防作弊签名。

## 验证证据

- [完整构建](benchmarks/data-layer-20261104/build.log)：0 警告、0 错误，包含数据库基础库、服务器、测试和独立示例。
- [104 项测试](benchmarks/data-layer-20261104/tests.log)：48 单元、56 集成，全部通过，无跳过。MySQL/SQLite 同套 CRUD、NULL/映射、参数化、分页、唯一冲突、原子扣减、两表提交/回滚、事务失败保护、取消/释放、根操作/会话队列限制均经过测试。SQLite 另有实际明文和 AES TCP 登录/进度测试。
- [锁定依赖恢复](benchmarks/data-layer-20261104/locked-restore.log) 成功；[NuGet 依赖审计](benchmarks/data-layer-20261104/dependency-audit.json) 未报告已知漏洞或审计失败。这不是对所有潜在漏洞的保证。
- [SQLite 示例](benchmarks/data-layer-20261104/example-sqlite.log)、[MySQL 示例](benchmarks/data-layer-20261104/example-mysql.log) 均验证完整 CRUD、自增 ID、参数查询、原子扣减及两表提交/回滚。[SQLite 初始化](benchmarks/data-layer-20261104/initialize-sqlite.log)、[MySQL 初始化](benchmarks/data-layer-20261104/initialize-mysql.log) 均成功。
- 当前 MySQL＋AES-GCM 版 [8000 连接回归](benchmarks/data-layer-20261104/regression-aes/summary.json) 无失败、无资源预算违规；空闲 RSS 180.1 MiB，持续通信采样峰值 193.6 MiB，1 KiB 回显约 27212 次/秒。服务器/客户端正常退出，日志确认 Connections、Sessions、Players、Unauthenticated、Queued、Running、ReceiveInUse、SendInUse、PendingSend 全部归零。

回归是单次本机稳定性验证，按现有脚本预热 3 秒心跳＋3 秒回显，服务器 4 CPU、客户端 1 CPU；不是重新执行历史三模式固定请求数的对照，也不能视为线上 4 GB 机器容量保证。原始 [配置与 DLL 指纹](benchmarks/data-layer-20261104/regression-aes/configuration.json)、[资源 CSV](benchmarks/data-layer-20261104/regression-aes/resources.csv)、[服务器日志](benchmarks/data-layer-20261104/regression-aes/server.log) 保留。

## 使用边界

- SQLite 的单写者、类型和排序规则与 MySQL 不同；统一 API 不会消除这些数据库语义差异。默认并发与等待有限，不能按 Async 后缀推断 SQLite I/O 不阻塞。
- 泛型 Update 是普通覆盖，不自动防止业务层丢失更新；关键业务使用示例中的条件更新或版本 CAS。数据库约束和索引由显式 SQL 维护。
- 启动检查验证当前业务表列可访问，不是完整 schema 迁移系统；初始化工具不会 ALTER 已有表。旧账号表结构要按实际情况维护。
- 事务回调必须使用 session 创建仓储并 await 操作；默认注入的仓储仍然独立租用连接，不能假定会自动参与外层事务。
- 配置和后端切换不会迁移原数据库或文件存档，也没有新增公网数据库管理接口。

修改前备份位于 `/workspace/experiments/tcp-code-review-20261104-142907/before.tar.gz`，内部保留 `TcpGameServer/` 目录。新的源代码包为 `/workspace/TcpGameServer-database-source.tar.gz`。原 WS 工程未改动；未发布或推送内容。
