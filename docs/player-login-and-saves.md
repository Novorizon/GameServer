# Player login and cloud saves

TCP login accepts a short-lived JWT from a trusted authentication service. The server validates its signature, issuer, audience, lifetime, player ID, device claim, and existing account mapping. Clients never receive the signing key and cannot create accounts or mint tokens through the game protocol. A device identifier alone is not an authentication credential. An external account provider can later replace the operator delivery step while preserving this boundary.

For local development or trusted administration, set `GAMESERVER_Database__Provider`, `GAMESERVER_Database__ConnectionString`, and `GAMESERVER_Authentication__SigningKeyBase64` through protected server environment configuration. Use the same signing key, issuer, and audience as the running game server. Apply the selected account schema explicitly first. The Host executable also supports these commands without opening a game listener:

```bash
dotnet src/GameServer.Host/bin/Release/net10.0/GameServer.Host.dll provision-player --player-id 123 --device-id test-device
dotnet src/GameServer.Host/bin/Release/net10.0/GameServer.Host.dll issue-login-token --player-id 123 --device-id test-device --lifetime-seconds 300
```

Provisioning is idempotent for an existing exact ID/device pair and rejects reassignment. Token issuance requires a matching provisioned account and writes only the requested token to stdout; deliver it privately to the client and exclude it from logs. This operator command is a trusted bootstrap path, not a public signup endpoint. Production signup/password or third-party login must first verify the real user's identity in a separate trusted service.

The client sends `LoginRequest { device_id, access_token }`, then uses the authenticated connection. Save messages contain no player ID: the server derives ownership from that connection. `LoginResponse.player_id` identifies the authenticated player. A new connection for the same player replaces the old connection.

`Storage:Provider=File` remains the compatibility default: existing `SaveWriteRequest { data }` and the original MD5 corruption-check file format continue to work. Reads expose version zero and writes return the original empty acknowledgement. Versioned writes are rejected in File mode. These files have no distributed compare-and-swap support.

For durable cloud saves, explicitly apply `sql/player-save-sqlite.sql` or `sql/player-save-mysql.sql`, then set `GAMESERVER_Storage__Provider=Database`. New development schemas also include these tables. Existing file saves are not automatically imported. Database mode requires these fields on each write:

```text
SaveWriteRequest { data, expected_version: 0, operation_id: "unique-client-operation" }
SaveResponse     { version: 1 }
SaveReadRequest  {}
SaveResponse     { data, version: 1 }
```

The initial expected version is zero; subsequent writes use the version from the latest read. Missing saves return `NotFound`. A stale version returns `Conflict`; read the current save before resolving the conflict. Missing version, invalid operation ID, or excessive payload size returns `InvalidRequest`. Operation IDs contain 1–64 ASCII letters, digits, `_` or `-` and must be unique per player's logical write. Retry an uncertain write with the same ID, expected version, and exact bytes. Atomic receipts return its original committed version after retries or restarts; changing content while reusing the ID returns `Conflict`. A replay never rolls newer save data back. Receipts are deliberately retained: do not delete them while clients may retry those operations. Database transactions protect writes across server processes; a process-local player lock is not the durability mechanism.

Save bytes are opaque, non-authoritative client data. They never grant currency, owned cards, run progress, or rewards and never update tower-defense profile/run tables. Authoritative game state uses the server's dedicated business commands and transactions. `Storage:MaxSaveBytes` defaults to 128 KiB, bounded to 1 MiB; the networking frame limit must also accommodate it. Back up both save and receipt tables together.
