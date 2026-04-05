#!/bin/bash
set -e

# Install .NET 10 SDK
curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 10.0
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1

# Replace Supabase config from environment variables (set in Cloudflare Pages settings)
if [ -n "$SUPABASE_URL" ] && [ -n "$SUPABASE_KEY" ]; then
  sed -i "s|__SUPABASE_URL__|$SUPABASE_URL|g" wwwroot/appsettings.json
  sed -i "s|__SUPABASE_KEY__|$SUPABASE_KEY|g" wwwroot/appsettings.json
  echo "Supabase config injected from environment variables"
else
  echo "ERROR: SUPABASE_URL or SUPABASE_KEY not set in environment"
  exit 1
fi

# Publish the app
dotnet publish InventoryPlus.csproj -c Release -o publish

# Create non-fingerprinted blazor.webassembly.js for static hosting
find publish/wwwroot/_framework -maxdepth 1 \
  -name 'blazor.webassembly.*.js' \
  ! -name '*.gz' ! -name '*.br' \
  -exec cp {} publish/wwwroot/_framework/blazor.webassembly.js \;

echo "Build complete. Output in publish/wwwroot"
