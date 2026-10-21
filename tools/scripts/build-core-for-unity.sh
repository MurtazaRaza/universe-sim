#!/usr/bin/env bash
# Builds the native core library (universesim) in Release and copies it into the Unity project.
#
# Usage: tools/scripts/build-core-for-unity.sh
#
# The plugin binary is a build artifact and is not committed (see docs/adr/0002). Its import
# settings are applied by UniverseSim/Assets/UniverseSim/Editor/NativePluginImportSettings.cs.
#
# The Unity Editor keeps native plugins loaded once they are used. Restart the Editor after
# replacing the plugin, or the old binary stays in memory.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
core_dir="$repo_root/core"
plugins_dir="$repo_root/UniverseSim/Assets/UniverseSim/Plugins"

case "$(uname -s)" in
    Darwin)
        preset="unity-macos"
        library="universesim.dylib"
        dest_dir="$plugins_dir/macOS"
        ;;
    *)
        echo "error: this script supports macOS only so far (IMPLEMENTATION_PLAN P0-13 adds Windows)." >&2
        exit 1
        ;;
esac

(cd "$core_dir" && cmake --preset "$preset" && cmake --build --preset "$preset")

mkdir -p "$dest_dir"
cp "$core_dir/build/$preset/capi/$library" "$dest_dir/$library"

echo "Copied $library to ${dest_dir#"$repo_root"/}/"
echo "If the Unity Editor is open and has used the plugin, restart it to load the new binary."
