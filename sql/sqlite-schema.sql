-- Run explicitly on a development/selected SQLite database. No automatic migration.
PRAGMA foreign_keys=ON;
CREATE TABLE IF NOT EXISTS account (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  device_id TEXT NOT NULL UNIQUE
);
CREATE TABLE IF NOT EXISTS tcp_player_progress (
  player_id INTEGER PRIMARY KEY,
  progress INTEGER NOT NULL DEFAULT 0,
  version INTEGER NOT NULL DEFAULT 1
);
CREATE TABLE IF NOT EXISTS tcp_facebook_binding (
  player_id INTEGER PRIMARY KEY,
  user_id TEXT NOT NULL UNIQUE
);
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
