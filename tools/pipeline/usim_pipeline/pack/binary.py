"""Byte-level writer and reader of data pack format v1. The layout is specified in docs/formats/DATA_PACK.md;
the C++ reader (core/src/data/data_pack.cpp) implements the same specification independently.
"""

from __future__ import annotations

import dataclasses
import hashlib
import math
import struct
from dataclasses import dataclass

import numpy

from .. import PipelineError
from .model import (
    BodyKind,
    PackApproximation,
    Field,
    Frame,
    PackBody,
    PackContent,
    PackFile,
    PackQuantity,
    PackRotation,
    PackRing,
    PackSegment,
    PackSource,
    PackStar,
    PackStarCatalog,
    PackTime,
    Unit,
    check_content,
)

MAGIC = b"USIMPACK"
FORMAT_MAJOR = 1
FORMAT_MINOR = 2
ENDIAN_MARKER = 0x01020304
SECTION_VERSION = 1
ALIGNMENT = 8

HEADER = struct.Struct("<8sHHIIIQ32s32s32s")  # 128 bytes
SECTION = struct.Struct("<4sIQQQ")  # 32 bytes
COUNTS = struct.Struct("<II")
STRREF = struct.Struct("<II")
META_ENTRY = struct.Struct("<IIII")  # 16
SOURCE = struct.Struct("<IIIIIIIIIIIIiI")  # 56
FILE = struct.Struct("<IIIIIIIIQ32s16s")  # 88
BODY = struct.Struct("<iiqiIqII")  # 40
QUANTITY = struct.Struct("<IIIIddIIIIII")  # 56
ROTATION = struct.Struct("<IiIIqIIIIIIIIIIIIIIII")  # 88
TIME = struct.Struct("<IIdddddII")  # 56
LEAP = struct.Struct("<iiii")  # 16
SEGMENT = struct.Struct("<iiqqiiddddIIQIIII")  # 96
APPROXIMATION = struct.Struct("<iiqqII")  # 32
STAR_HEADER = struct.Struct("<IIdIIII")  # 32
STAR = struct.Struct("<iIqddddddII")  # 72
RING = struct.Struct("<IIddddIIIIIIII")  # 72

# Sections in the order they are written. The format 1.0 sections are required; APPX (1.1) is optional and
# always written; STAR and RING (1.2) are optional and written only when the pack has stars or rings.
SECTION_ORDER = (
    b"META", b"SRCS", b"FILE", b"BODY", b"QNTY", b"ROTN", b"TIME", b"EPHS", b"APPX", b"STAR", b"RING", b"STRS", b"EPHD",
)  # fmt: skip
OPTIONAL_SECTIONS = (b"APPX", b"STAR", b"RING")
REQUIRED_SECTIONS = tuple(s for s in SECTION_ORDER if s not in OPTIONAL_SECTIONS)

QUANTITY_HAS_UNCERTAINTY = 1
FILE_PUBLISHER_VERIFIED = 1
STAR_HAS_COLOR = 1
STAR_HAS_PROPER_MOTION = 2
RING_GAP = 1
RING_HAS_OPTICAL_DEPTH = 2

assert HEADER.size == 128 and SECTION.size == 32 and SOURCE.size == 56 and FILE.size == 88
assert BODY.size == 40 and QUANTITY.size == 56 and ROTATION.size == 88 and TIME.size == 56 and SEGMENT.size == 96
assert APPROXIMATION.size == 32 and STAR_HEADER.size == 32 and STAR.size == 72 and RING.size == 72


class _Strings:
    """The STRS section: UTF-8 strings concatenated, each referenced by (offset, length). Deduplicated."""

    def __init__(self) -> None:
        self._data = bytearray()
        self._refs: dict[str, tuple[int, int]] = {}

    def ref(self, text: str) -> tuple[int, int]:
        if text not in self._refs:
            encoded = text.encode("utf-8")
            self._refs[text] = (len(self._data), len(encoded))
            self._data += encoded
        return self._refs[text]

    def data(self) -> bytes:
        return bytes(self._data)


def _pad(data: bytes | bytearray) -> bytes:
    return bytes(data) + b"\0" * (-len(data) % ALIGNMENT)


def _f64(values: tuple[float, ...] | list[float]) -> bytes:
    return struct.pack(f"<{len(values)}d", *values)


def canonical(content: PackContent) -> PackContent:
    """`content` in the order the writer stores it (quantities by body, then field), which is the order the
    reader returns. Two contents with equal canonical forms give identical packs."""
    body_index = {b.id: i for i, b in enumerate(content.bodies)}
    quantities = sorted(content.quantities, key=lambda q: (body_index[q.body], int(q.field)))
    approximations = sorted(content.approximations, key=lambda a: body_index[a.body])
    catalog = content.star_catalog
    if catalog is not None:
        catalog = dataclasses.replace(catalog, stars=tuple(sorted(catalog.stars, key=lambda s: s.id)))
    rings = sorted(content.rings, key=lambda r: (body_index[r.body], r.inner_km))
    return dataclasses.replace(
        content, quantities=quantities, approximations=approximations, metadata=dict(sorted(content.metadata.items())),
        star_catalog=catalog, rings=rings,
    )  # fmt: skip


def write_pack(content: PackContent) -> bytes:
    """Serializes `content`. The output depends only on the content, so equal content gives equal bytes."""
    check_content(content)
    strings = _Strings()
    source_index = {s.id: i for i, s in enumerate(content.sources)}
    body_index = {b.id: i for i, b in enumerate(content.bodies)}
    sections: dict[bytes, bytes] = {}

    meta = bytearray(COUNTS.pack(len(content.metadata), 0))
    for key in sorted(content.metadata):
        meta += META_ENTRY.pack(*strings.ref(key), *strings.ref(content.metadata[key]))
    sections[b"META"] = bytes(meta)

    srcs = bytearray(COUNTS.pack(len(content.sources), 0))
    for s in content.sources:
        refs = [x for text in (s.id, s.title, s.provider, s.url, s.citation, s.license) for x in strings.ref(text)]
        srcs += SOURCE.pack(*refs, s.tier, 0)
    sections[b"SRCS"] = bytes(srcs)

    files = bytearray(COUNTS.pack(len(content.files), 0))
    for f in content.files:
        flags = FILE_PUBLISHER_VERIFIED if f.publisher_checksum_verified else 0
        files += FILE.pack(
            source_index[f.source_id], flags, *strings.ref(f.name), *strings.ref(f.url), *strings.ref(f.retrieved_utc),
            f.size, bytes.fromhex(f.sha256), bytes.fromhex(f.md5),
        )  # fmt: skip
    sections[b"FILE"] = bytes(files)

    bodies = bytearray(COUNTS.pack(len(content.bodies), 0))
    for b in content.bodies:
        primary = b.primary if b.primary is not None else (0, 0)
        bodies += BODY.pack(b.id[0], int(b.kind), b.id[1], primary[0], 0, primary[1], *strings.ref(b.name))
    sections[b"BODY"] = bytes(bodies)

    quantities = sorted(content.quantities, key=lambda q: (body_index[q.body], int(q.field)))
    qnty = bytearray(COUNTS.pack(len(quantities), 0))
    for q in quantities:
        flags = QUANTITY_HAS_UNCERTAINTY if q.uncertainty is not None else 0
        qnty += QUANTITY.pack(
            body_index[q.body], int(q.field), int(q.unit), flags, q.value, q.uncertainty or 0.0,
            source_index[q.source_id], 0, *strings.ref(q.locator), *strings.ref(q.derivation),
        )  # fmt: skip
    sections[b"QNTY"] = bytes(qnty)

    pool: list[float] = []

    def array(values: tuple[float, ...]) -> tuple[int, int]:
        first = len(pool)
        pool.extend(values)
        return (first, len(values))

    records = bytearray()
    for r in content.rotations:
        arrays = [
            x
            for values in (r.pole_ra, r.pole_dec, r.pm, r.nut_prec_ra, r.nut_prec_dec, r.nut_prec_pm, r.nut_prec_angles)
            for x in array(values)
        ]
        records += ROTATION.pack(
            body_index[r.body], int(r.frame), source_index[r.source_id], r.phase_degree, r.angles_body,
            *arrays, *strings.ref(r.locator),
        )  # fmt: skip
    sections[b"ROTN"] = COUNTS.pack(len(content.rotations), len(pool)) + bytes(records) + _f64(pool)

    t = content.time
    assert t is not None
    time = bytearray(
        TIME.pack(len(t.leap_seconds), source_index[t.source_id], t.tt_minus_tai_s, t.k_s, t.eb, t.m0_rad,
                  t.m1_rad_per_s, *strings.ref(t.locator))
    )  # fmt: skip
    for row in t.leap_seconds:
        time += LEAP.pack(*row)
    sections[b"TIME"] = bytes(time)

    ephs = bytearray(COUNTS.pack(len(content.segments), 0))
    data_parts: list[bytes] = []
    first = 0
    for s in content.segments:
        ephs += SEGMENT.pack(
            s.target[0], s.center[0], s.target[1], s.center[1], int(s.frame), s.spk_type,
            s.start_s, s.end_s, s.init_s, s.interval_s, s.record_size, s.record_count, first,
            source_index[s.source_id], 0, *strings.ref(s.locator),
        )  # fmt: skip
        data_parts.append(numpy.ascontiguousarray(s.records, dtype="<f8").tobytes())
        first += s.records.size
    sections[b"EPHS"] = bytes(ephs)
    sections[b"EPHD"] = b"".join(data_parts)

    approximations = sorted(content.approximations, key=lambda a: body_index[a.body])
    appx = bytearray(COUNTS.pack(len(approximations), 0))
    for a in approximations:
        appx += APPROXIMATION.pack(a.body[0], a.proxy[0], a.body[1], a.proxy[1], *strings.ref(a.reason))
    sections[b"APPX"] = bytes(appx)

    catalog = content.star_catalog
    if catalog is not None:
        star = bytearray(
            STAR_HEADER.pack(len(catalog.stars), source_index[catalog.source_id], catalog.epoch_tdb_s,
                             *strings.ref(catalog.locator), *strings.ref(catalog.derivation))
        )  # fmt: skip
        for s in sorted(catalog.stars, key=lambda s: s.id):
            flags = (STAR_HAS_COLOR if s.b_v is not None else 0) | (
                STAR_HAS_PROPER_MOTION if s.pm_ra_cosdec_rad_per_s is not None else 0
            )
            star += STAR.pack(
                s.id[0], flags, s.id[1], s.ra_rad, s.dec_rad, s.pm_ra_cosdec_rad_per_s or 0.0,
                s.pm_dec_rad_per_s or 0.0, s.v_mag, s.b_v or 0.0, *strings.ref(s.designation),
            )  # fmt: skip
        sections[b"STAR"] = bytes(star)

    if content.rings:
        ring_data = bytearray(COUNTS.pack(len(content.rings), 0))
        for r in sorted(content.rings, key=lambda r: (body_index[r.body], r.inner_km)):
            flags = (RING_GAP if r.gap else 0) | (RING_HAS_OPTICAL_DEPTH if r.optical_depth is not None else 0)
            low, high = r.optical_depth or (0.0, 0.0)
            depth_source = source_index[r.optical_depth_source_id] if r.optical_depth_source_id is not None else 0
            ring_data += RING.pack(
                body_index[r.body], flags, r.inner_km, r.outer_km, low, high, source_index[r.radii_source_id],
                depth_source, *strings.ref(r.name), *strings.ref(r.radii_locator), *strings.ref(r.optical_depth_locator),
            )  # fmt: skip
        sections[b"RING"] = bytes(ring_data)
    sections[b"STRS"] = strings.data()

    written = [s for s in SECTION_ORDER if s in sections]
    offset = HEADER.size + SECTION.size * len(written)
    table = bytearray()
    body = bytearray()
    for section_id in written:
        payload = sections[section_id]
        table += SECTION.pack(section_id, SECTION_VERSION, offset, len(payload), 0)
        padded = _pad(payload)
        body += padded
        offset += len(padded)
    after_header = bytes(table) + bytes(body)
    header = HEADER.pack(
        MAGIC, FORMAT_MAJOR, FORMAT_MINOR, ENDIAN_MARKER, HEADER.size, len(written),
        HEADER.size + len(after_header), hashlib.sha256(after_header).digest(), bytes.fromhex(content.lock_sha256),
        bytes(32),
    )  # fmt: skip
    return header + after_header


@dataclass(frozen=True)
class PackHeader:
    format_major: int
    format_minor: int
    file_size: int
    content_sha256: str
    lock_sha256: str
    sections: dict[str, tuple[int, int]]  # id -> (offset, size)


class _View:
    """Bounds-checked access to one section."""

    def __init__(self, name: str, data: memoryview) -> None:
        self.name = name
        self.data = data

    def unpack(self, layout: struct.Struct, offset: int) -> tuple:
        if offset < 0 or offset + layout.size > len(self.data):
            raise PipelineError(f"pack: section {self.name} is truncated")
        return layout.unpack_from(self.data, offset)

    def records(self, layout: struct.Struct, start: int, count: int) -> list[tuple]:
        return [self.unpack(layout, start + i * layout.size) for i in range(count)]


def read_header(data: bytes) -> PackHeader:
    """Checks the header, the section table and the content hash. Fails with PipelineError."""
    if data.startswith(b"version https://git-lfs"):
        raise PipelineError("pack: this is a Git LFS pointer, not the pack; run git lfs pull")
    if len(data) < HEADER.size:
        raise PipelineError("pack: file is shorter than the header")
    magic, major, minor, marker, header_size, count, size, content_hash, lock_hash, _ = HEADER.unpack_from(data)
    if magic != MAGIC:
        raise PipelineError("pack: not a data pack (bad magic)")
    if marker != ENDIAN_MARKER:
        raise PipelineError("pack: bad endianness marker")
    if major != FORMAT_MAJOR:
        raise PipelineError(f"pack: format {major}.{minor} is not supported (this reader reads {FORMAT_MAJOR}.x)")
    if header_size < HEADER.size or size != len(data):
        raise PipelineError("pack: header size or file size does not match")
    if hashlib.sha256(data[header_size:]).digest() != content_hash:
        raise PipelineError("pack: content hash mismatch (the file is corrupt or was modified)")
    table_end = header_size + count * SECTION.size
    if table_end > len(data):
        raise PipelineError("pack: section table is truncated")
    sections: dict[str, tuple[int, int]] = {}
    spans: list[tuple[int, int]] = []
    for i in range(count):
        raw_id, version, offset, length, _ = SECTION.unpack_from(data, header_size + i * SECTION.size)
        name = raw_id.decode("ascii", errors="replace")
        if name in sections:
            raise PipelineError(f"pack: section {name} appears twice")
        if offset % ALIGNMENT or offset < table_end or offset + length > len(data):
            raise PipelineError(f"pack: section {name} is misaligned or out of bounds")
        if raw_id in SECTION_ORDER and version != SECTION_VERSION:
            raise PipelineError(f"pack: section {name} has version {version}, expected {SECTION_VERSION}")
        sections[name] = (offset, length)
        spans.append((offset, offset + length))
    spans.sort()
    if any(a[1] > b[0] for a, b in zip(spans, spans[1:])):
        raise PipelineError("pack: sections overlap")
    missing = [s.decode() for s in REQUIRED_SECTIONS if s.decode() not in sections]
    if missing:
        raise PipelineError(f"pack: missing sections {missing}")
    return PackHeader(major, minor, size, content_hash.hex(), lock_hash.hex(), sections)


def _frame(code: int) -> Frame:
    try:
        return Frame(code)
    except ValueError:
        raise PipelineError(f"pack: unknown frame code {code}") from None


def read_pack(data: bytes) -> tuple[PackHeader, PackContent]:
    """Parses and validates a pack. Fails with PipelineError on any inconsistency."""
    header = read_header(data)
    whole = memoryview(data)

    def view(name: str) -> _View:
        offset, length = header.sections[name]
        return _View(name, whole[offset : offset + length])

    strs = view("STRS")

    def text(offset: int, length: int) -> str:
        if offset + length > len(strs.data):
            raise PipelineError("pack: string reference out of bounds")
        try:
            return bytes(strs.data[offset : offset + length]).decode("utf-8")
        except UnicodeDecodeError:
            raise PipelineError("pack: string is not valid UTF-8") from None

    def strings(values: tuple, *positions: int) -> list[str]:
        return [text(values[p], values[p + 1]) for p in positions]

    meta = view("META")
    count, _ = meta.unpack(COUNTS, 0)
    metadata: dict[str, str] = {}
    for entry in meta.records(META_ENTRY, COUNTS.size, count):
        key, value = strings(entry, 0, 2)
        metadata[key] = value

    srcs = view("SRCS")
    count, _ = srcs.unpack(COUNTS, 0)
    sources = []
    for r in srcs.records(SOURCE, COUNTS.size, count):
        sid, title, provider, url, citation, license_ = strings(r, 0, 2, 4, 6, 8, 10)
        sources.append(PackSource(sid, title, provider, url, citation, license_, r[12]))

    def source_id(index: int) -> str:
        if index >= len(sources):
            raise PipelineError("pack: source index out of range")
        return sources[index].id

    fil = view("FILE")
    count, _ = fil.unpack(COUNTS, 0)
    files = []
    for r in fil.records(FILE, COUNTS.size, count):
        name, url, retrieved = strings(r, 2, 4, 6)
        files.append(
            PackFile(source_id(r[0]), name, url, retrieved, r[8], r[9].hex(), r[10].hex(), bool(r[1] & FILE_PUBLISHER_VERIFIED))
        )

    bod = view("BODY")
    count, _ = bod.unpack(COUNTS, 0)
    bodies = []
    for r in bod.records(BODY, COUNTS.size, count):
        primary = None if r[3] == 0 else (r[3], r[5])
        try:
            kind = BodyKind(r[1])
        except ValueError:
            raise PipelineError(f"pack: unknown body kind {r[1]}") from None
        bodies.append(PackBody((r[0], r[2]), kind, text(r[6], r[7]), primary))

    def body_ref(index: int) -> tuple[int, int]:
        if index >= len(bodies):
            raise PipelineError("pack: body index out of range")
        return bodies[index].id

    qty = view("QNTY")
    count, _ = qty.unpack(COUNTS, 0)
    quantities = []
    for r in qty.records(QUANTITY, COUNTS.size, count):
        try:
            field, unit = Field(r[1]), Unit(r[2])
        except ValueError:
            raise PipelineError(f"pack: unknown quantity field {r[1]} or unit {r[2]}") from None
        uncertainty = r[5] if r[3] & QUANTITY_HAS_UNCERTAINTY else None
        locator, derivation = strings(r, 8, 10)
        quantities.append(PackQuantity(body_ref(r[0]), field, unit, r[4], uncertainty, source_id(r[6]), locator, derivation))

    rot = view("ROTN")
    count, pool_count = rot.unpack(COUNTS, 0)
    pool_start = COUNTS.size + count * ROTATION.size
    if pool_start + pool_count * 8 > len(rot.data):
        raise PipelineError("pack: section ROTN is truncated")
    pool = struct.unpack_from(f"<{pool_count}d", rot.data, pool_start)

    def pool_array(first: int, n: int) -> tuple[float, ...]:
        if first + n > pool_count:
            raise PipelineError("pack: rotation coefficient array out of range")
        return tuple(pool[first : first + n])

    rotations = []
    for r in rot.records(ROTATION, COUNTS.size, count):
        arrays = [pool_array(r[i], r[i + 1]) for i in range(5, 19, 2)]
        rotations.append(
            PackRotation(
                body=body_ref(r[0]), frame=_frame(r[1]), source_id=source_id(r[2]), locator=text(r[19], r[20]),
                pole_ra=arrays[0], pole_dec=arrays[1], pm=arrays[2], nut_prec_ra=arrays[3], nut_prec_dec=arrays[4],
                nut_prec_pm=arrays[5], angles_body=r[4], phase_degree=r[3], nut_prec_angles=arrays[6],
            )
        )  # fmt: skip

    tim = view("TIME")
    t = tim.unpack(TIME, 0)
    leaps = tuple(tuple(row) for row in tim.records(LEAP, TIME.size, t[0]))
    time = PackTime(source_id(t[1]), text(t[7], t[8]), t[2], t[3], t[4], t[5], t[6], leaps)  # type: ignore[arg-type]

    ephd = view("EPHD")
    if len(ephd.data) % 8:
        raise PipelineError("pack: section EPHD is not a whole number of doubles")
    coefficients = numpy.frombuffer(ephd.data, dtype="<f8")
    eph = view("EPHS")
    count, _ = eph.unpack(COUNTS, 0)
    segments = []
    for r in eph.records(SEGMENT, COUNTS.size, count):
        size, n, first = r[10], r[11], r[12]
        if size == 0 or first + size * n > coefficients.size:
            raise PipelineError("pack: segment records out of range")
        records = coefficients[first : first + size * n].astype(numpy.float64).reshape(n, size)
        segments.append(
            PackSegment(
                target=(r[0], r[2]), center=(r[1], r[3]), frame=_frame(r[4]), spk_type=r[5], start_s=r[6], end_s=r[7],
                init_s=r[8], interval_s=r[9], records=records, source_id=source_id(r[13]), locator=text(r[15], r[16]),
            )
        )  # fmt: skip

    approximations = []
    if "APPX" in header.sections:
        apx = view("APPX")
        count, _ = apx.unpack(COUNTS, 0)
        for r in apx.records(APPROXIMATION, COUNTS.size, count):
            approximations.append(PackApproximation((r[0], r[2]), (r[1], r[3]), text(r[4], r[5])))

    star_catalog = None
    if "STAR" in header.sections:
        sta = view("STAR")
        h = sta.unpack(STAR_HEADER, 0)
        stars = []
        for r in sta.records(STAR, STAR_HEADER.size, h[0]):
            moving = bool(r[1] & STAR_HAS_PROPER_MOTION)
            stars.append(
                PackStar(
                    id=(r[0], r[2]), ra_rad=r[3], dec_rad=r[4], pm_ra_cosdec_rad_per_s=r[5] if moving else None,
                    pm_dec_rad_per_s=r[6] if moving else None, v_mag=r[7], b_v=r[8] if r[1] & STAR_HAS_COLOR else None,
                    designation=text(r[9], r[10]),
                )
            )  # fmt: skip
        star_catalog = PackStarCatalog(source_id(h[1]), h[2], text(h[3], h[4]), text(h[5], h[6]), tuple(stars))

    rings = []
    if "RING" in header.sections:
        rng = view("RING")
        count, _ = rng.unpack(COUNTS, 0)
        for r in rng.records(RING, COUNTS.size, count):
            has_depth = bool(r[1] & RING_HAS_OPTICAL_DEPTH)
            rings.append(
                PackRing(
                    body=body_ref(r[0]), name=text(r[8], r[9]), gap=bool(r[1] & RING_GAP), inner_km=r[2], outer_km=r[3],
                    radii_source_id=source_id(r[6]), radii_locator=text(r[10], r[11]),
                    optical_depth=(r[4], r[5]) if has_depth else None,
                    optical_depth_source_id=source_id(r[7]) if has_depth else None,
                    optical_depth_locator=text(r[12], r[13]),
                )
            )  # fmt: skip

    content = PackContent(
        metadata=metadata, lock_sha256=header.lock_sha256, sources=sources, files=files, bodies=bodies,
        quantities=quantities, rotations=rotations, time=time, segments=segments, approximations=approximations,
        star_catalog=star_catalog, rings=rings,
    )  # fmt: skip
    check_content(content)
    for q in quantities:
        if not math.isfinite(q.value):
            raise PipelineError("pack: non-finite quantity")
    return header, content
