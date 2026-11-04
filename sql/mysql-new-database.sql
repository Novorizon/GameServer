-- For a NEW development database. Do not use this as a migration of legacy account data.
CREATE TABLE IF NOT EXISTS account (
  id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
  device_id VARCHAR(255) NOT NULL UNIQUE
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS tcp_player_progress (
  player_id BIGINT NOT NULL PRIMARY KEY,
  progress INT NOT NULL DEFAULT 0,
  version BIGINT NOT NULL DEFAULT 1
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS tcp_facebook_binding (
  player_id BIGINT NOT NULL PRIMARY KEY,
  user_id VARCHAR(255) NOT NULL UNIQUE
) ENGINE=InnoDB;
