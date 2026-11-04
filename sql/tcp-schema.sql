-- Apply explicitly to the selected database. Never automatically alters legacy tables.
CREATE TABLE IF NOT EXISTS tcp_player_progress (
  player_id BIGINT NOT NULL PRIMARY KEY,
  progress INT NOT NULL DEFAULT 0,
  version BIGINT NOT NULL DEFAULT 1
);
CREATE TABLE IF NOT EXISTS tcp_facebook_binding (
  player_id BIGINT NOT NULL PRIMARY KEY,
  user_id VARCHAR(255) NOT NULL UNIQUE
);
