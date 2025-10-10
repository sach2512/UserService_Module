#!/usr/bin/env bash
set -e
echo "🚀 Starting .NET 8 build for UserService.API..."

# Install .NET 8 SDK
curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 8.0
export PATH=$PATH:$HOME/.dotnet

# Show .NET info
dotnet --info

# Restore dependencies (NOTE: no src path)
dotnet restore UserService.sln

# Publish API
dotnet publish UserService.API -c Release -r linux-x64 --self-contained true -p:PublishTrimmed=false -o out

echo "✅ Build and publish completed successfully."
