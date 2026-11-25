#!/bin/bash
# Server-API guard (copied from session 5f8816c6). Prints any Fusion.*/SomniumSpace.* member the server-like build uses
# that no world which passed the Somnium server compile has used. Anything printed must be removed or proven.
D="D:/Dreamport/Metroid Prime/METROIDPRIME/DevTools/fusionapi"
U="C:/Program Files/Unity/Hub/Editor/6000.3.5f2/Editor/Data"
DLL="${1:-$D/../build/server_like.dll}"
"$U/NetCoreRuntime/dotnet.exe" "$D/RefDump.dll" "$DLL" | tr -d '\r' | LC_ALL=C sort -u > "$D/current.txt" || exit 2
tr -d '\r' < "$D/known_good.txt" | LC_ALL=C sort -u > "$D/known_good.sorted"
NEW=$(LC_ALL=C comm -13 "$D/known_good.sorted" "$D/current.txt")
if [ -n "$NEW" ]; then echo "UNPROVEN server API (not used by any world that passed the server compile):"; echo "$NEW"; exit 1; fi
echo "Server API check: all $(wc -l < "$D/current.txt") Fusion/Somnium members are proven."
