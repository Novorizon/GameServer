-- Optional additive migration. Apply before enabling TowerDefense:Enabled.
CREATE TABLE IF NOT EXISTS tcp_td_player (
    player_id BIGINT NOT NULL PRIMARY KEY,
    version BIGINT NOT NULL,
    state_json MEDIUMTEXT NOT NULL,
    CHECK (version > 0)
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS tcp_td_receipt (
    player_id BIGINT NOT NULL,
    operation_id VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    fingerprint VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    response MEDIUMBLOB NOT NULL,
    created_at_unix_ms BIGINT NOT NULL,
    PRIMARY KEY (player_id, operation_id)
) ENGINE=InnoDB;
