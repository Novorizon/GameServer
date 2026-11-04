# GameServer

基于 C# / .NET 10 的独立 TCP 游戏服务器。当前主线采用新版模块化实现，提供可配置的 AES-GCM 包加密、Protobuf 消息、MySQL/SQLite 数据访问和表仓储生成工具，可作为后续游戏服务开发基础。

旧版 `GameServer/Scripts`、Unity/PureMVC 插件及随仓库提交的 DLL 已从主线移出；历史提交和 `backup/legacy-gameserver-20261104-183959` 分支保留旧版完整代码。迁移与恢复说明见 [migration.md](docs/migration.md)。

## 当前功能

- 原生 TCP；`Networking.Encryption.Mode=AesGcm` 开启包加密，`None` 关闭，默认开启。模式修改后重启，客户端必须一致。
- Protobuf 消息构建时自动生成；无需手动放入 Google.Protobuf.dll。
- 接收与发送预算、有上限的缓冲池、连接限制和超时管理；低活跃连接使用零字节读取，收到数据后租借接收缓冲区。
- 按模块注册 Handler、业务 Service、会话/玩家 Manager，提供单发、有界广播和服务端踢人接口。
- MySQL、SQLite 共用参数化 SQL、事务和通用 CRUD；账号、进度、绑定仓储及背包/玩家设置示例齐全。
- 从现有 MySQL/SQLite 数据表生成 Row、Repository 和保留手写代码的 partial 扩展。

游戏监听没有 HTTP 升级、WebSocket 或 TLS 握手；业务仍验证 JWT 与账号。正式账号令牌需由可信认证服务签发，本工程没有 HTTP 登录服务。预共享通信密钥与 JWT 签名密钥分开配置。

## 工程结构

| 目录 | 职责 |
| --- | --- |
| `src/GameServer.Protocol` | 包头、Protobuf 消息、AES-GCM |
| `src/GameServer.Networking` | TCP 收发、拆包、连接生命周期、资源预算 |
| `src/GameServer.Gameplay` | 业务模块、Handler、Service、玩家与会话 Manager |
| `src/GameServer.Data` | MySqlConnector / Microsoft.Data.Sqlite 封装、事务、通用表仓储 |
| `src/GameServer.Infrastructure` | 具体表仓储、JWT、文件存档、外部服务适配 |
| `src/GameServer.Host` | 配置、依赖注入、启动停止、资源统计 |
| `protos` | 客户端与服务器共享的 `.proto` |
| `tools/GameServer.SchemaGenerator` | 从现有数据表生成封装层的开发工具 |
| `examples/GameServer.DatabaseExample` | 两种数据库的 CRUD、事务及生成代码示例 |
| `tests` | 单元、集成、负载测试与 .NET TCP 客户端示例 |

入口仍为 `TcpGameServer.sln`，项目命名空间统一为 `GameServer.*`。SDK 固定在 `global.json`，依赖版本与锁文件随源码提交。

## 构建与测试

安装 .NET SDK **10.0.403** 后，在仓库根目录执行：

```bash
dotnet restore TcpGameServer.sln --locked-mode --disable-parallel -m:1 -nr:false
dotnet build TcpGameServer.sln -c Release --no-restore -m:1 --disable-build-servers -tl:off

# 完整集成测试需要本机 Docker、Python 3；测试库仅绑定 127.0.0.1:13307。
bash scripts/prepare-tcp-test-db.sh
dotnet test TcpGameServer.sln -c Release --no-build --no-restore -m:1 --disable-build-servers -tl:off
```

Linux x64 也可运行 `bash scripts/setup-tcp.sh` 下载、校验并安装所需 SDK，再构建工程。脚本优先使用 `TCP_DOTNET_ROOT`、`DOTNET_ROOT` 指定的目录，否则安装到 `$HOME/.dotnet`；运行时将该目录加入 PATH。该安装脚本需要 `curl`、`tar`、`rg`，不适用于其他系统/CPU 架构。

完整测试使用独立 `tcp-game-test-db` 容器和 8200 个测试账号。它是仅供本机测试的无密码夹具，不是正式数据库配置。只运行单元测试可执行 `dotnet test tests/GameServer.UnitTests -c Release --no-build --no-restore`。

## 启动

完成构建和本机测试库准备后，注入配置：

```bash
export GAMESERVER_Database__ConnectionString='Server=127.0.0.1;Port=13307;Database=gameserver_tcp_test;User ID=gameserver_tcp_test;Password=;SslMode=None'
export GAMESERVER_Authentication__SigningKeyBase64='<可信服务端的 JWT 签名密钥，至少 32 个随机字节的 Base64>'
export GAMESERVER_Networking__Encryption__Mode=AesGcm
export GAMESERVER_Networking__Encryption__KeyBase64='<与客户端共享的通信密钥，恰好 32 个随机字节的 Base64>'
bash scripts/run-tcp-host.sh
```

不要提交实际密钥。默认监听 `127.0.0.1:1236`；需要其他地址时设置 `GAMESERVER_Networking__ListenAddress`。加密开启而通信密钥缺失或无效时会拒绝启动。关闭包加密可设置 `GAMESERVER_Networking__Encryption__Mode=None`。

正式使用时换成自己的数据库连接配置；建表方式、SQLite 配置及跨表事务见 [数据库指南](docs/database.md)。默认文件存档位置为程序目录下 `data/player-saves`，可通过 `GAMESERVER_Storage__RootPath` 指定持久目录。通信密钥可被客户端逆向提取，当前方案不提供证书身份认证或前向保密。

## 协议及开发工具

**新版与旧版客户端协议不兼容。** 客户端需使用 `protos/game.proto`、新版包头及匹配的加密模式，接入说明见 [客户端协议](docs/encryption.md)。`tests/GameServer.LoadTests/ClientPacketTransport.cs` 可参考；JWT 签名密钥只留在可信服务端。

Protobuf 已通过 NuGet 配置；修改 `.proto` 后正常构建即可重新生成服务器代码。导出 C# 客户端消息：

```bash
python3 scripts/generate-protobuf.py --output /absolute/path/client/Generated
```

已有表可自动生成模型与仓储；生成前可使用 `--dry-run`，更新生成文件使用 `--force`，手写扩展始终保留。完整命令及支持范围见 [代码生成指南](docs/schema-generation.md)。

## 验证与文档

本次迁移版本通过 **116 项测试**，完整 Release 构建零警告、零错误。生成器测试实际编译输出代码，并在 MySQL、SQLite 上执行 CRUD。

此前数据库版还完成过一次 8000 个 AES-GCM 连接回归：空闲 RSS 180.1 MiB，持续通信采样峰值 193.6 MiB。本次仓库迁移未重新进行大连接数压测；历史实验结果不代表完整正式游戏业务的容量。复现工具为 `scripts/run-benchmark.py`，需要 Linux 和至少 5 个可用逻辑 CPU。

- [模块、推送与管理服务](docs/modules.md)
- [数据库开发与表模板](docs/database.md)
- [从数据表生成代码](docs/schema-generation.md)
- [Protobuf 配置与导出](docs/protobuf.md)
- [网络架构与资源上限](docs/architecture.md)
- [包加密及客户端协议](docs/encryption.md)
- [迁移、备份与恢复](docs/migration.md)
- [迁移前代码生成版验证记录](docs/verification-schema-20261104.md)
- [历史 TCP/AES/TLS 对照实验](docs/tls-comparison.md)

部分历史报告保留原沙盒绝对路径作为实验记录；构建、运行及开发工具不依赖这些路径。原有 MIT 许可证保留在 [LICENSE](LICENSE)。
