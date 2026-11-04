# GameServer2026 评审

审查对象：[Novorizon/GameServer2026](https://github.com/Novorizon/GameServer2026)，提交 `7c6a473900488411a2c2e9f3e259f31a96c1363d`。本地只读参考副本为 `/workspace/references/GameServer2026`，未改动其源码或推送内容。

结论：适合作为 HTTP/SignalR 游戏后台样例，业务分层和接口拆分有参考价值；目前不能当作经过生产验证的纯 TCP 框架，也不能据此判断 8000 连接的内存。当前独立 TCP 工程不引入该仓库的传输层。

## 实际传输与优点

`Server/Program.cs:11` 使用 ASP.NET Core `WebApplication`，注册 HTTP API，映射 `/sr/game` SignalR Hub，配置 HTTP 7798 和 HTTPS 7797。SignalR 使用 MessagePack；默认可协商多种传输，未在此处强制仅用 WebSocket。HTTPS 实际启动仍取决于证书配置。

可以参考的部分包括：Protocol/Server 分离、HTTP Handler/Service 与数据库代码拆分、DI 组装、异步参数化 MySQL 查询、`INotify` 推送抽象，以及 MessagePack 的 `UntrustedData` 配置。业务原型简洁，但尚缺完整运行说明、数据库迁移和自动化测试证据。

## 需要先修复的问题

### P1：旧连接清理可能删除新连接

[SingleLoginRegistry.cs:107](https://github.com/Novorizon/GameServer2026/blob/7c6a473900488411a2c2e9f3e259f31a96c1363d/Server/WebSocket/Net/SingleLoginRegistry.cs#L107) 的 `RemoveIfMatch` 先读取并比较连接号，之后按 playerId 无条件 `TryRemove`。

具体交错：旧连接 A 的断线处理读到 A → 新连接 B 写入同一个 playerId → A 执行 TryRemove 删除 B。此后 B 会被 `EnsureCurrentConnection` 当作无效连接。`ConcurrentDictionary` 只能保证单个操作安全，不保证这几步整体原子性。

应改成对 `(playerId, connectionId)` 的原子条件删除。`TrySetCurrent` 的读写也需要 CAS 重试或按玩家串行：两个新连接同时读取旧 A 时，后写入的连接可能无法正确定位和关闭被其替换的新连接。

### P1：公会加入与发言缺少成员权限验证

[GameHub.cs:72](https://github.com/Novorizon/GameServer2026/blob/7c6a473900488411a2c2e9f3e259f31a96c1363d/Server/WebSocket/Net/GameHub.cs#L72) 接受客户端传入任意 guildId 后直接加入组；`SendGuildChat` 同样直接向传入的公会广播。`EnsureCurrentConnection` 验证当前连接身份，未验证该玩家属于这个公会。

认证用户可尝试订阅其他公会消息或向其发送消息。应由服务端成员关系决定可加入/发送的组，并增加业务文本长度、频率与扇出额度。框架的消息大小限制不能替代成员权限。

### P1：设备号登录仅适用于游客原型

[Http AuthService.cs:34](https://github.com/Novorizon/GameServer2026/blob/7c6a473900488411a2c2e9f3e259f31a96c1363d/Server/Http/Services/AuthService.cs#L34) 仅要求 DeviceId 非空，随后按该字符串查找账号并签发令牌。DeviceId 是可复制的标识，不是账号所有权证明。商业游戏需平台凭证、已有会话证明或相应账号验证；通信加密不会解决此问题。

首次注册同时采用“查询不存在，再插入”。同设备并发注册需要唯一约束与冲突后重读；仓库没有数据库 DDL，无法确认线上约束情况。

### P2：挤下线只通知客户端，没有服务端强制释放

[GameHub.cs:43](https://github.com/Novorizon/GameServer2026/blob/7c6a473900488411a2c2e9f3e259f31a96c1363d/Server/WebSocket/Net/GameHub.cs#L43) 和 `KickService` 只发送 `ForceLogout`。旧连接再次调用业务方法才被 Abort；若其忽略通知、继续维持底层连接而不调用业务，会继续占用连接资源。它还可能留在 SignalR 组或 `Clients.User(playerId)` 推送目标中。

应使用由服务端拥有的连接终止机制清理被替换连接，并将组成员、推送权限、会话回收绑定到同一个生命周期。客户端主动断开可以作为配合，不能作为唯一回收路径。这一点与当前重连内存调查直接相关。

### P2：令牌字典随累计登录玩家数增长

[AuthSessionStore.cs:18](https://github.com/Novorizon/GameServer2026/blob/7c6a473900488411a2c2e9f3e259f31a96c1363d/Server/WebSocket/Auth/AuthSessionStore.cs#L18) 每个玩家保留一个 token，没有过期清理或容量限制；提供的 `Remove` 在本仓库调用链中没有使用。即使令牌过期后验证被拒绝，字典仍保留对应条目。应按过期时间清理，并用条件删除防止误删更新后的令牌；不是每次重连一定新增一条，而是不同玩家累计登录会增长。

### P2：HTTP 请求体完整暂存后再次复制

[RequestBodyReader.cs:15](https://github.com/Novorizon/GameServer2026/blob/7c6a473900488411a2c2e9f3e259f31a96c1363d/Server/Http/HttpApi/Endpoints/RequestBodyReader.cs#L15) 把请求体复制到 MemoryStream，再 ToArray 生成第二份完整数组。应用未给登录等小请求设置较小的专用上限。Kestrel 有框架默认请求体上限，因此这里不能称为完全无限；但并发大请求仍会造成明显瞬时分配。应设置路由级长度/并发限制，并减少整包复制。

## 构建与依赖结果

使用已安装 .NET 8 运行时及 SDK 构建成功：**0 错误、40 警告**。没有运行仓库级性能测试，也没有启动其数据库业务环境，因此不能据此声称功能或容量已验证。原始证据见 [restore.log](evidence/restore.log) 和 [build.log](evidence/build.log)。

- 项目声明的 `MessagePack 2.6.101` 未找到，NuGet 自动解析到 `3.0.3`，出现 NU1603；这使实际依赖版本偏离声明。
- 解析到的 MessagePack、SignalR MessagePack 8.0.0 和 MySqlConnector 2.5.0 被当前 NuGet 审计报告已知中/高风险问题，详情及公告链接在日志中。应明确选取兼容且已修复版本，并锁定依赖；仅升级目标框架不会自动修复这些包。
- `Protocol.csproj` 引用了 MySqlConnector，使客户端协议层携带数据库依赖，应移至服务端基础设施层。
- 令牌辅助代码仍兼容旧式未签名 token；当前 TokenAuthHandler 还有 AuthSessionStore 的精确令牌匹配，因此本次不把它报告为可直接绕过认证。建议删除未使用的兼容路径，减少未来误用。

建议借鉴 `INotify` 和业务接口组织方式，保留当前独立 TCP 的连接准入、超时、有限缓冲池、有限请求队列及可重现负载测试。不要为了借用样例业务再把 HTTP/SignalR 传输层混回 TCP 工程。
