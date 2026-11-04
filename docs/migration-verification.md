# 新版 TCP 仓库迁移验证

验证日期：2026-11-04（北京时间）。目标仓库：`Novorizon/GameServer`，旧主线：`aa0cf6bd036fbdad700dae35a65a6a81a5668ec1`。

## 新工作目录验证

在 `/workspace/GameServer` 独立检出目标仓库，并从已验证的 TCP 新版源码包迁入源码后执行：

```bash
DOTNET_ROOT=/workspace/.dotnet bash scripts/setup-tcp.sh
bash scripts/prepare-tcp-test-db.sh
DOTNET_ROOT=/workspace/.dotnet PATH=/workspace/.dotnet:$PATH \
  dotnet test TcpGameServer.sln -c Release --no-build --no-restore \
  -m:1 --disable-build-servers -tl:off
```

结果：

| 检查 | 结果 |
| --- | --- |
| 锁定依赖还原 | 11 个项目成功 |
| Release 完整构建 | 0 警告、0 错误 |
| 单元测试 | 48 通过，0 失败、0 跳过 |
| 集成测试 | 68 通过，0 失败、0 跳过 |
| MySQL/SQLite | 实际数据库 CRUD、事务、生成代码编译与 CRUD 均通过 |
| Protobuf 导出 | 新路径运行成功，输出 libprotoc 35.1 和客户端 Game.cs |
| 开发工具入口 | schema 生成器 help、基准工具 help 均通过 |
| 脚本语法 | 三个 shell 脚本 bash -n、两个 Python 脚本 py_compile 通过 |
| Git 差异 | diff --cached --check 通过；历史生成图表/补丁按原字节保留 |
| 仓库完整性 | 11 个 csproj、11 个依赖锁文件、原始 .proto 均纳入版本控制 |

## 迁移边界

- 与来源包逐项比较，服务器 C#、协议、SQL、生成器、测试和示例内容保持一致；仅清理 `.editorconfig` 末尾多余空行。
- 改写仓库首页、补充迁移与验证文档；修正 SDK 安装与基准脚本的目录选择；增加文本行尾及本地文件忽略规则。
- 默认配置中的数据库连接、JWT 签名密钥和通信密钥均为空占位；版本控制不包含构建 DLL、obj/bin、测试结果目录、数据库或环境密钥文件。
- 迁移不执行线上数据库变更，不启动线上服务，不发布云环境配置。本次未重新进行 8000 连接压力测试，历史报告继续标明其测试版本和条件。
- 保留 MIT LICENSE、完整 Git 历史，以及 `backup/legacy-gameserver-20261104-183959` 旧版分支；完整本地备份见 [migration.md](migration.md)。

本地验证原始日志和 TRX 留在 `/workspace/experiments/gameserver-replacement-20261104-183959/verification`。这些本地路径只用于追溯，不影响其他机器克隆和运行。
