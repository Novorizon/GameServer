#!/usr/bin/env bash
set -euo pipefail
repo_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
sdk_dir=${TCP_DOTNET_ROOT:-${DOTNET_ROOT:-$HOME/.dotnet}}
mkdir -p "$sdk_dir"
if ! [ -x "$sdk_dir/dotnet" ] || ! "$sdk_dir/dotnet" --list-sdks | rg -q '^10\.0\.403 '; then
  archive=$(mktemp /tmp/gameserver-sdk.XXXXXX.tar.gz)
  trap 'rm -f "$archive"' EXIT
  curl --fail --show-error --location --retry 3 https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.403/dotnet-sdk-10.0.403-linux-x64.tar.gz -o "$archive"
  printf '%s  %s\n' b7b3d8461cf98b07d2552ce9199702fa3ad010ac2671a5f8cd5136f7534e50f6855ba7bbb02b11494563f1d04156276a31cda1103b900e5eecd0d9473dc7b9c5 "$archive" | sha512sum --check --status
  tar -xzf "$archive" -C "$sdk_dir"
fi
export DOTNET_ROOT="$sdk_dir" PATH="$sdk_dir:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
cd "$repo_dir"
dotnet restore TcpGameServer.sln --locked-mode --disable-parallel -m:1 -nr:false
dotnet build TcpGameServer.sln -c Release --no-restore -m:1 --disable-build-servers -tl:off
