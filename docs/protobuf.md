# Protobuf 配置与生成

当前新版 TCP 服务器已经配置并安装 Protobuf，通过 NuGet 管理，不需要复制旧仓库的 DLL：

| 组件 | 固定版本 / 位置 | 作用 |
| --- | --- | --- |
| Google.Protobuf | 3.36.2 | 消息序列化、解析运行库 |
| Grpc.Tools | 2.85.0，PrivateAssets=all | 构建期 protoc 编译器，不随服务器发布 |
| 协议定义 | `protos/game.proto` | 客户端与服务器共享的消息契约 |
| 构建配置 | `src/GameServer.Protocol/GameServer.Protocol.csproj` | `<Protobuf ... GrpcServices="None" />` |

版本统一在 `src/Directory.Packages.props` 管理，依赖锁在各项目 `packages.lock.json`。本环境 Linux x64 编译器已实际运行，输出 `libprotoc 35.1`；该版本号与 NuGet 包版本的编号方式不同。

```bash
dotnet build src/GameServer.Protocol -c Release -m:1 --disable-build-servers -tl:off -p:RestoreLockedMode=true
```

构建会自动把 `.proto` 生成到 `src/GameServer.Protocol/obj/Release/net10.0/Game.cs`，勿手改 `obj` 文件。新增消息修改 `.proto`，并注册消息 ID、解析器与 Handler；Protobuf 负责消息内容，TCP 的包头、拆包、连接与 AES-GCM 继续由现有网络层负责。使用 Protobuf 不等于使用 gRPC、HTTP 或 TLS。

供 C# / Unity 客户端单独获取消息代码：

```bash
# 若 SDK 安装到自定义目录，请改为实际安装路径。
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
python3 scripts/generate-protobuf.py --output /absolute/path/client/Generated
```

脚本先构建已锁定的工具链，从 NuGet 还原目录定位 `protoc`，输出 `Game.cs`；不同内容覆盖须加 `--force`，且目标必须是 Protobuf 生成文件。客户端添加与本工程兼容的 `Google.Protobuf` 运行库，不需安装 Grpc.Tools 运行时。导出脚本已在当前 Linux x64 环境验证；其他平台需有对应 .NET/Python 及包内 protoc 的运行条件。

Unity 项目应按其运行时支持安装兼容版本，避免同时放多份不同版本的 Google.Protobuf.dll。若客户端版本较旧，需单独验证运行时兼容性；旧仓库的 `C2S.cs/S2C.cs` 是另一份协议，不能直接替换当前 `game.proto` 的产物。

兼容演进：保留已有字段编号和消息 ID，删除字段后使用 `reserved`，新增字段使用新编号；不要只更改一端的定义。密钥配置仍按 [encryption.md](encryption.md) 设置。
