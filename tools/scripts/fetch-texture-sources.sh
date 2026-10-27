#!/usr/bin/env bash
# Gets the sources of the surface map textures (P6-07, ADR-0014) and builds the textures. It downloads a lot,
# so run it on a fast connection:
#
#   1. the 15 USGS global mosaics into data/raw/usgs/ (about 4.6 GB; resumable, each checked against the
#      MD5 file USGS publishes next to it), with tools/scripts/download-phase6-kernels.sh --group textures;
#   2. tools/pipeline/usim-pipeline fetch --optional: pins those mosaics in data/sources.lock.json without
#      downloading them again, and downloads and pins the small files: the ISIS and PDS3 labels, the three
#      NASA maps (Moon, Earth, Jupiter) and the Mimas archive (about 160 MB). It also re-checks the other
#      optional files already here (the satellite kernels), which takes a few minutes but no network;
#   3. tools/pipeline/usim-pipeline textures (offline): writes UniverseSim/Assets/UniverseSim/Textures/Bodies/
#      (19 JPEGs and BodyTextures.json) and UniverseSim/Assets/UniverseSim/ATTRIBUTION.md.
#
# It is safe to stop and run again: finished downloads are kept, partial ones resume, and fetch keeps the
# files it pinned before a failure.
#
# Usage: tools/scripts/fetch-texture-sources.sh [--download-only] [--help]
#
#   --download-only   steps 1 and 2 only (everything that needs the network)
#
# Afterwards, with the Unity Editor closed, import the textures and rebuild the main scene (see CLAUDE.md,
# "Rebuild the main scene"), then run the tests.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
download_only=0
while [ $# -gt 0 ]; do
    case "$1" in
        --download-only) download_only=1; shift ;;
        -h | --help) awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "${BASH_SOURCE[0]}"; exit 0 ;;
        *) echo "error: unknown option '$1' (see --help)" >&2; exit 1 ;;
    esac
done

echo "== 1/3: USGS mosaics"
"$repo_root/tools/scripts/download-phase6-kernels.sh" --group textures

echo
echo "== 2/3: pinning (and the small files)"
"$repo_root/tools/pipeline/usim-pipeline" fetch --optional

if [ "$download_only" -eq 1 ]; then
    echo
    echo "Downloads done. Build the textures later with: tools/pipeline/usim-pipeline textures"
    exit 0
fi

echo
echo "== 3/3: textures"
"$repo_root/tools/pipeline/usim-pipeline" textures

echo
echo "Done. Next, with the Unity Editor closed: rebuild the main scene (it picks up the textures), then run"
echo "the pipeline tests (tools/pipeline/.venv/bin/python -m pytest tools/pipeline) and the Unity tests."
