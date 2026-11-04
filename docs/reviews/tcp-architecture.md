# TCP 架构评估与 GameServer2026 借鉴建议（采纳前记录）

以下保留默认 AES 采纳时的评估。此后已实现 Service 命名、模块注册、统一推送、管理踢人和消息发送边界拆分；当前设计与用法见 [模块设计](../modules.md)。下文“尚无/建议”描述的是评估时状态。

评估范围为当前独立 TCP 工程，以及 GameServer2026 提交 `7c6a473900488411a2c2e9f3e259f31a96c1363d`。本轮已将 TCP＋AES-GCM 设为默认，保留明文开关；下文其余扩展项是设计建议，没有在本轮加入对应业务功能。

## 当前结构是否合理

**作为独立 TCP 游戏服务器基础，当前结构合理，适合继续扩展。** 五个工程的边界足够清楚，现阶段不需要再增加一套通用框架或大量空的抽象层。连接压测验证了传输与资源管理基础，但并不代表已经具备房间、战斗、完整运营后台等所有游戏功能。

| 层 | 当前职责 | 判断 |
| --- | --- | --- |
| Protocol | GWS1 帧、Protobuf 消息、可选包加密 | 不依赖数据库或业务，适合与客户端约定协议 |
| Networking | TCP 收发、拆包、缓冲池、连接限流、收发预算、超时 | 资源约束集中，业务 Handler 不处理 socket 或密钥 |
| Gameplay | Handler/Dispatcher、会话和玩家管理、账号/存档/进度/Facebook System | 功能按模块组织，业务通过仓储接口访问持久化 |
| Infrastructure | JWT、MySQL、文件存档、外部 API 适配 | 实现业务接口，便于替换存储和外部服务 |
| Host | 配置、DI、启动关闭、统计 | 启动和配置集中，没有把 WebSocket/HTTP 游戏监听混入 TCP |

单例由 DI 注册和释放，而不是在每个 Manager 内增加全局静态 Instance。当前命名职责基本一致：Manager 管理运行时集合和生命周期；System 处理业务规则；Handler 处理消息入口；Repository / Store 提供持久化接口与实现。保留 `Async` 异步后缀和 `I` 接口前缀即可，不应为了命名统一将所有类都改名为 Manager。

已经具备的关键能力：

- `TcpServer` 控制在线连接数量、接入速率和排空关闭；`GameConnectionHandler` 处理登录期限、空闲超时及会话清理。
- `SessionManager` 按连接管理会话，删除时匹配对象；`PlayerManager` 按玩家绑定当前会话，替换连接时由服务端主动关闭旧连接，避免只依赖客户端退出。
- `PlayerExecutionContext` 对同一玩家业务串行执行，并限制等待数量；进度写入有版本比较，减少旧连接或并发覆盖问题。
- `PacketDispatcher` 集中注册消息、认证要求和异常转换；登录、普通请求的并发与排队分别有上限。
- 收发池与发送积压都有预算，默认零字节读取不为每个空闲连接提前保留接收数组；加密不改变业务模块接口。

相关实现：[模块注册](../../src/GameServer.Gameplay/GameplayRegistration.cs)、[分发](../../src/GameServer.Gameplay/Dispatching/PacketDispatcher.cs)、[玩家生命周期](../../src/GameServer.Gameplay/Players/PlayerManager.cs)、[会话生命周期](../../src/GameServer.Gameplay/Sessions/GameConnectionHandler.cs)。

## 目前值得收紧的边界

1. **业务层仍接触部分网络实现。** `GameSession` 直接租借 `BufferPools` 编码应答，`GameConnectionHandler` 依赖 `TcpServerOptions.MaximumApplicationPacketBytes`。小工程中可以接受，后续宜把“发送业务消息”和“会话允许的响应大小”提炼为窄接口/会话配置，让 System 不需要了解缓冲池和加密标签。这主要改善测试与维护，不要求把 WS 加回工程。
2. **目前以请求应答为主，缺少统一主动推送出口。** 应答复用请求号；主动通知需要单独约定消息 ID、请求号含义、目标会话及慢客户端处理规则。不要让各 System 自行查连接、编码、广播。
3. **运行时管理与业务规则需继续分开。** 当前 PlayerManager 与各 System 已体现这一点。新增房间/公会时，让 Manager 管成员和生命周期，System 管加入条件、权限与玩法规则，Handler 只做协议入口；按实际业务需要增加模块。
4. **日志与管理功能可以逐步补充。** 错误和关键生命周期日志可增加 SessionId、PlayerId、RequestId 关联；指标使用有限的消息类型等维度，避免按每个玩家建立无限指标标签。管理命令与游戏协议的权限、资源预算应分别定义。

这些是后续演进点，不是当前 8000 连接测试失败或必须重写网络层的证据。本轮只修改默认加密与客户端选项，没有开展这些重构。

## GameServer2026 可以借鉴什么

| 参考设计 | 当前 TCP 状态 | 适合的借鉴方式 |
| --- | --- | --- |
| Protocol 与 Server 分离、DI 注册 | 已有，并且进一步拆成五层 | 保持现有结构，无需为此迁回 SignalR |
| `INotify` 统一推送出口 | 尚无独立推送服务 | 优先参考这个接口思想，新增按玩家/业务组推送的服务；底层继续使用当前 TCP、加密和发送预算 |
| AuthModule / AdminModule 入口与 Service 分离 | 账号 Handler/System 已分离；无独立管理模块 | 新增功能延续薄 Handler＋业务 System，管理工具通过独立管理服务调用业务能力 |
| `KickService` 管理入口 | 当前连接替换已有服务端关闭；尚无通用管理入口 | 借鉴管理服务边界，真正踢人要执行会话关闭与资源清理，通知只是配合 |
| 公会组与推送 | 当前未实现公会/房间 | 按需求增加 RoomManager / RoomSystem 或 GuildManager / GuildSystem；成员资格来自服务端业务验证 |
| 异步 MySQL、参数化查询 | 已有 | 保持仓储接口与连接池；数据库依赖留在 Infrastructure |

参考源码：[INotify](https://github.com/Novorizon/GameServer2026/blob/7c6a473900488411a2c2e9f3e259f31a96c1363d/Server/WebSocket/Services/INotify.cs)、[AuthModule](https://github.com/Novorizon/GameServer2026/blob/7c6a473900488411a2c2e9f3e259f31a96c1363d/Server/Http/HttpApi/Modules/AuthModule.cs)、[KickService](https://github.com/Novorizon/GameServer2026/blob/7c6a473900488411a2c2e9f3e259f31a96c1363d/Server/WebSocket/Services/KickService.cs)。

GameServer2026 的 `GuildService` 当前只是公会组名生成器，不能视为完整公会权限系统。它的旧连接清理竞态、只通知客户端下线、无过期清理的令牌字典，以及 Protocol 携带数据库依赖等问题，不应原样带入。详细问题及位置见 [仓库评审](GameServer2026.md)。

若后续需要扩展，建议优先顺序为：统一主动推送 → 管理服务 → 按实际玩法增加房间/公会业务 → 逐步收紧会话对网络实现的依赖。无需为了借鉴模块组织把 HTTP/SignalR 传输层放回当前 TCP 游戏链路。

## 原版服务器的 TLS 状态

“支持 TLS”与“已经开启 TLS”需要区分：

| 对象 | 仓库中能确认的状态 |
| --- | --- |
| 原始 SimpleServer，提交 `0840aeff956317f182161f0bc6ed46e2572cf2ed` | Kestrel 在 1235 端口监听 HTTP/1，未调用 `UseHttps`，应用本身是明文 `ws://` |
| 当前保留的优化版 WS | 具备可选证书配置；当前工作区 appsettings 未配置证书路径，相关环境覆盖也未见启用，默认仍为明文 WS |
| 前一轮 TCP＋TLS 对照组 | 为测试显式开启 TLS 1.3，实际协商套件与证书校验已记录；仅存在于独立测试副本 |
| GameServer2026 | 程序同时配置 HTTP 7798 与 HTTPS 7797；HTTPS 实际能否启动还取决于证书及部署环境 |
| 当前正式独立 TCP | TCP＋AES-GCM 默认开启，可显式关闭包加密；没有 TLS 监听器 |

原始代码证据：[SimpleServer Program.cs](https://github.com/Novorizon/SimpleServer/blob/0840aeff956317f182161f0bc6ed46e2572cf2ed/Server/Program.cs#L58)。代码只能确认应用监听方式，不能据此确定线上 CDN、负载均衡或反向代理是否另行提供了 TLS。

## 本轮验证与恢复

默认配置与缺省选项均改为 AES-GCM；`Mode=None` 明确关闭。示例客户端同样默认 AES-GCM，允许环境变量或命令行选择模式。缺少有效通信密钥时启动失败，不静默降级。

本轮 **68 项测试通过**（41 单元、27 集成），包含真实独立客户端的默认加密登录/心跳、明文开关，以及默认加密缺少密钥的启动拒绝。[测试日志](../benchmarks/aes-default-20261104/tests.log)。本轮没有修改算法或网络收发热路径，没有重跑或改写上一轮三模式性能结果。

修改前源码备份位于 `/workspace/experiments/tcp-aes-adoption-20261104-135708/before.tar.gz`，保留了默认明文时的工程状态。未发布、推送或改动原 WS 工程。
