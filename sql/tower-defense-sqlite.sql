-- Optional additive migration. Apply before enabling TowerDefense:Enabled.
-- Player state and command receipt are always committed in one transaction.
CREATE TABLE IF NOT EXISTS tcp_td_player (
    player_id INTEGER NOT NULL PRIMARY KEY,
    version INTEGER NOT NULL CHECK (version > 0),
    state_json TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS tcp_td_receipt (
    player_id INTEGER NOT NULL,
    operation_id TEXT COLLATE BINARY NOT NULL,
    fingerprint TEXT COLLATE BINARY NOT NULL,
    response BLOB NOT NULL,
    created_at_unix_ms INTEGER NOT NULL,
    PRIMARY KEY (player_id, operation_id)
);
