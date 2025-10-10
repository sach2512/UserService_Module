#!/bin/bash
set -e  # stop on first error

echo "🟢 Starting build for Render..."

# Restore and publish using Render's built-in .NET SDK (no apt-get)
dotnet restore

dotnet publish UserService.API/UserService.API.csproj -c Release -o out

echo "✅ Build complete! Ready to run with:"
echo "    dotnet out/UserService.API.dll"
