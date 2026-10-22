"""The star catalog of a pack (SPEC FR-REN-09): the Yale Bright Star Catalogue, 5th revised edition (CDS
catalog V/50, file catalog.gz), parsed with the byte layout of its ReadMe.

Values are converted from the catalog's units with IEEE double arithmetic only (no libm), so the result does
not depend on the platform (packs are byte-identical across machines, P2-08).
"""

from __future__ import annotations

import gzip
import math
from fractions import Fraction
from pathlib import Path

from . import PipelineError
from .pack.model import IdNamespace, PackStar, PackStarCatalog

# Byte ranges (1-based, inclusive) of the columns used, from "Byte-by-byte Description of file: catalog" in
# the V/50 ReadMe. The ReadMe is pinned with the catalog, and the parser checks these labels in it.
COLUMNS = {
    "HR": (1, 4),
    "Name": (5, 14),
    "RAh": (76, 77),
    "RAm": (78, 79),
    "RAs": (80, 83),
    "DE-": (84, 84),
    "DEd": (85, 86),
    "DEm": (87, 88),
    "DEs": (89, 90),
    "Vmag": (103, 107),
    "B-V": (110, 114),
    "pmRA": (149, 154),
    "pmDE": (155, 160),
}
RECORD_LENGTH = 197

ARCSEC_TO_RAD = math.pi / 648000.0  # 648000 arcseconds in pi radians
JULIAN_YEAR_S = 365.25 * 86400.0  # IAU: the Julian year of 365.25 days of 86400 SI seconds
EPOCH_TDB_S = 0.0  # catalog epoch 2000.0, the Julian epoch J2000.0

LOCATOR = (
    "catalog.gz (V/50): bytes 1-4 HR, 5-14 Name, 76-90 RA and Dec (equinox J2000, epoch 2000.0), 103-107 Vmag, "
    "110-114 B-V, 149-160 pmRA and pmDE (FK5, arcsec/yr)"
)
DERIVATION = (
    "RA = (RAh*3600 + RAm*60 + RAs)*15 arcsec, Dec = sign*(DEd*3600 + DEm*60 + DEs) arcsec, summed exactly and "
    "converted with pi/648000 rad per arcsec; proper motions in arcsec per Julian year (365.25 d) converted to "
    "rad/s (pmRA is pmRA*cos(Dec) per the ReadMe). FK5 equinox J2000 coordinates are used as ICRF directions "
    "without a frame tie correction (the catalog gives RA to 0.1 s of time and Dec to 1 arcsec). Epoch 2000.0 "
    "is stored as J2000.0 (TDB 0 s). Objects without a J2000 position or V magnitude (removed from the "
    "catalog) are left out."
)


def check_readme(readme: Path) -> None:
    """Fails unless the pinned ReadMe describes the columns at the byte ranges the parser uses."""
    text = readme.read_text(encoding="ascii", errors="replace")
    if "Byte-by-byte Description of file: catalog" not in text:
        raise PipelineError(f"{readme.name}: no byte-by-byte description of the catalog file")
    lines = text.splitlines()
    for label, (first, last) in COLUMNS.items():
        span = f"{first:4d}-{last:3d}" if first != last else f"{first:8d}"
        if not any(line.startswith(span) and f" {label} " in line + " " for line in lines):
            raise PipelineError(f"{readme.name}: column {label} is not described at bytes {first}-{last}")


def _field(line: str, label: str) -> str:
    first, last = COLUMNS[label]
    return line[first - 1 : last].strip()


def _number(text: str, where: str) -> Fraction:
    try:
        return Fraction(text)
    except ValueError:
        raise PipelineError(f"{where}: {text!r} is not a number") from None


def parse_catalog(data: bytes, source_id: str) -> PackStarCatalog:
    """The stars of the V/50 catalog file (uncompressed bytes)."""
    stars: list[PackStar] = []
    lines = data.decode("ascii").splitlines()
    for number, raw in enumerate(lines, start=1):
        if len(raw) > RECORD_LENGTH:
            raise PipelineError(f"catalog line {number}: longer than {RECORD_LENGTH} bytes")
        line = raw.ljust(RECORD_LENGTH)
        where = f"catalog line {number}"
        if not _field(line, "RAh") or not _field(line, "Vmag"):
            continue  # removed object: no J2000 position or magnitude
        hr = int(_field(line, "HR"))
        ra_arcsec = (
            _number(_field(line, "RAh"), where) * 3600 + _number(_field(line, "RAm"), where) * 60
            + _number(_field(line, "RAs"), where)
        ) * 15  # fmt: skip
        sign = _field(line, "DE-")
        if sign not in ("+", "-"):
            raise PipelineError(f"{where}: declination sign {sign!r}")
        dec_arcsec = (
            _number(_field(line, "DEd"), where) * 3600 + _number(_field(line, "DEm"), where) * 60
            + _number(_field(line, "DEs"), where)
        ) * (-1 if sign == "-" else 1)  # fmt: skip
        if not (0 <= ra_arcsec < 1296000 and -324000 <= dec_arcsec <= 324000):
            raise PipelineError(f"{where}: position out of range")
        pm_ra, pm_dec = _field(line, "pmRA"), _field(line, "pmDE")
        moving = bool(pm_ra) and bool(pm_dec)
        color = _field(line, "B-V")
        stars.append(
            PackStar(
                id=(int(IdNamespace.HR), hr),
                ra_rad=float(ra_arcsec) * ARCSEC_TO_RAD,
                dec_rad=float(dec_arcsec) * ARCSEC_TO_RAD,
                pm_ra_cosdec_rad_per_s=float(_number(pm_ra, where)) * ARCSEC_TO_RAD / JULIAN_YEAR_S if moving else None,
                pm_dec_rad_per_s=float(_number(pm_dec, where)) * ARCSEC_TO_RAD / JULIAN_YEAR_S if moving else None,
                v_mag=float(_number(_field(line, "Vmag"), where)),
                b_v=float(_number(color, where)) if color else None,
                designation=" ".join(_field(line, "Name").split()),
            )
        )
    if not stars:
        raise PipelineError("catalog: no stars")
    return PackStarCatalog(source_id, EPOCH_TDB_S, LOCATOR, DERIVATION, tuple(stars))


def read_star_catalog(catalog: Path, readme: Path, source_id: str) -> PackStarCatalog:
    """Checks the ReadMe's column layout, then parses the gzip-compressed catalog."""
    check_readme(readme)
    with gzip.open(catalog, "rb") as stream:
        return parse_catalog(stream.read(), source_id)
