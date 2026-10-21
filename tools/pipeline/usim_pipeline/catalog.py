"""Catalog builder (P2-04, P2-05): bodies, sourced quantities and IAU rotation elements from text kernels.

Every value records its file and keyword. Text kernels are loaded one at a time so a value can only come
from the file named in its locator. Derived values record their formula.
"""

from __future__ import annotations

import decimal
from collections.abc import Callable
from dataclasses import dataclass
from pathlib import Path

from . import PipelineError
from .jsonio import read_json
from .kernels import KernelReader, body_name, kernel_pool
from .manual import ManualEntry
from .pack.model import BodyKind, Field, Frame, PackBody, PackQuantity, PackRotation, Unit, naif


@dataclass(frozen=True)
class Selection:
    """One body of a selection file (data/catalog/bodies-*.json)."""

    naif_id: int
    kind: BodyKind
    primary: int | None


def load_selection(path: Path) -> list[Selection]:
    document = read_json(path)
    if not isinstance(document, dict) or document.get("schema_version") != 1:
        raise PipelineError(f"{path}: expected an object with schema_version 1")
    result: list[Selection] = []
    for index, entry in enumerate(document.get("bodies", [])):
        where = f"{path.name}: bodies[{index}]"
        if not isinstance(entry, dict) or set(entry) != {"naif_id", "kind", "primary"}:
            raise PipelineError(f"{where}: needs exactly naif_id, kind and primary")
        naif_id, primary = entry["naif_id"], entry["primary"]
        if isinstance(naif_id, bool) or not isinstance(naif_id, int):
            raise PipelineError(f"{where}: naif_id must be an integer")
        if primary is not None and (isinstance(primary, bool) or not isinstance(primary, int)):
            raise PipelineError(f"{where}: primary must be an integer or null")
        try:
            kind = BodyKind[str(entry["kind"]).upper()]
        except KeyError:
            raise PipelineError(f"{where}: unknown kind {entry['kind']!r}") from None
        result.append(Selection(naif_id, kind, primary))
    ids = [s.naif_id for s in result]
    if len(set(ids)) != len(ids):
        raise PipelineError(f"{path}: a body is listed twice")
    for s in result:
        if s.primary is not None and s.primary not in ids:
            raise PipelineError(f"{path}: primary {s.primary} of {s.naif_id} is not selected")
    if not result:
        raise PipelineError(f"{path}: no bodies")
    return result


def display_name(naif_name: str) -> str:
    """NAIF names are upper case ("EARTH BARYCENTER"); the catalog shows them title-cased ("Earth Barycenter")."""
    return " ".join(word.capitalize() for word in naif_name.split())


def system_barycenter(naif_id: int) -> int:
    """The body whose BODY<n>_NUT_PREC_ANGLES apply to `naif_id`: for planets and satellites (100 to 999)
    their system barycenter (id // 100), as in SPICE; otherwise the body itself."""
    return naif_id // 100 if 100 < naif_id < 1000 else naif_id


_DECIMAL = decimal.Context(prec=50, rounding=decimal.ROUND_HALF_EVEN)


def volumetric_mean_radius(a: float, b: float, c: float) -> float:
    """cbrt(a b c), the radius of the sphere with the ellipsoid's volume.

    Computed in 50-digit decimal arithmetic and rounded once to the nearest double, so the result does not
    depend on the platform's libm (packs must be byte-identical across machines, P2-08).
    """
    product = _DECIMAL.multiply(_DECIMAL.multiply(decimal.Decimal(a), decimal.Decimal(b)), decimal.Decimal(c))
    root = _DECIMAL.power(product, _DECIMAL.divide(decimal.Decimal(1), decimal.Decimal(3)))
    return float(root)


MEAN_RADIUS_DERIVATION = (
    "cbrt(a * b * c) with a, b, c from {keyword}: radius of the sphere with the volume of the triaxial "
    "ellipsoid; 50-digit decimal arithmetic, rounded once to double"
)

_UNSUPPORTED = ("CONSTANTS_JED_EPOCH", "CONSTANTS_REF_FRAME")


def read_shapes_and_rotations(
    selection: list[Selection], pck: Path, source_id: str, log: Callable[[str], None] = print
) -> tuple[list[PackQuantity], list[PackRotation]]:
    """Radii (and the derived mean radius) and IAU rotation elements of the selected bodies from a text PCK."""
    quantities: list[PackQuantity] = []
    rotations: list[PackRotation] = []
    file = pck.name
    reader = KernelReader(pck)
    numbers = reader.numbers
    with kernel_pool(pck):
        for s in selection:
            body = naif(s.naif_id)
            bary = system_barycenter(s.naif_id)
            for item in _UNSUPPORTED:
                for owner in sorted({s.naif_id, bary}):
                    if reader.defines(f"BODY{owner}_{item}"):
                        raise PipelineError(f"{file}: BODY{owner}_{item} is not supported by pack format 1")

            keyword = f"BODY{s.naif_id}_RADII"
            radii = numbers(keyword)
            if radii is not None:
                if len(radii) != 3 or not all(r > 0 for r in radii):
                    raise PipelineError(f"{file}: {keyword} must hold three positive radii")
                for i, field in enumerate((Field.RADIUS_A, Field.RADIUS_B, Field.RADIUS_C)):
                    quantities.append(
                        PackQuantity(body, field, Unit.KM, radii[i], None, source_id, f"{file} {keyword}[{i}]")
                    )
                quantities.append(
                    PackQuantity(
                        body, Field.MEAN_RADIUS, Unit.KM, volumetric_mean_radius(*radii), None, source_id,
                        f"{file} {keyword}", MEAN_RADIUS_DERIVATION.format(keyword=keyword),
                    )
                )  # fmt: skip

            prefix = f"BODY{s.naif_id}_"
            pole_ra = numbers(prefix + "POLE_RA")
            pole_dec = numbers(prefix + "POLE_DEC")
            pm = numbers(prefix + "PM")
            if pole_ra is None and pole_dec is None and pm is None:
                continue
            if pole_ra is None or pole_dec is None or pm is None:
                raise PipelineError(f"{file}: body {s.naif_id} has only part of POLE_RA, POLE_DEC and PM")
            nut = {name: numbers(prefix + name) or () for name in ("NUT_PREC_RA", "NUT_PREC_DEC", "NUT_PREC_PM")}
            used = ["POLE_RA", "POLE_DEC", "PM"] + [name for name, values in nut.items() if values]
            locator = f"{file} " + ", ".join(prefix + name for name in used)
            angles: tuple[float, ...] = ()
            degree = 1
            angles_body = 0
            if any(nut.values()):
                angles_body = bary
                angles = numbers(f"BODY{bary}_NUT_PREC_ANGLES") or ()
                if not angles:
                    raise PipelineError(f"{file}: body {s.naif_id} has nutation terms but BODY{bary}_NUT_PREC_ANGLES is missing")
                locator += f"; BODY{bary}_NUT_PREC_ANGLES"
                max_degree = numbers(f"BODY{bary}_MAX_PHASE_DEGREE")
                if max_degree is not None:
                    if len(max_degree) != 1 or max_degree[0] != int(max_degree[0]) or max_degree[0] < 1:
                        raise PipelineError(f"{file}: BODY{bary}_MAX_PHASE_DEGREE must be one integer >= 1")
                    degree = int(max_degree[0])
                    locator += f", BODY{bary}_MAX_PHASE_DEGREE"
            rotations.append(
                PackRotation(
                    body=body, frame=Frame.ICRF, source_id=source_id, locator=locator, pole_ra=pole_ra,
                    pole_dec=pole_dec, pm=pm, nut_prec_ra=nut["NUT_PREC_RA"], nut_prec_dec=nut["NUT_PREC_DEC"],
                    nut_prec_pm=nut["NUT_PREC_PM"], angles_body=angles_body, phase_degree=degree,
                    nut_prec_angles=angles,
                )
            )  # fmt: skip
    log("  " + reader.report())
    return quantities, rotations


def read_gms(
    selection: list[Selection], gm_kernel: Path, source_id: str, log: Callable[[str], None] = print
) -> list[PackQuantity]:
    """GM of the selected bodies from a text PCK (BODYnnn_GM, km^3/s^2)."""
    result = []
    reader = KernelReader(gm_kernel)
    with kernel_pool(gm_kernel):
        for s in selection:
            keyword = f"BODY{s.naif_id}_GM"
            values = reader.numbers(keyword)
            if values is None:
                continue
            if len(values) != 1 or not values[0] > 0:
                raise PipelineError(f"{gm_kernel.name}: {keyword} must hold one positive value")
            result.append(
                PackQuantity(naif(s.naif_id), Field.GM, Unit.KM3_PER_S2, values[0], None, source_id, f"{gm_kernel.name} {keyword}")
            )
    log("  " + reader.report())
    return result


def build_bodies(selection: list[Selection]) -> list[PackBody]:
    """Bodies with NAIF names (from the toolkit's built-in table), in selection order."""
    return [
        PackBody(naif(s.naif_id), s.kind, display_name(body_name(s.naif_id)), None if s.primary is None else naif(s.primary))
        for s in selection
    ]


def apply_manual_entries(
    selection: list[Selection], quantities: list[PackQuantity], entries: list[ManualEntry]
) -> list[PackQuantity]:
    """Adds manual entries for the selected bodies. An entry for a field a kernel already provides fails:
    manual transcription is only for values with no machine-readable Tier 1 source."""
    selected = {s.naif_id for s in selection}
    present = {(q.body, q.field) for q in quantities}
    result = list(quantities)
    for entry in entries:
        if entry.naif_id not in selected:
            continue
        key = (naif(entry.naif_id), entry.field)
        if key in present:
            raise PipelineError(
                f"{entry.file}: entries[{entry.index}]: {entry.field.name.lower()} of {entry.naif_id} is already "
                "provided by a kernel; manual entries are only for values with no machine-readable source"
            )
        result.append(
            PackQuantity(key[0], entry.field, entry.unit, entry.value, entry.uncertainty, entry.source_id, entry.locator)
        )
    return result
