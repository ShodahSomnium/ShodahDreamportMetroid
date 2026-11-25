#!/bin/bash
# Runs the play-mode lab (Editor/MpLab.cs) in Unity batch mode with a graphics device. Usage: lab.sh <scenario> [extra args...]
# Pictures and the log go to DevTools/lab. Prints the LAB / [SomniumMetroid] lines and any shader or script errors.
# When the user's own Unity has the project open (Temp/UnityLockfile), the lab runs in the private copy DevTools/labproject
# (refreshed by labcopy.sh) - never close or kill the user's editor.
# one lab at a time (several helpers may queue): an atomic lock folder, taken over when its owner is gone for 50 min
LOCK="/d/Dreamport/Metroid Prime/METROIDPRIME/DevTools/lab.lock"
waited=0
until mkdir "$LOCK" 2>/dev/null; do
  if [ $waited -ge 3000 ]; then echo "lab lock stale - taking it"; rm -rf "$LOCK"; continue; fi
  [ $waited = 0 ] && echo "waiting for another lab run to finish..."
  sleep 10; waited=$((waited+10))
done
trap 'rm -rf "$LOCK"' EXIT
P='D:\Dreamport\Metroid Prime\METROIDPRIME'
OUT="$P\DevTools\lab"
if [ -f "/d/Dreamport/Metroid Prime/METROIDPRIME/Temp/UnityLockfile" ]; then
  bash "/d/Dreamport/Metroid Prime/METROIDPRIME/DevTools/labcopy.sh" || exit 1
  P='D:\Dreamport\Metroid Prime\METROIDPRIME\DevTools\labproject'
fi
S="${1:-view}"; shift
LOG="/d/Dreamport/Metroid Prime/METROIDPRIME/DevTools/lab/lab_$S.log"
timeout 3000 "/c/Program Files/Unity/Hub/Editor/6000.3.5f2/Editor/Unity.exe" -batchmode -projectPath "$P" -mplab "$S" -mpout "$OUT" -logFile "$OUT\lab_$S.log" "$@"
echo "unity exit $? (project $P)"
grep -a -n "Shader error\|error CS\|Exception" "$LOG" | grep -v "LogAssemblyErrors" | head -20
grep -a "LAB:\|SomniumMetroid\]" "$LOG" | head -90
