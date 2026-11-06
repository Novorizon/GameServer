-- Apply explicitly before setting Storage:Provider=Database. Existing files are not imported.
CREATE TABLE IF NOT EXISTS tcp_player_save (
  player_id BIGINT NOT NULL PRIMARY KEY,
  data MEDIUMBLOB NOT NULL,
  version BIGINT NOT NULL
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS tcp_player_save_receipt (
  player_id BIGINT NOT NULL,
  operation_id VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  request_hash CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  committed_version BIGINT NOT NULL,
  PRIMARY KEY(player_id, operation_id)
) ENGINE=InnoDB;
