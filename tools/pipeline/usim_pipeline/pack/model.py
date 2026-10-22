"""In-memory content of a data pack, shared by the writer, the reader and the builder.

Codes (namespaces, kinds, fields, units, frames) are the numeric values of docs/formats/DATA_PACK.md, which
equal the C++ enums they map to (usim::catalog::IdNamespace, BodyKind, Unit and usim::frames::FrameKind).
"""

from __future__ import annotations

import math
from dataclasses import dataclass, field
from enum import IntEnum

import numpy

from .. import PipelineError


class IdNamespace(IntEnum):
    NAIF = 1
    GAIA_DR3 = 2
    HIP = 3
    HR = 4  # Bright Star Catalogue (Harvard Revised) number


class BodyKind(IntEnum):
    BARYCENTER = 0
    STAR = 1
    PLANET = 2
    DWARF_PLANET = 3
    MOON = 4
    ASTEROID = 5
    COMET = 6
    SPACECRAFT = 7
    STAR_SYSTEM = 8
    GALAXY = 9


class Unit(IntEnum):
    NONE = 0
    KM = 1
    S = 2
    KM_PER_S = 3
    RAD = 4
    RAD_PER_S = 5
    KM3_PER_S2 = 6


class Field(IntEnum):
    """Catalog fields a quantity record can fill, with the unit each requires (FIELD_UNITS)."""

    GM = 1
    MEAN_RADIUS = 2
    RADIUS_A = 3
    RADIUS_B = 4
    RADIUS_C = 5


FIELD_UNITS = {
    Field.GM: Unit.KM3_PER_S2,
    Field.MEAN_RADIUS: Unit.KM,
    Field.RADIUS_A: Unit.KM,
    Field.RADIUS_B: Unit.KM,
    Field.RADIUS_C: Unit.KM,
}


class Frame(IntEnum):
    ICRF = 0
    ECLIPJ2000 = 1


BodyRef = tuple[int, int]  # (namespace, value)

MAX_PHASE_DEGREE = 16  # largest phase angle polynomial degree the format allows (DATA_PACK.md, ROTN)


def naif(value: int) -> BodyRef:
    return (int(IdNamespace.NAIF), value)


@dataclass(frozen=True)
class PackSource:
    id: str
    title: str
    provider: str
    url: str
    citation: str
    license: str
    tier: int


@dataclass(frozen=True)
class PackFile:
    """A source file the pack was built from, as pinned in the lock file."""

    source_id: str
    name: str
    url: str
    retrieved_utc: str
    size: int
    sha256: str  # hex
    md5: str  # hex
    publisher_checksum_verified: bool


@dataclass(frozen=True)
class PackBody:
    id: BodyRef
    kind: BodyKind
    name: str
    primary: BodyRef | None


@dataclass(frozen=True)
class PackQuantity:
    body: BodyRef
    field: Field
    unit: Unit
    value: float
    uncertainty: float | None
    source_id: str
    locator: str
    derivation: str = ""


@dataclass(frozen=True)
class PackRotation:
    """IAU rotation elements of one body, in the units of the PCK (DATA_PACK.md, section ROTN)."""

    body: BodyRef
    frame: Frame
    source_id: str
    locator: str
    pole_ra: tuple[float, ...]
    pole_dec: tuple[float, ...]
    pm: tuple[float, ...]
    nut_prec_ra: tuple[float, ...] = ()
    nut_prec_dec: tuple[float, ...] = ()
    nut_prec_pm: tuple[float, ...] = ()
    angles_body: int = 0
    phase_degree: int = 1
    nut_prec_angles: tuple[float, ...] = ()


@dataclass(frozen=True)
class PackTime:
    source_id: str
    locator: str
    tt_minus_tai_s: float
    k_s: float
    eb: float
    m0_rad: float
    m1_rad_per_s: float
    leap_seconds: tuple[tuple[int, int, int, int], ...]  # (year, month, day, TAI - UTC in s)


@dataclass(frozen=True)
class PackApproximation:
    """A body without its own ephemeris, placed at its primary with every state flagged approximate
    (DATA_PACK.md, section APPX; ADR-0008)."""

    body: BodyRef
    proxy: BodyRef  # the body's primary
    reason: str


@dataclass(frozen=True)
class PackStar:
    """One star of the star catalog (DATA_PACK.md, section STAR, format 1.2)."""

    id: BodyRef  # (namespace, catalog number)
    ra_rad: float  # right ascension at the catalog epoch, ICRF, [0, 2 pi)
    dec_rad: float  # declination at the catalog epoch, ICRF
    pm_ra_cosdec_rad_per_s: float | None  # proper motion in RA times cos(dec); None with pm_dec None too
    pm_dec_rad_per_s: float | None
    v_mag: float
    b_v: float | None  # B-V color index, magnitudes
    designation: str = ""


@dataclass(frozen=True)
class PackStarCatalog:
    """The star catalog of a pack: one source, one epoch, and how its columns became the values."""

    source_id: str
    epoch_tdb_s: float
    locator: str
    derivation: str
    stars: tuple[PackStar, ...]


@dataclass(frozen=True)
class PackRing:
    """A ring region, or a gap within one, in a body's equatorial plane (DATA_PACK.md, section RING, 1.2)."""

    body: BodyRef
    name: str
    gap: bool
    inner_km: float
    outer_km: float
    radii_source_id: str
    radii_locator: str  # with the derivation when the radii were computed from a center and a width
    optical_depth: tuple[float, float] | None = None  # (smallest, largest) normal optical depth
    optical_depth_source_id: str | None = None
    optical_depth_locator: str = ""


@dataclass(frozen=True, eq=False)
class PackSegment:
    """Chebyshev records of one SPK type 2 or 3 segment (DATA_PACK.md, sections EPHS and EPHD)."""

    target: BodyRef
    center: BodyRef
    frame: Frame
    spk_type: int
    start_s: float
    end_s: float
    init_s: float
    interval_s: float
    records: numpy.ndarray  # float64, shape (record_count, record_size)
    source_id: str
    locator: str

    @property
    def record_size(self) -> int:
        return int(self.records.shape[1])

    @property
    def record_count(self) -> int:
        return int(self.records.shape[0])

    def __eq__(self, other: object) -> bool:
        if not isinstance(other, PackSegment):
            return NotImplemented
        scalars = ("target", "center", "frame", "spk_type", "start_s", "end_s", "init_s", "interval_s")
        return (
            all(getattr(self, n) == getattr(other, n) for n in scalars)
            and (self.source_id, self.locator) == (other.source_id, other.locator)
            and self.records.shape == other.records.shape
            and self.records.tobytes() == other.records.tobytes()
        )

    __hash__ = None  # type: ignore[assignment]


@dataclass
class PackContent:
    """Everything a pack holds. `metadata` keys are written sorted."""

    metadata: dict[str, str]
    lock_sha256: str  # hex SHA-256 of the canonical lock file
    sources: list[PackSource] = field(default_factory=list)
    files: list[PackFile] = field(default_factory=list)
    bodies: list[PackBody] = field(default_factory=list)
    quantities: list[PackQuantity] = field(default_factory=list)
    rotations: list[PackRotation] = field(default_factory=list)
    time: PackTime | None = None
    segments: list[PackSegment] = field(default_factory=list)
    approximations: list[PackApproximation] = field(default_factory=list)
    star_catalog: PackStarCatalog | None = None
    rings: list[PackRing] = field(default_factory=list)


def check_content(content: PackContent) -> None:
    """Rejects content the format cannot represent or a reader would refuse (DATA_PACK.md, validation)."""
    source_ids = [s.id for s in content.sources]
    if len(set(source_ids)) != len(source_ids):
        raise PipelineError("pack: duplicate source id")
    known_sources = set(source_ids)
    body_ids = [b.id for b in content.bodies]
    if len(set(body_ids)) != len(body_ids):
        raise PipelineError("pack: duplicate body id")
    known_bodies = set(body_ids)
    for body in content.bodies:
        if not body.name:
            raise PipelineError(f"pack: body {body.id} has no name")
        if body.primary is not None and body.primary not in known_bodies:
            raise PipelineError(f"pack: primary {body.primary} of {body.id} is not in the pack")

    def check_source(source_id: str, what: str) -> None:
        if source_id not in known_sources:
            raise PipelineError(f"pack: {what} cites unknown source {source_id}")

    for file in content.files:
        check_source(file.source_id, f"file {file.name}")
    seen_fields: set[tuple[BodyRef, Field]] = set()
    for q in content.quantities:
        what = f"quantity {q.field.name} of {q.body}"
        if q.body not in known_bodies:
            raise PipelineError(f"pack: {what}: body is not in the pack")
        if (q.body, q.field) in seen_fields:
            raise PipelineError(f"pack: {what} appears twice")
        seen_fields.add((q.body, q.field))
        if FIELD_UNITS[q.field] != q.unit:
            raise PipelineError(f"pack: {what} has unit {q.unit.name}, expected {FIELD_UNITS[q.field].name}")
        if not math.isfinite(q.value) or (q.uncertainty is not None and not (math.isfinite(q.uncertainty) and q.uncertainty >= 0)):
            raise PipelineError(f"pack: {what} is not finite or has an invalid uncertainty")
        check_source(q.source_id, what)
    rotation_bodies = [r.body for r in content.rotations]
    if len(set(rotation_bodies)) != len(rotation_bodies):
        raise PipelineError("pack: two rotation models for one body")
    for r in content.rotations:
        what = f"rotation of {r.body}"
        if r.body not in known_bodies:
            raise PipelineError(f"pack: {what}: body is not in the pack")
        check_source(r.source_id, what)
        for name in ("pole_ra", "pole_dec", "pm"):
            if not 1 <= len(getattr(r, name)) <= 3:
                raise PipelineError(f"pack: {what}: {name} needs 1 to 3 coefficients")
        if not 1 <= r.phase_degree <= MAX_PHASE_DEGREE or len(r.nut_prec_angles) % (r.phase_degree + 1) != 0:
            raise PipelineError(f"pack: {what}: phase angle table does not match degree {r.phase_degree}")
        angle_count = len(r.nut_prec_angles) // (r.phase_degree + 1)
        for name in ("nut_prec_ra", "nut_prec_dec", "nut_prec_pm"):
            if len(getattr(r, name)) > angle_count:
                raise PipelineError(f"pack: {what}: {name} has more terms than phase angles")
        values = r.pole_ra + r.pole_dec + r.pm + r.nut_prec_ra + r.nut_prec_dec + r.nut_prec_pm + r.nut_prec_angles
        if not all(math.isfinite(v) for v in values):
            raise PipelineError(f"pack: {what}: non-finite coefficient")
    if content.time is None:
        raise PipelineError("pack: no time data (leap seconds and TDB model)")
    check_source(content.time.source_id, "time data")
    for segment in content.segments:
        what = f"segment {segment.target} wrt {segment.center}"
        check_source(segment.source_id, what)
        if segment.target not in known_bodies or segment.center not in known_bodies:
            raise PipelineError(f"pack: {what}: target or center is not in the pack")
        if segment.spk_type not in (2, 3):
            raise PipelineError(f"pack: {what}: SPK type {segment.spk_type}")
        components = 3 if segment.spk_type == 2 else 6
        if segment.records.dtype != numpy.float64 or segment.records.ndim != 2 or segment.record_count == 0:
            raise PipelineError(f"pack: {what}: records must be a non-empty float64 matrix")
        if segment.record_size < 2 + components or (segment.record_size - 2) % components:
            raise PipelineError(f"pack: {what}: record size {segment.record_size} does not fit SPK type {segment.spk_type}")
        if not (segment.interval_s > 0 and segment.init_s <= segment.start_s < segment.end_s):
            raise PipelineError(f"pack: {what}: invalid coverage")
        if segment.end_s > segment.init_s + segment.record_count * segment.interval_s:
            raise PipelineError(f"pack: {what}: coverage extends past the last record")
        if not numpy.isfinite(segment.records).all():
            raise PipelineError(f"pack: {what}: non-finite coefficient")
    primaries = {b.id: b.primary for b in content.bodies}
    targets = {s.target for s in content.segments}
    approximated: set[BodyRef] = set()
    for a in content.approximations:
        what = f"approximate position of {a.body}"
        if a.body not in known_bodies:
            raise PipelineError(f"pack: {what}: body is not in the pack")
        if a.body in approximated:
            raise PipelineError(f"pack: {what} appears twice")
        approximated.add(a.body)
        if primaries[a.body] is None or a.proxy != primaries[a.body]:
            raise PipelineError(f"pack: {what}: the proxy {a.proxy} is not the body's primary")
        if a.body in targets:
            raise PipelineError(f"pack: {what}: the body has its own ephemeris segment")
    _check_stars(content.star_catalog, check_source)
    rotating = {r.body for r in content.rotations}
    for ring in content.rings:
        what = f"ring {ring.name!r} of {ring.body}"
        if ring.body not in known_bodies:
            raise PipelineError(f"pack: {what}: body is not in the pack")
        if ring.body not in rotating:
            raise PipelineError(f"pack: {what}: the body has no rotation model to define the ring plane")
        if not ring.name:
            raise PipelineError(f"pack: {what}: no name")
        if not (math.isfinite(ring.inner_km) and math.isfinite(ring.outer_km) and 0 < ring.inner_km < ring.outer_km):
            raise PipelineError(f"pack: {what}: radii must satisfy 0 < inner < outer")
        check_source(ring.radii_source_id, what)
        if ring.optical_depth is not None:
            low, high = ring.optical_depth
            if ring.gap:
                raise PipelineError(f"pack: {what}: a gap has no optical depth")
            if not (math.isfinite(low) and math.isfinite(high) and 0 <= low <= high):
                raise PipelineError(f"pack: {what}: optical depth must satisfy 0 <= smallest <= largest")
            if ring.optical_depth_source_id is None:
                raise PipelineError(f"pack: {what}: optical depth without a source")
            check_source(ring.optical_depth_source_id, what)
        elif ring.optical_depth_source_id is not None or ring.optical_depth_locator:
            raise PipelineError(f"pack: {what}: optical depth source without a value")


def _check_stars(catalog: PackStarCatalog | None, check_source) -> None:
    if catalog is None:
        return
    check_source(catalog.source_id, "star catalog")
    if not math.isfinite(catalog.epoch_tdb_s):
        raise PipelineError("pack: star catalog epoch is not finite")
    if not catalog.stars:
        raise PipelineError("pack: star catalog without stars")
    ids = [s.id for s in catalog.stars]
    if len(set(ids)) != len(ids):
        raise PipelineError("pack: star catalog lists a star twice")
    for s in catalog.stars:
        what = f"star {s.id}"
        if s.id[0] not in (int(v) for v in IdNamespace):
            raise PipelineError(f"pack: {what}: unknown id namespace")
        if not (0.0 <= s.ra_rad < 2 * math.pi and -math.pi / 2 <= s.dec_rad <= math.pi / 2):
            raise PipelineError(f"pack: {what}: direction out of range")
        if (s.pm_ra_cosdec_rad_per_s is None) != (s.pm_dec_rad_per_s is None):
            raise PipelineError(f"pack: {what}: proper motion needs both components")
        values = [s.v_mag] + [v for v in (s.b_v, s.pm_ra_cosdec_rad_per_s, s.pm_dec_rad_per_s) if v is not None]
        if not all(math.isfinite(v) for v in values):
            raise PipelineError(f"pack: {what}: non-finite value")
