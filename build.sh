#!/bin/bash
set -e

# Install .NET 10 SDK
curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 10.0
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1

# Publish the app
dotnet publish InventoryPlus.csproj -c Release -o publish

# Create non-fingerprinted blazor.webassembly.js for static hosting
find publish/wwwroot/_framework -maxdepth 1 \
  -name 'blazor.webassembly.*.js' \
  ! -name '*.gz' ! -name '*.br' \
  -exec cp {} publish/wwwroot/_framework/blazor.webassembly.js \;

echo "Build complete. Output in publish/wwwroot"
