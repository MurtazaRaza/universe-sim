"""TDB calendar text <-> TDB seconds past J2000 for pipeline inputs and reports.

TDB is uniform, so this is plain proleptic Gregorian calendar arithmetic (no leap seconds). Used for pack
windows and for printing coverage; the core's time module (and CSPICE) remain the reference for time scales.
"""

from __future__ import annotations

import datetime
import re

from . import PipelineError

_J2000 = datetime.datetime(2000, 1, 1, 12, 0, 0)
_TEXT = re.compile(r"(\d{4})-(\d{2})-(\d{2})(?:[T ](\d{2}):(\d{2}):(\d{2}))? TDB")


def parse_tdb(text: str) -> float:
    """Seconds past J2000 TDB of "YYYY-MM-DD[THH:MM:SS] TDB" (years 1 to 9999, proleptic Gregorian)."""
    match = _TEXT.fullmatch(text)
    if not match:
        raise PipelineError(f"expected a TDB time like '1900-01-01T00:00:00 TDB', got {text!r}")
    parts = [int(p) if p is not None else 0 for p in match.groups()]
    try:
        moment = datetime.datetime(*parts)  # type: ignore[arg-type]
    except ValueError as error:
        raise PipelineError(f"{text!r}: {error}") from None
    delta = moment - _J2000
    return float(delta.days * 86400 + delta.seconds)


def format_tdb(seconds: float) -> str:
    """Calendar text of TDB seconds past J2000, to the second, for example "1849-12-26T00:00:00 TDB"."""
    moment = _J2000 + datetime.timedelta(seconds=round(seconds))
    return moment.strftime("%Y-%m-%dT%H:%M:%S") + " TDB"
