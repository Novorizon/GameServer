#!/usr/bin/env bash
set -euo pipefail
repo_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
container=${TCP_TEST_DB_CONTAINER:-tcp-game-test-db}
dkr() { env -u DOCKER_HOST -u DOCKER_CONTEXT -u DOCKER_TLS -u DOCKER_TLS_VERIFY -u DOCKER_CERT_PATH docker --host=unix:///var/run/docker.sock "$@"; }
dkr info --format '{{.ServerVersion}}' >/dev/null
if ! dkr container inspect "$container" >/dev/null 2>&1; then
  dkr run -d --name "$container" --label codex.setup=tcp-game \
    -p 127.0.0.1:13307:3306 -e MYSQL_ALLOW_EMPTY_PASSWORD=yes \
    mysql@sha256:a81d043930e51745522bd5c430961cf8286f0a23f0a9a2a6e0108f7ea71b0902 >/dev/null
else
  dkr start "$container" >/dev/null
fi
for attempt in $(seq 1 60); do
  if dkr exec "$container" mysql -uroot -Nse 'SELECT 1' >/dev/null 2>&1; then break; fi
  if [[ "$attempt" == 60 ]]; then printf '%s\n' 'MySQL readiness failed.' >&2; exit 1; fi
  sleep 1
done
{
  cat <<'SQL'
CREATE DATABASE IF NOT EXISTS gameserver_tcp_test;
CREATE USER IF NOT EXISTS 'gameserver_tcp_test'@'%' IDENTIFIED BY '';
GRANT ALL ON gameserver_tcp_test.* TO 'gameserver_tcp_test'@'%';
USE gameserver_tcp_test;
CREATE TABLE IF NOT EXISTS account(id INT NOT NULL PRIMARY KEY,device_id VARCHAR(255) NOT NULL UNIQUE);
SQL
  cat "$repo_dir/sql/tcp-schema.sql"
  python3 - <<'PY'
for start in range(1, 8201, 200):
    values=','.join("(%d,'tcp-load-%d')"%(i,i) for i in range(start,min(start+200,8201)))
    print('INSERT IGNORE INTO account(id,device_id) VALUES'+values+';')
PY
} | dkr exec -i "$container" mysql -uroot
printf '%s\n' 'Independent TCP test database ready at 127.0.0.1:13307 (local test only).'
