-- Apply explicitly before setting Storage:Provider=Database. Existing files are not imported.
CREATE TABLE IF NOT EXISTS tcp_player_save (
  player_id INTEGER PRIMARY KEY,
  data BLOB NOT NULL,
  version INTEGER NOT NULL CHECK(version > 0)
);
CREATE TABLE IF NOT EXISTS tcp_player_save_receipt (
  player_id INTEGER NOT NULL,
  operation_id TEXT NOT NULL COLLATE BINARY,
  request_hash TEXT NOT NULL,
  committed_version INTEGER NOT NULL CHECK(committed_version > 0),
  PRIMARY KEY(player_id, operation_id)
);
