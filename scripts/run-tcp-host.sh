#!/usr/bin/env bash
set -euo pipefail
repo_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
server_dll="$repo_dir/src/GameServer.Host/bin/Release/net10.0/GameServer.Host.dll"
if [[ ! -f "$server_dll" ]]; then
  printf '%s\n' 'Build TcpGameServer.sln -c Release first.' >&2
  exit 1
fi
if [[ -n ${DOTNET_ROOT:-} && -x "$DOTNET_ROOT/dotnet" ]]; then
  dotnet_binary="$DOTNET_ROOT/dotnet"
else
  dotnet_binary=$(command -v dotnet || true)
fi
if [[ -z "$dotnet_binary" ]]; then
  printf '%s\n' 'Set DOTNET_ROOT or add .NET 10 to PATH.' >&2
  exit 1
fi
cd -- "$(dirname -- "$server_dll")"
exec "$dotnet_binary" "$server_dll" "$@"
