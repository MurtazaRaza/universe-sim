"""The info command: print what a pack contains (bodies, coverage, sources, sizes)."""

from __future__ import annotations

from collections.abc import Callable
from pathlib import Path

from .pack.binary import read_pack
from .pack.model import Field, PackContent, Unit
from .tdb import format_tdb

_UNIT_TEXT = {Unit.NONE: "", Unit.KM: "km", Unit.S: "s", Unit.KM_PER_S: "km/s", Unit.RAD: "rad",
              Unit.RAD_PER_S: "rad/s", Unit.KM3_PER_S2: "km^3/s^2"}  # fmt: skip


def _id(ref: tuple[int, int]) -> str:
    return f"naif:{ref[1]}" if ref[0] == 1 else f"{ref[0]}:{ref[1]}"


def describe(path: Path, log: Callable[[str], None] = print) -> PackContent:
    data = path.read_bytes()
    header, content = read_pack(data)
    names = {b.id: b.name for b in content.bodies}

    log(f"{path} ({header.file_size} bytes)")
    log(f"  format {header.format_major}.{header.format_minor}, content sha256 {header.content_sha256}")
    log(f"  lock sha256 {header.lock_sha256}")
    for key in sorted(content.metadata):
        log(f"  {key}: {content.metadata[key]}")

    log(f"\nsources ({len(content.sources)})")
    for s in content.sources:
        log(f"  {s.id} (tier {s.tier}): {s.title}")
    log(f"\nfiles ({len(content.files)})")
    for f in content.files:
        check = "publisher MD5 verified" if f.publisher_checksum_verified else "no publisher checksum"
        log(f"  {f.name}: {f.size} bytes, sha256 {f.sha256}, retrieved {f.retrieved_utc} ({check})")

    by_body: dict[tuple[int, int], list] = {}
    for q in content.quantities:
        by_body.setdefault(q.body, []).append(q)
    rotations = {r.body for r in content.rotations}
    log(f"\nbodies ({len(content.bodies)})")
    for b in content.bodies:
        primary = f", primary {names.get(b.primary, _id(b.primary))}" if b.primary else ""
        rotation = ", IAU rotation model" if b.id in rotations else ""
        log(f"  {_id(b.id):<10} {b.name} ({b.kind.name.lower()}{primary}{rotation})")
        for q in sorted(by_body.get(b.id, []), key=lambda q: q.field):
            derived = " (derived)" if q.derivation else ""
            log(f"      {Field(q.field).name.lower():<12} {q.value!r} {_UNIT_TEXT[q.unit]}  [{q.source_id}: {q.locator}]{derived}")

    t = content.time
    if t is not None:
        last = t.leap_seconds[-1]
        log(f"\ntime ({t.source_id}): {len(t.leap_seconds)} leap second entries, last TAI - UTC = {last[3]} s "
            f"from {last[0]:04d}-{last[1]:02d}-{last[2]:02d}; TDB - TT model k = {t.k_s!r} s")  # fmt: skip

    log(f"\nephemeris segments ({len(content.segments)}), coverage in TDB")
    total = 0
    for s in content.segments:
        size = s.records.size * 8
        total += size
        degree = (s.record_size - 2) // (3 if s.spk_type == 2 else 6) - 1
        log(
            f"  {names.get(s.target, _id(s.target))} wrt {names.get(s.center, _id(s.center))}: SPK type {s.spk_type}, "
            f"degree {degree}, {s.record_count} records of {s.interval_s / 86400:g} d, "
            f"{format_tdb(s.start_s)} to {format_tdb(s.end_s)}, {size} bytes"
        )
    log(f"  coefficient data: {total} bytes")
    approximated = {a.body for a in content.approximations}
    uncovered = [
        b.name
        for b in content.bodies
        if b.id not in approximated and all(b.id not in (s.target, s.center) for s in content.segments)
    ]
    if uncovered:
        log(f"  bodies without ephemeris: {', '.join(uncovered)}")
    if content.approximations:
        log(f"\napproximate positions ({len(content.approximations)}), every state flagged approximate")
        for a in content.approximations:
            log(f"  {names.get(a.body, _id(a.body))} at {names.get(a.proxy, _id(a.proxy))}: {a.reason}")
    stars = content.star_catalog
    if stars is not None:
        brightest = sorted(stars.stars, key=lambda s: s.v_mag)[:3]
        log(
            f"\nstars ({len(stars.stars)}, {stars.source_id}): V {min(s.v_mag for s in stars.stars)!r} to "
            f"{max(s.v_mag for s in stars.stars)!r}, {sum(1 for s in stars.stars if s.b_v is not None)} with B-V; "
            f"brightest {', '.join(s.designation or str(s.id[1]) for s in brightest)}"
        )
        log(f"  {stars.locator}")
    if content.rings:
        log(f"\nrings ({len(content.rings)})")
        for r in content.rings:
            depth = f", optical depth {r.optical_depth[0]!r} to {r.optical_depth[1]!r} [{r.optical_depth_source_id}]" if r.optical_depth else ""
            kind = "gap" if r.gap else "ring"
            log(f"  {names.get(r.body, _id(r.body))} {r.name} ({kind}): {r.inner_km!r} to {r.outer_km!r} km [{r.radii_source_id}]{depth}")
    return content
