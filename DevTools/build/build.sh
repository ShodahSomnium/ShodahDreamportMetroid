#!/bin/bash
# (synced with the real upload log 2026-11-10: the server SKIPS CommunityModules, GLTF, Meshia, PhotonClient, SomniumSpace.Bundles/Common/Helpers/SDK/SDK.Tools/User - runtime_server.head leaves them out)
# Offline compile of the Metroid Prime Dreamport two ways: editor-like and server-like (the Somnium upload server's reference set,
# with the OLDER Fusion the server has - heads copied from the WoW Dreamport's proven build tools), plus the editor tools.
# .cs files are globbed every run. Usage: build.sh [outdir]. Prints error counts + first errors; exit 1 on errors.
B="D:/Dreamport/Metroid Prime/METROIDPRIME/DevTools/build"
SRC="D:/Dreamport/Metroid Prime/METROIDPRIME/Assets/#User/SomniumMetroid"
OUT="${1:-$B/out}"; mkdir -p "$OUT"
U="C:/Program Files/Unity/Hub/Editor/6000.3.5f2/Editor/Data"
RUNTIME_CS=$(find "$SRC" -name "*.cs" -not -path "*/Editor/*" -not -path "*/Tests/*" | sort)
EDITOR_CS=$(find "$SRC" -path "*/Editor/*" -name "*.cs" | sort)
mk() { # $1 head, $2 out dll, $3 file list, $4 extra ref
  cat "$B/$1.head"; echo "-out:\"$OUT/$2\""; [ -n "$4" ] && echo "-r:\"$4\""
  echo "$3" | while IFS= read -r f; do [ -n "$f" ] && echo "\"$f\""; done
}
mk runtime editor_like.dll "$RUNTIME_CS" > "$OUT/runtime.gen.rsp"
mk runtime_server server_like.dll "$RUNTIME_CS" > "$OUT/runtime_server.gen.rsp"
mk editortool editor_tool.dll "$EDITOR_CS" "$OUT/editor_like.dll" > "$OUT/editortool.gen.rsp"
fail=0
for r in runtime runtime_server editortool; do
  if [ "$r" = editortool ] && [ -z "$EDITOR_CS" ]; then echo "editortool: no editor files"; continue; fi
  "$U/NetCoreRuntime/dotnet.exe" "$U/DotNetSdkRoslyn/csc.dll" -noconfig @"$OUT/$r.gen.rsp" > "$OUT/$r.out" 2>&1
  e=$(grep -c 'error CS' "$OUT/$r.out"); w=$(grep -c 'warning CS' "$OUT/$r.out")
  echo "$r: errors $e warnings $w"; [ "$e" != 0 ] && fail=1
  grep 'error CS' "$OUT/$r.out" | sed 's|.*SomniumMetroid/||' | sort -u | head -40
done
# Span types: the WoW Dreamport found the OLD (Fusion 2.0) upload server without them (CS7069). Fusion 2.1's own callback interface
# (INetworkRunnerCallbacks.OnReliableDataReceived) takes ReadOnlySpan<byte>, and the Annihilator world - Fusion 2.1, its own
# SceneNetworking with exactly that callback - uploads and syncs live, so that one use is accepted; any OTHER Span/Memory use fails.
SPANS=$(grep -rn -E 'ReadOnlySpan|Span<|Memory<' "/d/Dreamport/Metroid Prime/METROIDPRIME/Assets/#User/SomniumMetroid" --include=*.cs | grep -v "/Editor/" | grep -v "Net/SceneNetworking.cs:.*OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data)" | grep -v "^\s*//" | grep -v ":\s*///\|://\s" )
if [ -n "$SPANS" ]; then echo "server_span: Span/Memory used outside SceneNetworking's Fusion 2.1 callback:"; echo "$SPANS" | head -5; fail=1; else echo "server_span: only SceneNetworking's Fusion 2.1 callback (ReadOnlySpan<byte>, as the live Annihilator world)"; fi
exit $fail
