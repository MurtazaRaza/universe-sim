#!/usr/bin/env bash
# Copies the data packs into the Unity project's StreamingAssets, where player builds load them
# (SimulationHost). In the Editor the host also finds the packs in data/packs, so this is needed for builds.
#
# Usage: tools/scripts/copy-pack-to-unity.sh [--default-only]
#
# Copies the newest default pack (solar-system-<version>.uspack) and, unless --default-only is given, the
# newest optional moons pack (solar-system-moons-<version>.uspack, about 300 MB) if one has been built. The
# host prefers the moons pack. The copies are ignored by git (UniverseSim/.gitignore); the committed
# default pack stays in data/packs (ADR-0006).
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
dest_dir="$repo_root/UniverseSim/Assets/StreamingAssets/UniverseSim"

with_moons=1
case "${1:-}" in
    "") ;;
    --default-only) with_moons=0 ;;
    *) echo "usage: $0 [--default-only]" >&2; exit 2 ;;
esac

newest() {
    find "$repo_root/data/packs" -maxdepth 1 -name "$1" | sort | tail -n 1
}

pack="$(newest 'solar-system-[0-9]*.uspack')"
if [ -z "$pack" ]; then
    echo "error: no data/packs/solar-system-<version>.uspack; build it (tools/pipeline/usim-pipeline build) or git lfs pull" >&2
    exit 1
fi
if [ "$(head -c 23 "$pack")" = "version https://git-lfs" ]; then
    echo "error: ${pack#"$repo_root"/} is a Git LFS pointer; run git lfs pull first" >&2
    exit 1
fi
moons=""
if [ "$with_moons" -eq 1 ]; then
    moons="$(newest 'solar-system-moons-[0-9]*.uspack')"
fi

mkdir -p "$dest_dir"
rm -f "$dest_dir"/solar-system-*.uspack "$dest_dir"/solar-system-*.uspack.meta
for file in "$pack" $moons; do
    cp "$file" "$dest_dir/"
    echo "Copied ${file#"$repo_root"/} to ${dest_dir#"$repo_root"/}/"
done
