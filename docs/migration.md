# 从旧版 GameServer 迁移至新版 TCP 实现

本次替换以旧主线提交 `aa0cf6bd036fbdad700dae35a65a6a81a5668ec1` 为父提交，使用普通 Git 提交保留全部历史，不重建仓库、不强制推送。旧目录 `GameServer/Scripts` 及 Unity/PureMVC/Mono.Data.Sqlite/Google.Protobuf 预编译插件从当前主线移出；根目录 MIT LICENSE 保留。

新版来自此前独立开发、验证的 TCP 源码，源包 SHA256：`fbc5f374129adb267d774debe954033d3c6feb8ede70c1d1f908e99fdf155430`。迁入包括六个服务器工程、协议、数据库生成器、示例、测试、文档及依赖锁文件；不携带原 WebSocket 工程、构建产物、数据库文件或实际凭据。

## 备份

- 旧版分支：`backup/legacy-gameserver-20261104-183959`，指向迁移前提交。
- Git 历史也保留该旧提交，可随时查看或单独检出。
- 本次工作环境另留完整 Git bundle 与旧源码包：`/workspace/experiments/gameserver-replacement-20261104-183959/legacy-history.bundle`、`legacy-source.tar.gz`。这些路径为本地备份，不是克隆仓库的运行依赖。

取回旧版源码、保持当前目录不变：

```bash
git fetch origin
git worktree add ../GameServer-legacy origin/backup/legacy-gameserver-20261104-183959
```

若需要将主线回退到旧内容，建议通过 `git revert <本次迁移提交>` 生成恢复提交，经过检查后正常推送；不要用强制推送抹掉历史。恢复提交需要单独决定执行，本轮只实施向新版迁移。

## 行为与接入变化

- 新版为独立 .NET 10 TCP 服务器，程序集/命名空间使用 `GameServer.*`，解决方案为 `TcpGameServer.sln`，不依赖 Unity 或 PureMVC。
- 消息改为受版本控制的 `protos/game.proto`，包头、消息 ID 与加密封包均使用新版实现。旧客户端不能只更换服务器地址直接连接，须按 [encryption.md](encryption.md) 调整。
- 默认 AES-GCM 包加密，可配置为 None；没有增加 WebSocket/HTTP/TLS 游戏监听。原有验证数据仍按实验条件分别保留，不重算此前内存优化比例。
- 新版包含 JWT/账号验证，需可信认证服务签发令牌。默认配置不含实际数据库连接字符串或密钥，部署者通过环境变量注入。
- 数据库采用 MySqlConnector 和 Microsoft.Data.Sqlite 的统一封装。表仓储和生成工具说明见 [database.md](database.md)、[schema-generation.md](schema-generation.md)。数据库迁移不随源码替换自动执行，不连接或覆盖线上数据库。
- 本轮替换不启动或部署线上游戏服务，也不发布云环境配置。

迁移时将首页改为仓库使用说明，并移除构建/基准脚本对原 `/workspace` SDK 安装目录的依赖。服务器运行源代码、协议定义及 SQL 保持迁移前 TCP 新版内容。

## 验证

在新的仓库路径中执行锁定依赖还原、完整 Release 构建、116 项单元/集成测试；集成测试使用本机独立 MySQL 夹具和 SQLite。额外检查脚本语法、Protobuf 客户端导出、生成器入口以及 Git 差异。详细迁移验证结果由同目录 `migration-verification.md` 记录。
