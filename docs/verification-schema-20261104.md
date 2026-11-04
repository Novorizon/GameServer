# Protobuf、仓库评审与 schema 代码生成验证

日期：2026-11-04。工作目录：`/workspace/TcpGameServer`。

- 锁定模式还原：11 个项目通过。
- Release 全解决方案构建：0 警告、0 错误，包含生成器与两种数据库的生成示例。
- 完整自动测试：116 通过，0 失败，0 跳过；其中单元测试 48、集成测试 68。
- 新增生成器测试 12 项：真实 MySQL/SQLite 元数据、生成代码的独立编译及 CRUD、NULL/BLOB/布尔、计算列、属性避让、行尾兼容、覆盖保护、特殊主键、非法结构及命名冲突。
- CLI 另行验证 6 项：dry-run 不创建输出、不修改 SQLite 文件；实际生成不改变 SQLite 字节；结构变更需 force、手写代码保留；缺失文件不创建；连接解析失败不输出连接字符串；复合主键在写文件前拒绝。
- Protobuf：已安装的 NuGet 工具链实际输出 `libprotoc 35.1`，导出 `Game.cs` 与服务器构建产物逐字节相同。导出文件在独立、仅引用 Google.Protobuf 3.36.2 的客户端项目中编译通过，0 警告、0 错误。
- 上游仓库检查：`Novorizon/GameServer`，commit `aa0cf6bd036fbdad700dae35a65a6a81a5668ec1`；静态源码检查，引用目录无修改。评审见 [reviews/GameServer.md](reviews/GameServer.md)。
- 对修改前源码包逐文件 SHA256 比较：`src/`、`protos/`、`sql/` 无变化。本轮没有新增网络监听，也没有执行新的大连接数性能对比。

修改前备份：`/workspace/experiments/tcp-schema-generation-20261104-153707/before.tar.gz`。验证日志与 TRX 保存在该目录的 `verification/`，CLI 复现脚本也一并保留。旧数据库版源码包继续保留，新代码生成版打包为 `/workspace/TcpGameServer-generated-source.tar.gz`。

使用说明：[Protobuf](protobuf.md)、[表仓储生成](schema-generation.md)。生成器为开发期脚手架，不自动实现数据库迁移、复合主键仓储、并发业务规则或权限校验。
