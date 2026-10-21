# Data Pack Format, Version 1

| | |
|---|---|
| Status | Format 1.0, implemented |
| Last updated | 2026-10-21 |
| Writer | `tools/pipeline/usim_pipeline/pack/binary.py` (Python) |
| Readers | `core/src/data/data_pack.cpp` (C++, `usim::data::DataPack`); `binary.py` (Python, for `usim-pipeline info`) |
| Related | [ARCHITECTURE.md section 3.7](../ARCHITECTURE.md#37-data-pack-format-v1), [ADR-0005](../adr/0005-data-provenance-and-storage.md), [ADR-0006](../adr/0006-default-pack-window-and-storage.md), [ADR-0007](../adr/0007-text-kernel-values.md) |

A data pack (`.uspack`) is the only data the core reads at runtime. The pipeline writes it from verified sources; the core reads it without parsing any other format. Two independent implementations follow this document, and the round-trip fixture (`core/tests/fixtures/test_pack_v1.uspack`) keeps them consistent. Change this document first, then both implementations.

## 1. Conventions

- All integers are little-endian. `u32` is unsigned 32-bit, `i64` signed 64-bit, and so on.
- `f64` is an IEEE 754 binary64 value, little-endian. Every stored `f64` is finite.
- `str` is a string reference of 8 bytes: `u32 offset, u32 length` into section `STRS`. Strings are UTF-8 and not NUL-terminated. The empty string is (0, 0).
- `fourcc` is four ASCII bytes, for example `BODY`.
- Offsets are in bytes from the start of the file unless stated otherwise.
- Times are TDB seconds past J2000 (the same number SPICE calls ET). Lengths are km, velocities km/s, GM km^3/s^2.
- Codes:

| Code | Values |
|---|---|
| Id namespace (`usim::catalog::IdNamespace`) | 1 naif, 2 gaia-dr3, 3 hip. 0 means "none" where a field allows it. |
| Body kind (`usim::catalog::BodyKind`) | 0 barycenter, 1 star, 2 planet, 3 dwarf planet, 4 moon, 5 asteroid, 6 comet, 7 spacecraft, 8 star system, 9 galaxy |
| Unit (`usim::catalog::Unit`) | 0 none, 1 km, 2 s, 3 km/s, 4 rad, 5 rad/s, 6 km^3/s^2 |
| Field | 1 gm (unit 6), 2 mean_radius (1), 3 radius_a (1), 4 radius_b (1), 5 radius_c (1) |
| Frame (`usim::frames::FrameKind`) | 0 ICRF, 1 ECLIPJ2000 |

## 2. File layout

```
offset 0      header (128 bytes)
offset 128    section table: section_count entries of 32 bytes
...           sections, each starting at a multiple of 8, zero bytes between them
```

### 2.1 Header

| Offset | Type | Field | Value |
|---|---|---|---|
| 0 | u8[8] | magic | `USIMPACK` |
| 8 | u16 | format_major | 1 |
| 10 | u16 | format_minor | 0 |
| 12 | u32 | endian_marker | 0x01020304 (bytes 04 03 02 01) |
| 16 | u32 | header_size | 128 |
| 20 | u32 | section_count | |
| 24 | u64 | file_size | size of the whole file |
| 32 | u8[32] | content_sha256 | SHA-256 of bytes [header_size, file_size) |
| 64 | u8[32] | lock_sha256 | SHA-256 of the lock file in canonical form (section 5) |
| 96 | u8[32] | reserved | zero |

### 2.2 Section table entry

| Offset | Type | Field |
|---|---|---|
| 0 | fourcc | id |
| 4 | u32 | section version (1 for every section of format 1.x) |
| 8 | u64 | offset, a multiple of 8, after the section table |
| 16 | u64 | size in bytes, without padding |
| 24 | u64 | reserved, zero |

Sections must lie inside the file and must not overlap. Ids are unique.

### 2.3 Versions

- A reader accepts every `format_minor` of its `format_major`. It rejects another major version with `incompatible_version`.
- Minor versions only add sections. A reader skips sections it does not know. Changing the layout or codes of an existing section requires a new major version.
- A reader rejects a known section whose section version it does not support (`incompatible_version`).

## 3. Sections

All ten sections are required in format 1.0. The writer stores them in the order META, SRCS, FILE, BODY, QNTY, ROTN, TIME, EPHS, STRS, EPHD; readers use the table and do not depend on the order. Most sections start with an 8-byte count header (`u32 count, u32 second`), followed by `count` fixed-size records.

### META: metadata

Header `u32 count, u32 reserved`, then `count` records of 16 bytes: `str key, str value`. Keys are unique and sorted by bytes. The pipeline writes:

| Key | Meaning |
|---|---|
| `pack.name`, `pack.version`, `pack.description` | From `data/catalog/packs.json` |
| `pack.window` | The time window the ephemeris was trimmed to, or "full coverage of the ephemeris files" |
| `pipeline.version`, `pipeline.tools` | Pipeline version, and the CSPICE, SpiceyPy and numpy versions used |

No metadata records the build time: equal inputs give byte-identical packs (P2-08).

### STRS: string table

Raw UTF-8 bytes referenced by `str` fields. The writer deduplicates strings.

### SRCS: sources

Header `u32 count, u32 reserved`, then records of 56 bytes, sorted by id. Each is an entry of `data/sources.json` used by the pack.

| Offset | Type | Field |
|---|---|---|
| 0 | str | id (for example `S-NAIF-PCK11`) |
| 8 | str | title |
| 16 | str | provider |
| 24 | str | url |
| 32 | str | citation |
| 40 | str | license or terms of use |
| 48 | i32 | tier (1 or 2) |
| 52 | u32 | reserved |

Other sections refer to a source by its index in this table.

### FILE: source files

Header `u32 count, u32 reserved`, then records of 88 bytes: the lock file entries of the files the pack was built from.

| Offset | Type | Field |
|---|---|---|
| 0 | u32 | source index |
| 4 | u32 | flags: bit 0 set if the MD5 matched a checksum list published by the provider |
| 8 | str | file name |
| 16 | str | url |
| 24 | str | retrieval time, ISO 8601 UTC (`2026-10-20T15:09:49Z`) |
| 32 | u64 | file size |
| 40 | u8[32] | SHA-256 |
| 72 | u8[16] | MD5 |

### BODY: bodies

Header `u32 count, u32 reserved`, then records of 40 bytes, in catalog display order. Ids are unique; a primary must be a body of the pack, and primary links must not form a cycle.

| Offset | Type | Field |
|---|---|---|
| 0 | i32 | id namespace |
| 4 | i32 | kind |
| 8 | i64 | id value (the NAIF ID for namespace 1) |
| 16 | i32 | primary namespace, 0 for none |
| 20 | u32 | reserved |
| 24 | i64 | primary id value (0 for none) |
| 32 | str | name |

### QNTY: quantities

Header `u32 count, u32 reserved`, then records of 56 bytes, sorted by (body index, field). At most one record per (body, field). A field without a record is missing: the source does not provide it (SPEC FR-DATA-05).

| Offset | Type | Field |
|---|---|---|
| 0 | u32 | body index (into BODY) |
| 4 | u32 | field code |
| 8 | u32 | unit code; must be the field's unit |
| 12 | u32 | flags: bit 0 set if an uncertainty is present |
| 16 | f64 | value |
| 24 | f64 | one-sigma uncertainty, >= 0 (0 when absent) |
| 32 | u32 | source index |
| 36 | u32 | reserved |
| 40 | str | locator: file and variable, for example `pck00011.tpc BODY399_RADII[0]` |
| 48 | str | derivation: formula and inputs of a derived value, empty otherwise |

### ROTN: IAU rotation elements

Header `u32 count, u32 pool_count`, then `count` records of 88 bytes, then `pool_count` f64 values (the pool). Arrays in a record are `u32 first, u32 length` ranges of the pool. Values are kept in the units of the SPICE text PCK; the model is documented in `usim/frames/iau_rotation.hpp` (P4-03 evaluates it).

| Offset | Type | Field |
|---|---|---|
| 0 | u32 | body index |
| 4 | i32 | frame of the pole coordinates (0, the ICRF) |
| 8 | u32 | source index |
| 12 | u32 | phase_degree: degree of the phase angle polynomials, 1 to 16 (BODY*n*_MAX_PHASE_DEGREE, default 1) |
| 16 | i64 | NAIF ID of the system whose phase angles apply (BODY*n*_NUT_PREC_ANGLES); 0 when the angle array is empty |
| 24 | array | pole_ra: 1 to 3 values; deg, deg/century, deg/century^2 |
| 32 | array | pole_dec: 1 to 3 values; same units |
| 40 | array | pm (prime meridian): 1 to 3 values; deg, deg/day, deg/day^2 |
| 48 | array | nut_prec_ra: sine coefficients, deg |
| 56 | array | nut_prec_dec: cosine coefficients, deg |
| 64 | array | nut_prec_pm: sine coefficients, deg |
| 72 | array | nut_prec_angles: phase_degree + 1 values per angle; deg, deg/century, ... |
| 80 | str | locator: file and variables |

At most one record per body. The nutation arrays may be shorter than the number of angles (missing terms are zero), never longer.

### TIME: time scale data

| Offset | Type | Field |
|---|---|---|
| 0 | u32 | leap second entry count |
| 4 | u32 | source index |
| 8 | f64 | TT - TAI, s (DELTET/DELTA_T_A). The core requires the definitional 32.184 exactly. |
| 16 | f64 | k, s (DELTET/K) |
| 24 | f64 | eb (DELTET/EB) |
| 32 | f64 | m0, rad (DELTET/M, first value) |
| 40 | f64 | m1, rad/s (DELTET/M, second value) |
| 48 | str | locator |
| 56 | | leap second entries, 16 bytes each: `i32 year, i32 month, i32 day, i32 tai_minus_utc_s`: from 00:00 UTC of that Gregorian date, TAI - UTC is the given number of seconds. Dates strictly increase. |

The TDB - TT model is the one SPICE uses (`usim::time::TdbModel`, SPEC section 7.2).

### EPHS: ephemeris segments

Header `u32 count, u32 reserved`, then records of 96 bytes. Segments are in precedence order: where two segments of the same target cover an instant, the later one is used, as in SPICE.

| Offset | Type | Field |
|---|---|---|
| 0 | i32 | target namespace |
| 4 | i32 | center namespace |
| 8 | i64 | target id value |
| 16 | i64 | center id value |
| 24 | i32 | frame (0, the ICRF) |
| 28 | i32 | SPK data type: 2 (Chebyshev position) or 3 (Chebyshev position and velocity) |
| 32 | f64 | coverage start, TDB s (inclusive) |
| 40 | f64 | coverage end, TDB s (inclusive) |
| 48 | f64 | init: start of the first record, TDB s |
| 56 | f64 | interval: length of every record, s (> 0) |
| 64 | u32 | record_size: f64 values per record |
| 68 | u32 | record_count (> 0) |
| 72 | u64 | first: index of the segment's first value in EPHD |
| 80 | u32 | source index |
| 84 | u32 | reserved |
| 88 | str | locator: SPK file, segment index (0-based, DAF order) and segment identifier |

Constraints: target and center are bodies of the pack; `init <= start < end <= init + record_count * interval`; `record_size = 2 + n (degree + 1)` with n = 3 for type 2 and 6 for type 3.

Record i covers [init + i interval, init + (i + 1) interval] and is laid out as in the SPK file (NAIF SPK Required Reading):

```
MID, RADIUS                       TDB s; the Chebyshev argument is (t - MID) / RADIUS
X coefficients   (degree + 1)     km
Y coefficients   (degree + 1)     km
Z coefficients   (degree + 1)     km
VX, VY, VZ coefficients           km/s, type 3 only
```

The records are copied bit for bit from the SPK file. When the pack is trimmed to a time window, a segment keeps the records that overlap the window, and its coverage is the span of the kept records (clipped to the original coverage). It can therefore extend slightly beyond the window.

### EPHD: ephemeris data

The f64 values of all segments' records, concatenated. Its size is a multiple of 8.

## 4. Validation

A reader rejects (`data_format` unless noted):

- a Git LFS pointer instead of a pack (the message says to run `git lfs pull`), a short file, bad magic or endianness marker;
- another major version (`incompatible_version`);
- a header or file size that does not match, or a content hash mismatch;
- a section out of bounds, misaligned, overlapping or duplicated, or a missing required section;
- a string reference out of bounds or invalid UTF-8; an index out of range; an unknown code;
- a quantity with the wrong unit for its field, a duplicate (body, field), or a negative or non-finite uncertainty;
- a rotation record that breaks the ROTN constraints;
- TT - TAI other than 32.184 s, or an invalid leap second table;
- a segment that breaks the EPHS constraints, or a non-finite value;
- a catalog that `BodyCatalog::create` rejects (duplicates, missing primaries, cycles, unregistered sources).

The pipeline's writer applies the same checks before writing (`check_content`).

## 5. Provenance hashes

- `content_sha256` covers everything after the header, so any change to the content changes it.
- `lock_sha256` identifies the exact source files: it is the SHA-256 of `data/sources.lock.json` in canonical JSON form (keys sorted, no whitespace, UTF-8, as produced by Python `json.dumps(obj, sort_keys=True, separators=(",", ":"), ensure_ascii=False)`). Line endings and indentation of the file do not affect it.

## 6. Size

The default pack (`solar-system-1.0.0.uspack`, de440s.bsp untrimmed) is 32,681,168 bytes; the ephemeris records are almost all of it. Trimming to a window shrinks it roughly in proportion to the window's length.
