#!/bin/bash
set -e

HOST="ubuntu@129.159.13.201"
KEY="$HOME/.ssh/oracle_vm"
REMOTE_DIR="/opt/vkinegrab"
PUBLISH_DIR="$(dirname "$0")/publish/linux-x64"

echo "→ Building..."
dotnet publish "$(dirname "$0")/vkinegrab.csproj" -c Release -r linux-x64 --self-contained -o "$PUBLISH_DIR" --nologo 2>&1 | grep -E "error|warning|vkinegrab ->"

echo "→ Deploying to $HOST..."
rsync -az --delete -e "ssh -i $KEY" "$PUBLISH_DIR/" "$HOST:$REMOTE_DIR/"

echo "→ Setting permissions..."
ssh -i "$KEY" "$HOST" "chmod +x $REMOTE_DIR/vkinegrab"

echo "✓ Done."
