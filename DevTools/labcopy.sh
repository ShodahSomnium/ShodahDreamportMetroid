#!/bin/bash
# A private copy of the project for the batch labs while the user's own Unity has the project open (one Unity per project).
# Lives in DevTools/labproject (outside Assets, so the user's editor never imports it). Every run: the code tree (Assets/#User)
# and the scene are mirrored fresh; the rest (SDK, packages, settings) is copied once and then only updated.
# Usage: labcopy.sh  (then lab.sh uses it when the main project is locked)
set -e
SRC="/d/Dreamport/Metroid Prime/METROIDPRIME"
DST="$SRC/DevTools/labproject"
mkdir -p "$DST/Assets"
if [ ! -f "$DST/.copied" ]; then
  for d in Packages ProjectSettings; do cp -r "$SRC/$d" "$DST/"; done
  for f in "$SRC"/Assets/*; do
    n=$(basename "$f")
    [ "$n" = "#User" ] && continue
    cp -r "$f" "$DST/Assets/"
  done
  touch "$DST/.copied"
fi
# 1.5: Fusion (Assets/Photon), its package and its defines came after the first copy: kept in step
# Photon: copied when missing, and replaced whole when the project's Fusion changed (2026-10-21: 2.0.12 -> 2.1.3 left 2.0 here)
if ! cmp -s "$SRC/Assets/Photon/Fusion/Assemblies/Fusion.Runtime.dll" "$DST/Assets/Photon/Fusion/Assemblies/Fusion.Runtime.dll"; then
  rm -rf "$DST/Assets/Photon"
  cp -r "$SRC/Assets/Photon" "$DST/Assets/"; cp "$SRC/Assets/Photon.meta" "$DST/Assets/"
fi
cmp -s "$SRC/Packages/manifest.json" "$DST/Packages/manifest.json" || cp "$SRC/Packages/manifest.json" "$DST/Packages/"
cmp -s "$SRC/ProjectSettings/ProjectSettings.asset" "$DST/ProjectSettings/ProjectSettings.asset" || cp "$SRC/ProjectSettings/ProjectSettings.asset" "$DST/ProjectSettings/"
rm -rf "$DST/Assets/#User"
cp -r "$SRC/Assets/#User" "$DST/Assets/#User"
cp "$SRC/Assets/#User.meta" "$DST/Assets/"
cp "$SRC/Assets/Metroid Prime Scene.unity" "$SRC/Assets/Metroid Prime Scene.unity.meta" "$DST/Assets/"
echo "lab copy ready: $DST"
