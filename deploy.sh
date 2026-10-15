#!/usr/bin/env bash
# Build and drop the plugin into Dalamud's devPlugins folder.
# In game: /xlplugins -> Dev Tools -> reload, no restart needed.
set -e
cd "$(dirname "$0")"
dotnet build AzeronHud/AzeronHud.csproj -c Release "$@" | grep -E "error|warning CS|Build succeeded" || true
DEST="$HOME/.xlcore/devPlugins/AzeronHud"
mkdir -p "$DEST"
cp AzeronHud/bin/Release/AzeronHud.{dll,json,pdb} "$DEST/"
echo "  -> $DEST"

# Dalamud stops logging at 100 MiB and never tells you. Rotate before that
# rather than deleting mid-debug, which costs the very diagnostics you need.
LOG="$HOME/.xlcore/logs/dalamud.log"
if [ -f "$LOG" ]; then
  SZ=$(stat -c%s "$LOG")
  if [ "$SZ" -gt 83886080 ]; then      # 80 MiB
    mv "$LOG" "$LOG.$(date +%H%M%S).bak"
    echo "  rotated dalamud.log at $((SZ/1048576)) MiB (restart the game to resume logging)"
  fi
fi
