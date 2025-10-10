#!/usr/bin/env bash
set -e  # stop on first error

echo "🟢 Updating system packages..."
apt-get update

echo "🟢 Installing dependencies..."
apt-get install -y wget apt-transport-https

echo "🟢 Adding Microsoft package feed..."
wget https://packages.microsoft.com/config/debian/12/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb

echo "🟢 Installing .NET SDK 8.0..."
apt-get update
apt-get install -y dotnet-sdk-8.0

echo "🟢 Restoring NuGet packages..."
dotnet restore

echo "🟢 Publishing project..."
dotnet publish UserService.API/UserService.API.csproj -c Release -o out

echo "✅ Build complete! Ready to run with:"
echo "    dotnet out/UserService.API.dll"
