# Unity TCP 客户端对接

客户端位于 [Novorizon/Framework](https://github.com/Novorizon/Framework) 的 `Framework/Network`。目标为 Unity 2022.3 Windows、Android、iOS，运行时代码 C# 9 / .NET Standard 2.1；Unity 编辑器与真机验证需要在客户端项目中完成。

两端共用 28 字节 GWS1/v2 TCP 包头及 `protos/game.proto`。客户端使用 BouncyCastle 的托管 AES-GCM，服务器保持 .NET AES-GCM；没有为此引入 TLS 或 HTTP 握手。

| 服务器配置 | 客户端 TcpClientOptions |
| --- | --- |
| Networking.Port（默认 1236） | Port |
| 公网域名或 IP | Host，无 URL 前缀 |
| Networking.Encryption.Mode=AesGcm / None | Encryption=AesGcm / None |
| Networking.Encryption.KeyBase64 | KeyBase64，32 字节 Base64 通信密钥 |
| Networking.MaxPacketBytes | MaxPacketBytes，包含包头和 AES tag |

模式和通信密钥必须一致，客户端不做自动降级。JWT 由可信认证服务签发，客户端只提交账号令牌。Authentication.SigningKeyBase64 始终只在可信服务器保存。通信共享密钥不是 JWT 签名密钥，也不具备 TLS 的证书身份验证和前向保密。

客户端发起的消息必须满足 `RequestId > 0`、普通消息号、ResultCode=0。RequestId=0 和 >=2100000000 的消息号保留给服务器通知。此版本在分派入口明确拒绝非法编号并关闭连接；线格式、业务处理器与数据库 schema 均未改变。

登录确认之后才能调用其他业务。请求响应沿用相同 MessageId、RequestId，客户端支持并发和乱序匹配。SaveWrite 成功返回空包体确认，不能把它当作存档内容回显；SaveRead 才返回存档。ProgressWrite 使用 expected_version 做并发控制，客户端遇到冲突应重新读取后决定是否重试。

从 Framework 仓库执行协议同步（生成脚本需要 Linux x64，Unity 使用已生成的源码即可）：

```sh
dotnet restore build/Game.Framework.Network/Game.Framework.Network.csproj --locked-mode
python3 scripts/sync-server-protocol.py --server-root ../GameServer
python3 scripts/sync-server-protocol.py --server-root ../GameServer --check
dotnet test tests/Game.Framework.Network.Tests
```

测试运行真正的 GameServerHost，使用隔离的 SQLite 内存数据库，不访问生产账号或生产数据库。覆盖明文/AES 两模式、认证、存档、并发请求、通知、心跳故障重连、非法请求和断连后资源归还。客户端仅重连及重新登录，不自动重发业务写入；支付、奖励等请求应自行设计幂等键与结果查询。
