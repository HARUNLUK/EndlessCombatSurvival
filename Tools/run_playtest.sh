#!/bin/bash
# Runs the virtual gamepad play test (editor window opens, plays, quits). Optional arg 1: combat | rules | ui | vehicle | enemy
# Optional arg 2: savas = run it in the action test scene Assets/Scenes/Test/Test_Savas (built if missing)
cd "/d/Development/Endless Combat Survival Game/Project/EndlessCombatSurvival"
EXTRA=""
if [ -n "$1" ]; then EXTRA="-ptOnly $1"; fi
if [ -n "$2" ]; then EXTRA="$EXTRA -ptScene $2"; fi
"/c/Program Files/Unity/Hub/Editor/6000.5.10f1/Editor/Unity.exe" -projectPath "D:\Development\Endless Combat Survival Game\Project\EndlessCombatSurvival" -executeMethod EndlessSurvival.Survival.Editor.NightPlaytest.Run $EXTRA -logFile "Logs/night_playtest.log"
echo exit=$?
grep "error CS" Logs/night_playtest.log | sort -u | head -20
cat PoseShots/pt_log.txt
