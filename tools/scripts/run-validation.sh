#!/usr/bin/env bash
# Runs the validation tests (SPEC section 9: VAL-01 to VAL-04 and the pack's kernel values) against the
# default data pack, getting the pack first if it is missing.
#
# Usage: tools/scripts/run-validation.sh [preset]      (preset: dev, release or asan; default dev)
#
# The default pack is committed through Git LFS (ADR-0006). If the checkout holds only the LFS pointer, the
# script checks the pack out from the local LFS cache, or pulls it from the remote. If that fails or the
# pack is absent, it builds the pack with the data pipeline (fetch, then build), which needs the
# pipeline's virtual environment (see CLAUDE.md).
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
preset="${1:-dev}"
pipeline="$repo_root/tools/pipeline/usim-pipeline"

# The newest default pack, if any (the same choice as core/tests/validation/validation_support.hpp).
find_pack() {
    find "$repo_root/data/packs" -maxdepth 1 -name 'solar-system-[0-9]*.uspack' 2>/dev/null | sort | tail -n 1
}

is_lfs_pointer() {
    [ "$(head -c 23 "$1")" = "version https://git-lfs" ]
}

pack="$(find_pack)"
if [ -n "$pack" ] && is_lfs_pointer "$pack"; then
    echo "The default pack is a Git LFS pointer; checking out its content."
    (cd "$repo_root" && git lfs checkout data/packs) || true
    if is_lfs_pointer "$pack"; then
        (cd "$repo_root" && git lfs pull --include "data/packs/*") || true
    fi
fi
pack="$(find_pack)"
if [ -z "$pack" ] || is_lfs_pointer "$pack"; then
    echo "No default pack; building it with the data pipeline."
    "$pipeline" fetch
    "$pipeline" build
    pack="$(find_pack)"
fi
echo "Pack: ${pack#"$repo_root"/}"

cd "$repo_root/core"
cmake --preset "$preset"
cmake --build --preset "$preset"
ctest --preset "$preset" -L validation --output-on-failure
