#!/usr/bin/env bash
# Dev only: run the host (Steam copy, --s4d-lan host) and the client copy (tools/dev/make_client_instance.ps1) together,
# wait, then print both copies' Straft 4 Dead log lines about networking.
#   bash tools/dev/lan_test.sh <client-instance dir> [seconds]
CLIENT="$1"; SECS="${2:-120}"
GAME="/c/Program Files (x86)/Steam/steamapps/common/STRAFTAT"
taskkill //IM STRAFTAT.exe //F >/dev/null 2>&1; sleep 3
"/c/Program Files (x86)/Steam/steam.exe" -applaunch 2386720 --s4d-lan host >/dev/null 2>&1 &
sleep 35
(cd "$CLIENT" && ./STRAFTAT.exe --s4d-lan client -logFile "$(cygpath -w "$CLIENT")\\Player-client.log" >/dev/null 2>&1 &)
sleep "$SECS"
echo "== host"; grep -E "\[(lan|net|director|specials|revive|health)\]" "$GAME/BepInEx/LogOutput.log" | cut -c1-220 | tail -40
echo "== client"; grep -a -E "\[(lan|net|director|specials|revive|health|voice)\]|Exception" "$CLIENT/Player-client.log" | cut -c1-220 | tail -40
