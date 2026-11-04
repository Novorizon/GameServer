# Novorizon/GameServer 源码评审

检查版本：[aa0cf6bd036fbdad700dae35a65a6a81a5668ec1](https://github.com/Novorizon/GameServer/tree/aa0cf6bd036fbdad700dae35a65a6a81a5668ec1)。源码只读检出到 `/workspace/references/GameServer`，与之前检查的 `GameServer2026` 是不同仓库。

这个仓库包含 C# TCP、Protobuf 生成消息、Mono.Data.Sqlite、PureMVC/Unity 风格模块。没有跟踪 `.csproj/.sln` 或原始 `.proto`，主要是源码和预编译 DLL；本轮做静态代码检查，没有把它当成完整可复现服务器构建或进行性能排名。

## 可以参考的部分

| 实现 | 可参考的设计 | 新版中的对应做法 |
| --- | --- | --- |
| `Scripts/Game/Data/TableData` 与 `TableAccess` 分开 | 数据模型与表访问分层，业务不直接操作驱动 | 保持 Row / Repository / 数据库驱动三层，并增加 schema → Row/Repository 生成工具 |
| `TableProxy` 按类型注册访问器 | 统一注册、按类型访问表 | 保持模块依赖注入，避免依赖全局 Facade 查找 |
| `HandlerProxy.RegisterHandler` | 按消息类型集中注册、分发处理器 | 已有 Handler / Module 注册，继续使用有校验的消息 ID 与解析器映射 |
| `ProtoBuffer/C2S.cs` 与 `S2C.cs` | 客户端、服务器共享消息契约 | 当前从受版本控制的 `.proto` 自动生成，使用 NuGet 锁定运行库和编译器 |
| 配置表字典缓存 | 小型、只读游戏配置一次加载、多处读取 | 如以后增加 NPC/道具配置表，可按模块构建共享只读快照；玩家业务表继续分页/按索引查询 |

这些思路已融入现有架构或本轮工具。没有复制其网络、加密和 SQLiteHelper 实现，也没有引入 PureMVC 全局框架。若以后复制其 MIT 代码，需保留仓库 LICENSE 中版权与许可声明。

## 不宜直接搬入的问题

1. **TCP 半包会丢失。** [MessageProxy.cs:128](https://github.com/Novorizon/GameServer/blob/aa0cf6bd036fbdad700dae35a65a6a81a5668ec1/GameServer/Scripts/Game/Proxy/Network/MessageProxy.cs#L128) 在每次 `ReceiveMessage` 新建局部 MemoryStream；结尾虽然写回剩余字节，但没有保存到连接状态，下次接收不会接着解析。跨 Read 的消息因此可能丢失/错位。消息长度也没有完整的最小值、最大值校验。
2. **分配与复制较多。** 同一收发流程创建多个 MemoryStream、`ToArray`、`ReadBytes`，接收时再分配 `new byte[count]`。[NetProxy.cs:67](https://github.com/Novorizon/GameServer/blob/aa0cf6bd036fbdad700dae35a65a6a81a5668ec1/GameServer/Scripts/Game/Proxy/Network/NetProxy.cs#L67) 连接一接受就分配按 `ReceiveBufferSize` 大小的数组。这不利于本项目关注的大量低活跃连接。
3. **SQLite 拼接 SQL、状态共享。** [SQLiteHelper.cs](https://github.com/Novorizon/GameServer/blob/aa0cf6bd036fbdad700dae35a65a6a81a5668ec1/GameServer/Plugins/Sqlite/SQLiteHelper.cs) 将表、列、值拼进 SQL，容易出现转义、注入及错误条件；`SelectWhere` 循环还一直使用 `values[0]`。共享连接/命令/reader 字段不适合并发请求；打开失败被吞掉，事务与 reader 生命周期也需重做。当前使用参数化、有界操作和显式事务的封装。
4. **协议映射容易越界。** [ProtoUtils.cs:340](https://github.com/Novorizon/GameServer/blob/aa0cf6bd036fbdad700dae35a65a6a81a5668ec1/GameServer/Scripts/Network/ProtoUtils.cs#L340) 用两个平行 List 和 `IndexOf` 做 ID/type 映射；代码里 ID 列表很长，而类型列表只登记两个类型，存在通过 ID 检查后访问越界的路径。应在注册时检查唯一性与完整性，避免维护平行数组。
5. **加密辅助代码不能替换当前 AES-GCM。** [EncryptHelper.cs](https://github.com/Novorizon/GameServer/blob/aa0cf6bd036fbdad700dae35a65a6a81a5668ec1/GameServer/Scripts/Network/EncryptHelper.cs) 部分 CFB 方法把 Key 同时用作 IV，缺少消息认证；PEM 辅助转换也有按 UTF-8 取字节而未做 Base64 解码的问题。这些是静态发现，不能据此宣称该仓库线上启用了其中任意一种算法。
6. **不能把配置表缓存推广为所有玩家数据缓存。** `DefaultTableAccess.Load` 以 `SELECT *` 装入整表字典，适合有规模上限的只读配置；若对全部玩家存档照搬，会增加常驻内存，也需要并发与一致性控制。

建议优先继续完善可复现协议生成、表仓储生成、模块注册和配置数据边界。当前 TCP 收发、资源预算、超时、AES-GCM 与数据库封装保持现有实现；这个仓库没有提供足够依据支持替换它们。
