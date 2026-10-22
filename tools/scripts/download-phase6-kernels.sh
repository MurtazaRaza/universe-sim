#!/usr/bin/env bash
# Downloads the large NAIF kernels that Phase 6 needs into data/raw/naif/, where the data pipeline reads
# its inputs (the "naif/<file>" paths of data/sources.json). The full set is about 3.4 GB, so run it on a
# fast connection. `tools/pipeline/usim-pipeline fetch --optional` downloads the same satellite kernels
# without progress or resume; this script is the friendlier way on a slow or unreliable connection. It is
# safe to stop and run again: finished files are checked and kept, and interrupted downloads resume where
# they stopped.
#
# Usage: tools/scripts/download-phase6-kernels.sh [--group NAME]... [--dry-run] [--help]
#
#   --group NAME   download only this group (repeatable); default: every group
#   --dry-run      list the files, their sizes and what is already here, then exit without downloading
#
# Groups, in download order (selected from NAIF aa_summaries.txt, read 2026-10-21):
#   moons          the satellite kernels of the optional moons pack (S-NAIF-SAT, ADR-0010), one per planet
#                  system, each holding the planet center and its major moons: mar099, jup365, sat441,
#                  ura184_part-3, nep097, plu060. About 3.4 GB.
#   orientation    optional binary PCKs: the lunar principal axes (P6-05) and the Earth (P6-06). About 17 MB.
#                  Not registered yet.
# (The Uranus alternative ura111xl-701 to 705 and -799, about 6.7 GB, was dropped on 2026-10-22: ADR-0010
# chose ura184_part-3.)
#
# Checks: satellite kernels must match NAIF's aa_checksums.txt (MD5), downloaded on every run. The pck
# directory has no checksum list, so those files are checked against the size the server reports. The
# small text files that document the kernels (aa_summaries.txt, aa_checksums.txt and one .cmt per kernel)
# go to data/raw/naif/meta/satellites/. Everything under data/raw/ is ignored by git.
#
# Afterwards: tools/pipeline/usim-pipeline fetch checks the local copies against the lock file (they are
# declared in data/sources.json), then build --pack solar-system-moons builds the moons pack.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
raw_dir="$repo_root/data/raw/naif"
meta_dir="$raw_dir/meta/satellites"
sat_url="https://naif.jpl.nasa.gov/pub/naif/generic_kernels/spk/satellites"
pck_url="https://naif.jpl.nasa.gov/pub/naif/generic_kernels/pck"
all_groups="moons orientation"

# group|directory URL|file|contents (bodies as listed in aa_summaries.txt)
files=(
    "moons|$sat_url|mar099.bsp|Mars, Phobos, Deimos"
    "moons|$sat_url|jup365.bsp|Jupiter, Io, Europa, Ganymede, Callisto and inner moons"
    "moons|$sat_url|sat441.bsp|Saturn, Mimas to Phoebe and small moons"
    "moons|$sat_url|ura184_part-3.bsp|Uranus, Ariel, Umbriel, Titania, Oberon, Miranda, irregular moons"
    "moons|$sat_url|nep097.bsp|Neptune, Triton"
    "moons|$sat_url|plu060.bsp|Pluto, Charon, Nix, Hydra, Kerberos, Styx"
    "orientation|$pck_url|moon_pa_de440_200625.bpc|Moon principal axes orientation"
    "orientation|$pck_url|earth_latest_high_prec.bpc|Earth orientation (NAIF updates it often)"
)

die() {
    echo "error: $*" >&2
    exit 1
}

usage() {
    awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "${BASH_SOURCE[0]}"
}

# Size of a file in bytes, or 0 when it does not exist (GNU stat first, then BSD stat).
file_size() {
    if [ -f "$1" ]; then
        stat -c %s "$1" 2>/dev/null || stat -f %z "$1"
    else
        echo 0
    fi
}

# The Content-Length the server reports for a URL, or nothing when it does not say.
remote_size() {
    local headers
    headers="$(curl -sSfIL --connect-timeout 30 --max-time 60 "$1" 2>/dev/null)" || return 0
    printf '%s\n' "$headers" | tr -d '\r' | awk 'tolower($1) == "content-length:" { n = $2 } END { print n }'
}

human() {
    awk -v b="$1" 'BEGIN {
        if (b >= 1e9) printf "%.2f GB", b / 1e9
        else if (b >= 1e6) printf "%.1f MB", b / 1e6
        else if (b >= 1e3) printf "%.0f KB", b / 1e3
        else printf "%d B", b
    }'
}

human_size() {  # like human, but 0 means the server did not report a size
    if [ "$1" -gt 0 ]; then human "$1"; else printf 'unknown'; fi
}

duration() {
    local s=$1
    if [ "$s" -ge 3600 ]; then
        printf '%dh%02dm%02ds' $((s / 3600)) $((s % 3600 / 60)) $((s % 60))
    elif [ "$s" -ge 60 ]; then
        printf '%dm%02ds' $((s / 60)) $((s % 60))
    else
        printf '%ds' "$s"
    fi
}

bar() {  # bar <permille> <width>
    local filled=$(($1 * $2 / 1000))
    printf '[%s%s]' "$(printf '%*s' "$filled" '' | tr ' ' '#')" "$(printf '%*s' $(($2 - filled)) '' | tr ' ' '-')"
}

md5_of() {
    if command -v md5sum >/dev/null 2>&1; then
        md5sum "$1" | awk '{ print $1 }'
    else
        md5 -q "$1"
    fi
}

# The MD5 NAIF lists for a satellite kernel, or nothing.
publisher_md5() {
    awk -v name="$1" '$2 == name || $2 == "*" name { print tolower($1) }' "$meta_dir/aa_checksums.txt"
}

# Command line.
groups=""
dry_run=0
while [ $# -gt 0 ]; do
    case "$1" in
        --group)
            [ $# -ge 2 ] || die "--group needs a name ($all_groups)"
            case " $all_groups " in
                *" $2 "*) groups="$groups $2" ;;
                *) die "unknown group '$2' (groups: $all_groups)" ;;
            esac
            shift 2
            ;;
        --dry-run) dry_run=1; shift ;;
        -h | --help) usage; exit 0 ;;
        *) die "unknown option '$1' (see --help)" ;;
    esac
done
[ -n "$groups" ] || groups="$all_groups"
command -v curl >/dev/null 2>&1 || die "curl is required"
command -v md5sum >/dev/null 2>&1 || command -v md5 >/dev/null 2>&1 || die "md5sum or md5 is required"

selected=()
for entry in "${files[@]}"; do
    case " $groups " in
        *" ${entry%%|*} "*) selected+=("$entry") ;;
    esac
done
count=${#selected[@]}

# Stop cleanly on Ctrl-C: curl runs in the background (for the progress line), where it ignores SIGINT.
curl_pid=""
stop_curl() {
    if [ -n "$curl_pid" ]; then
        kill "$curl_pid" 2>/dev/null || true
        wait "$curl_pid" 2>/dev/null || true
        curl_pid=""
    fi
}
on_interrupt() {
    stop_curl
    printf '\n\nStopped. Partial downloads are kept as data/raw/naif/*.part; run the script again to resume.\n' >&2
    exit 130
}
trap on_interrupt INT TERM
trap stop_curl EXIT

# The plan: sizes from the server, and what is already here.
echo "Phase 6 kernels -> data/raw/naif/"
echo "Groups: ${groups# }"
echo
echo "Asking the server for file sizes..."
sizes=()
total=0
need=0
printf '\n  %-3s %-28s %-13s %10s  %s\n' "#" "File" "Group" "Size" "Already here"
index=0
for entry in "${selected[@]}"; do
    index=$((index + 1))
    IFS='|' read -r group base name what <<<"$entry"
    size="$(remote_size "$base/$name")"
    size="${size:-0}"
    sizes+=("$size")
    total=$((total + size))
    if [ -f "$raw_dir/$name" ]; then
        here="yes"
    elif [ -f "$raw_dir/$name.part" ]; then
        have=$(file_size "$raw_dir/$name.part")
        here="partial, $(human "$have")"
        need=$((need + size - have))
    else
        here="no"
        need=$((need + size))
    fi
    printf '  %-3s %-28s %-13s %10s  %s\n' "$index" "$name" "$group" "$(human_size "$size")" "$here"
done
echo
echo "Total: $(human "$total") in $count files. Left to download: $(human "$need")."

mkdir -p "$repo_root/data/raw"
free_kb=$(df -Pk "$repo_root/data/raw" | awk 'NR == 2 { print $4 }')
free=$((free_kb * 1024))
echo "Free disk space: $(human "$free")."
if [ "$need" -gt "$free" ]; then
    [ "$dry_run" -eq 1 ] || die "not enough disk space: $(human "$need") needed, $(human "$free") free"
    echo "warning: not enough disk space for the downloads"
fi
if [ "$dry_run" -eq 1 ]; then
    echo
    echo "Dry run: nothing downloaded."
    exit 0
fi

# NAIF's checksum list and summaries for the satellite kernels (small).
mkdir -p "$raw_dir"
case "${selected[*]}" in
    *"$sat_url"*)
        mkdir -p "$meta_dir"
        echo
        echo "Getting NAIF's checksum list and kernel summaries..."
        for listing in aa_checksums.txt aa_summaries.txt; do
            curl -sSf --connect-timeout 30 --max-time 300 -o "$meta_dir/$listing.tmp" "$sat_url/$listing" ||
                die "cannot download $sat_url/$listing"
            mv "$meta_dir/$listing.tmp" "$meta_dir/$listing"
        done
        ;;
esac

# Downloads one file into its .part file, resuming, with a progress line once a second.
watch_progress() {  # part size start_bytes
    local part=$1 size=$2 start=$3 started=$SECONDS last_print=-30 have elapsed speed line permille eta
    while kill -0 "$curl_pid" 2>/dev/null; do
        have=$(file_size "$part")
        elapsed=$((SECONDS - started))
        speed=0
        if [ "$elapsed" -gt 0 ]; then speed=$(((have - start) / elapsed)); fi
        if [ "$size" -gt 0 ]; then
            permille=$((have * 1000 / size))
            eta="--"
            if [ "$speed" -gt 0 ]; then eta="$(duration $(((size - have) / speed)))"; fi
            line="  $(bar "$permille" 16) $(printf '%3d.%d%%' $((permille / 10)) $((permille % 10)))"
            line="$line  $(human "$have")/$(human "$size")  $(human "$speed")/s  ETA $eta"
            if [ "$total" -gt 0 ]; then line="$line  all: $(((done_bytes + have) * 100 / total))%"; fi
        else
            line="  $(human "$have")  $(human "$speed")/s"
        fi
        if [ -t 1 ]; then
            printf '\r%s\033[K' "$line"
        elif [ $((SECONDS - last_print)) -ge 30 ]; then
            echo "$line"
            last_print=$SECONDS
        fi
        sleep 1
    done
    if [ -t 1 ]; then printf '\r\033[K'; fi
}

fetch_file() {  # url part size; returns non-zero after repeated failures
    local url=$1 part=$2 size=$3 attempt have status
    for attempt in 1 2 3 4 5 6 7 8; do
        have=$(file_size "$part")
        if [ "$size" -gt 0 ] && [ "$have" -eq "$size" ]; then return 0; fi
        if [ "$size" -gt 0 ] && [ "$have" -gt "$size" ]; then
            rm -f "$part"
            have=0
        fi
        if [ "$have" -gt 0 ]; then echo "  resuming at $(human "$have")"; fi
        curl --silent --show-error --fail --location --continue-at - \
            --connect-timeout 30 --speed-limit 10000 --speed-time 120 \
            --output "$part" "$url" &
        curl_pid=$!
        watch_progress "$part" "$size" "$have"
        status=0
        wait "$curl_pid" || status=$?
        curl_pid=""
        if [ "$status" -eq 0 ]; then return 0; fi
        if [ "$status" -eq 33 ]; then rm -f "$part"; fi  # the server refused to resume: start over
        echo "  attempt $attempt failed (curl exit code $status); trying again in $((attempt * 5)) s"
        sleep $((attempt * 5))
    done
    return 1
}

results=()
record() {  # record <ok|FAILED> <file> <message>
    results+=("$(printf '  %-7s %-28s %s' "$1" "$2" "$3")")
    if [ "$1" = "FAILED" ]; then failures=$((failures + 1)); fi
}

failures=0
done_bytes=0
run_started=$SECONDS
index=0
for entry in "${selected[@]}"; do
    index=$((index + 1))
    IFS='|' read -r group base name what <<<"$entry"
    size="${sizes[$((index - 1))]}"
    dest="$raw_dir/$name"
    part="$dest.part"
    echo
    echo "[$index/$count] $name ($(human_size "$size"), $group): $what"

    expected_md5=""
    if [ "$base" = "$sat_url" ]; then
        expected_md5="$(publisher_md5 "$name")"
        if [ -z "$expected_md5" ]; then
            echo "  not in NAIF's checksum list; skipped"
            record FAILED "$name" "not listed in $sat_url/aa_checksums.txt"
            continue
        fi
        cmt="$meta_dir/${name%.bsp}.cmt"
        if [ ! -f "$cmt" ]; then
            curl -sSf --connect-timeout 30 --max-time 300 -o "$cmt.tmp" "$sat_url/${name%.bsp}.cmt" &&
                mv "$cmt.tmp" "$cmt" || { rm -f "$cmt.tmp"; echo "  (no comment file on the server)"; }
        fi
    fi

    if [ -f "$dest" ]; then
        if [ -n "$expected_md5" ]; then
            printf '  already here; checking its MD5... '
            actual="$(md5_of "$dest")"
            if [ "$actual" = "$expected_md5" ]; then
                echo "matches NAIF"
                record ok "$name" "already here, MD5 matches NAIF"
            else
                echo "DIFFERENT"
                record FAILED "$name" "the local file's MD5 ($actual) is not NAIF's ($expected_md5); move it away and run again"
            fi
        elif [ "$size" -eq 0 ] || [ "$(file_size "$dest")" -eq "$size" ]; then
            echo "  already here"
            record ok "$name" "already here"
        else
            echo "  already here, but its size differs from the server's"
            record FAILED "$name" "the local file's size differs from the server's; move it away to download it again"
        fi
        done_bytes=$((done_bytes + size))
        continue
    fi

    started=$SECONDS
    start_bytes=$(file_size "$part")
    if ! fetch_file "$base/$name" "$part" "$size"; then
        record FAILED "$name" "download failed repeatedly; run again to resume"
        done_bytes=$((done_bytes + size))
        continue
    fi
    elapsed=$((SECONDS - started))
    got=$(($(file_size "$part") - start_bytes))
    if [ "$got" -gt 0 ]; then
        rate=""
        if [ "$elapsed" -gt 0 ]; then rate=", $(human $((got / elapsed)))/s"; fi
        echo "  downloaded $(human "$got") in $(duration "$elapsed")$rate"
    else
        echo "  the partial download from an earlier run was already complete"
    fi

    if [ -n "$expected_md5" ]; then
        printf '  checking MD5 against NAIF... '
        actual="$(md5_of "$part")"
        if [ "$actual" = "$expected_md5" ]; then
            echo "ok"
            mv "$part" "$dest"
            record ok "$name" "downloaded, MD5 matches NAIF"
        else
            echo "MISMATCH"
            rm -f "$part"
            record FAILED "$name" "MD5 $actual is not NAIF's $expected_md5; the download was removed, run again"
        fi
    elif [ "$size" -gt 0 ] && [ "$(file_size "$part")" -ne "$size" ]; then
        rm -f "$part"
        record FAILED "$name" "the size differs from the server's; the download was removed, run again"
    else
        mv "$part" "$dest"
        record ok "$name" "downloaded, size matches the server (NAIF has no checksum list for pck/)"
    fi
    done_bytes=$((done_bytes + size))
done

echo
echo "Summary ($count files, $(human "$total"), $(duration $((SECONDS - run_started)))):"
printf '%s\n' ${results[@]+"${results[@]}"}
echo
if [ "$failures" -gt 0 ]; then
    echo "$failures file(s) failed. Run the script again: finished files are kept and partial ones resume."
    exit 1
fi
echo "All files are in data/raw/naif/ (documentation in data/raw/naif/meta/satellites/)."
echo "Next: tools/pipeline/usim-pipeline fetch (checks these copies against the lock file), then"
echo "tools/pipeline/usim-pipeline build --pack solar-system-moons."
