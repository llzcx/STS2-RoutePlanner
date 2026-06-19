#!/bin/bash
# Auto-deploy: kill game, build, deploy DLL + config + locales, launch
GODOT_EXE="D:/dev/mod-dev/godot/Godot_v4.5.1-stable_mono_win64/Godot_v4.5.1-stable_mono_win64.exe"
PROJECT_DIR="D:/project/game/StS2/SlayTheSpire2"
MOD_DIR="D:/project/game/StS2/all-mods/RoutePlanner"
RUNTIME_DIR="D:/dev/mod-dev/godot/Godot_v4.5.1-stable_mono_win64/mods/RoutePlanner"
# 1. Kill ALL Godot processes and wait for DLL to unlock
echo "Killing Godot..."
powershell -Command "Get-Process -Name 'Godot*' -ErrorAction SilentlyContinue | Stop-Process -Force" 2>/dev/null
for i in 1 2 3 4 5; do
    if [ -f "$RUNTIME_DIR/route_planner.dll" ]; then
        if cp "$RUNTIME_DIR/route_planner.dll" "$RUNTIME_DIR/route_planner.dll.test" 2>/dev/null; then
            rm -f "$RUNTIME_DIR/route_planner.dll.test" 2>/dev/null
            break
        fi
    fi
    echo "  Waiting ($i)..."
    sleep 1
done

# 2. Build
cd "$MOD_DIR"
echo "Building..."
dotnet build RoutePlanner.csproj --nologo -v q
if [ $? -ne 0 ]; then
    echo "BUILD FAILED"
    exit 1
fi

# 3. Deploy DLL + config + locales to dev runtime
mkdir -p "$RUNTIME_DIR/config" "$RUNTIME_DIR/locale"
cp bin/Debug/net9.0/route_planner.dll "$MOD_DIR/route_planner.dll"
cp "$MOD_DIR/route_planner.dll" "$RUNTIME_DIR/route_planner.dll"
cp "$MOD_DIR/manifest.json" "$RUNTIME_DIR/manifest.json"
cp "$MOD_DIR/config/"*.json "$RUNTIME_DIR/config/"
cp "$MOD_DIR/locale/"*.json "$RUNTIME_DIR/locale/"
echo "Deployed dev ($(ls -lh "$RUNTIME_DIR/route_planner.dll" | awk '{print $5}'))"

# 4. Launch (game mode, not editor)
echo "Launching..."
"$GODOT_EXE" --path "$PROJECT_DIR" &
echo "Done."
