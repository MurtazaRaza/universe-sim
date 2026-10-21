# ADR-0005: Data provenance, pipeline and storage of large data

| | |
|---|---|
| Status | Accepted |
| Date | 2026-10-20 |

## Context

The project requires every simulated value to come from a verified source and to be traceable to it. Source files range from a few kilobytes (text kernels) to gigabytes (long-span and satellite ephemerides). The core is a C++ library (ADR-0002) that should stay free of third-party dependencies. Git LFS is installed and enabled on the development machine (git-lfs 3.8.0, 2026-10-20). No remote has been chosen yet.

## Decision

1. **Registry and lock.** Sources are declared in `data/sources.json` and documented in `docs/DATA_SOURCES.md` with a tier. Exact files are pinned in `data/sources.lock.json` (URL, size, SHA-256, publisher checksum where available, retrieval time). Both files are committed.
2. **The pipeline is written in Python.** `tools/pipeline/` is a Python package (Python 3.12 or newer, dependencies pinned in `requirements.txt`) with the commands `fetch`, `verify`, `build`, `info` and `reference`. It runs only on developer machines (and in CI once it exists) and never ships. Python is chosen because it has a mature HTTP stack, JSON handling and hashing in the standard library, and NASA's own CSPICE through SpiceyPy for reading kernels and generating reference data.
3. **The data pack.** The pipeline builds a versioned binary data pack (`.uspack`). The pack is the only data input the core reads at runtime. Its format is specified in `docs/formats/DATA_PACK.md` (Phase 2). The writer (Python) and the reader (C++) are independent implementations of that specification, and round-trip tests keep them consistent. The pipeline is reproducible: the same lock file gives an identical pack.
4. **Per-field provenance.** Every catalog value in the pack references its source (registry id plus file and keyword, or table). Derived values record their formula and inputs. Missing values stay missing.
5. **No JSON in the core.** Human-readable inputs are compiled into the binary pack, so the core needs no parser dependencies.
6. **Storage.**
   - Committed as normal text: the registry, the lock file, catalog selection and manual entries, and reference vectors.
   - Committed through **Git LFS**: binary assets (textures, models, audio, video, fonts, PDFs, and any binary kernel or pack that is ever deliberately committed). The patterns are in `.gitattributes`.
   - Not committed: raw downloads (`data/raw/`), generated packs (`data/packs/`), copies in Unity `StreamingAssets`, and native plugin binaries. All are reproducible from the lock file or the build.
   - Whether to commit a prebuilt default pack through LFS, so a fresh clone can run the viewer without the pipeline, is decided in Phase 2 (OQ-02).
7. **Manual transcription** from non-machine-readable Tier 1 pages is allowed only through `data/catalog/manual/`, with the source URL, access date and exact table recorded. The pipeline rejects incomplete entries.

## Consequences

- A fresh clone needs `fetch` and `build` before the Unity viewer has data, unless a prebuilt pack is committed later.
- The repository stays small. The Unity Library folder (about 2 GB) and the kernels never enter git.
- Kernel reading exists twice: in the pipeline (through SpiceyPy/CSPICE, to extract segments) and in the core (evaluating the extracted Chebyshev records from the pack). The core does not need a general DAF reader.
- The pack format must be fully specified, because two languages implement it. This also serves future non-C++ ports.
- Publisher outages or file removals could block a fresh build. The lock file's checksums make it safe to restore from any mirror or backup copy.
- The UI can show a citation for every value.
- Contributors need Git LFS installed (`git lfs install`) before cloning.

## Alternatives considered

- **Pipeline in C++** (sharing code with the core). HTTPS, JSON and hashing would need third-party libraries, and the shared code would be small (the core reads packs, not raw kernels). Rejected.
- **Commit kernels to git, even through LFS.** `de440s.bsp` alone is 31 MB, and satellite kernels run to hundreds of MB. They are reproducible from the lock file, so they are not committed.
- **Read raw kernels directly at runtime (no pack).** It ships hundreds of MB of unneeded coverage, spreads provenance across many formats, and makes the C++ core implement DAF and text kernel parsing. Rejected.
- **JSON data pack.** Large for Chebyshev coefficients, and it would need a JSON parser in the core. Rejected for the runtime format. JSON stays the format for human-edited pipeline inputs.
