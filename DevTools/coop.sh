#!/bin/bash
# The two-client co-op lab (Editor/MpLabCoop.cs): the MASTER plays in the main project, the HELPER in the private lab copy
# (DevTools/labproject, refreshed by labcopy.sh), the two joined by a localhost socket (Editor/MpLabLink.cs) instead of Fusion.
# One Unity per project: this needs the user's own Unity CLOSED for METROIDPRIME (it never closes it - it refuses to run).
# Takes the same queue lock as lab.sh. Usage: coop.sh [port] [scenario: coop | coopcannon]. Prints both sides' LAB lines and the CHECK verdicts.
LOCK="/d/Dreamport/Metroid Prime/METROIDPRIME/DevTools/lab.lock"
MAIN="/d/Dreamport/Metroid Prime/METROIDPRIME"
if [ -f "$MAIN/Temp/UnityLockfile" ]; then echo "NOT RUN: the project is open in Unity (Temp/UnityLockfile) - the co-op lab needs both the project and its lab copy"; exit 2; fi
waited=0
until mkdir "$LOCK" 2>/dev/null; do
  if [ $waited -ge 3000 ]; then echo "lab lock stale - taking it"; rm -rf "$LOCK"; continue; fi
  [ $waited = 0 ] && echo "waiting for another lab run to finish..."
  sleep 10; waited=$((waited+10))
done
trap 'rm -rf "$LOCK"' EXIT
bash "$MAIN/DevTools/labcopy.sh" || exit 1
PORT="${1:-47611}"; SC="${2:-coop}"
U="/c/Program Files/Unity/Hub/Editor/6000.3.5f2/Editor/Unity.exe"
OUT='D:\Dreamport\Metroid Prime\METROIDPRIME\DevTools\lab'
LM="$MAIN/DevTools/lab/lab_coop_master.log"; LH="$MAIN/DevTools/lab/lab_coop_helper.log"
timeout 3000 "$U" -batchmode -projectPath 'D:\Dreamport\Metroid Prime\METROIDPRIME' -mplab "$SC" -mpcoop master -mpport "$PORT" -mpout "$OUT" -logFile "$OUT\lab_coop_master.log" &
PM=$!
sleep 5
timeout 3000 "$U" -batchmode -projectPath 'D:\Dreamport\Metroid Prime\METROIDPRIME\DevTools\labproject' -mplab "$SC" -mpcoop helper -mpport "$PORT" -mpout "$OUT" -logFile "$OUT\lab_coop_helper.log" &
PH=$!
wait $PM; EM=$?
wait $PH; EH=$?
echo "unity exit: master $EM, helper $EH"
for L in "$LM" "$LH"; do
  echo "=== $(basename "$L")"
  grep -a -n "error CS\|Exception" "$L" | grep -v "LogAssemblyErrors" | head -10
  grep -a "LAB:\|Co-op:\|Net:" "$L" | head -120
done
echo "=== verdicts"
grep -a -h "LAB: CHECK" "$LM" "$LH"
