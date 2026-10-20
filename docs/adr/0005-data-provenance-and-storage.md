# ADR-0005: Data provenance, pipeline and storage of large data

| | |
|---|---|
| Status | Accepted |
| Date | 2026-10-20 |

## Context

The project requires every simulated value to come from a verified source and to be traceable to it. Source files range from a few kilobytes (text kernels) to gigabytes (long-span and satellite ephemerides). Git LFS is not installed on the development machine, and no remote has been chosen yet.

## Decision

1. **Registry and lock.** Sources are declared in `data/sources.json` and documented in `docs/DATA_SOURCES.md` with a tier. Exact files are pinned in `data/sources.lock.json` (URL, size, SHA-256, publisher checksum where available, retrieval time). Both files are committed.
2. **Pipeline.** `tools/UniverseSim.DataPipeline` fetches and verifies the sources and builds a versioned binary **data pack** (`.uspack`). The pack is the only data input the core reads at runtime. The pipeline is reproducible: the same lock file gives an identical pack.
3. **Per-field provenance.** Every catalog value in the pack references its source (registry id plus file and keyword, or table). Derived values record their formula and inputs. Missing values stay missing.
4. **No JSON in the core.** Human-readable inputs are compiled to the binary pack, so the core needs no parser dependencies.
5. **Storage.**
   - Committed: the registry, the lock file, catalog selection and manual entries, and reference vectors (small text files).
   - Not committed: raw downloads (`data/raw/`), generated packs (`data/packs/`) and the copies in Unity `StreamingAssets`. All are reproducible from the lock file.
   - Git LFS is not used for now. If committing large binaries becomes necessary (for example textures or prebuilt packs for non-developers), install git-lfs and record the change in a new ADR.
6. **Manual transcription** from non-machine-readable Tier 1 pages is allowed only through `data/catalog/manual/`, with the source URL, access date and exact table recorded. The pipeline rejects incomplete entries.

## Consequences

- A fresh clone needs `fetch` and `build` before the Unity viewer has data. This is documented in the README. A future "download prebuilt pack" option can make this easier for non-developers.
- The repository stays small. The Unity Library folder (about 2 GB) and the kernels never enter git.
- Publisher outages or file removals could block a fresh build. The lock file's checksums make it safe to restore from any mirror or backup copy.
- The UI can show a citation for every value.

## Alternatives considered

- **Commit kernels to git.** `de440s.bsp` alone is 31 MB, and satellite kernels run to hundreds of MB. Rejected.
- **Git LFS from the start.** Not installed, needs remote support, and adds friction while nothing large needs committing. Deferred.
- **Read raw kernels directly at runtime (no pack).** Simpler at first, but it ships hundreds of MB of unneeded coverage, spreads provenance across many formats, and leaves non-.NET ports without a single format to implement. Rejected.
- **JSON data pack.** Readable, but large for Chebyshev coefficients, and it would need a JSON parser in the core. Rejected for the runtime format. JSON stays the format for human-edited pipeline inputs.
