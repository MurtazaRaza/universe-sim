"""Time data from a leap seconds kernel: the leap second table and the TDB - TT model parameters (P2-04)."""

from __future__ import annotations

from collections.abc import Callable
from pathlib import Path

from . import PipelineError
from .kernels import KernelReader, kernel_pool, parse_at_date
from .pack.model import PackTime


def leap_second_rows(reader: KernelReader) -> tuple[tuple[int, int, int, int], ...]:
    """DELTET/DELTA_AT as (year, month, day, TAI - UTC) rows. The kernel lists pairs of a whole number of
    seconds and an @-date at 00:00 UTC; both readings (file text and CSPICE) are checked by the reader."""
    values = reader.numbers("DELTET/DELTA_AT")
    tokens = reader.kernel.text("DELTET/DELTA_AT")
    if values is None or tokens is None or len(values) % 2 or not values:
        raise PipelineError(f"{reader.file_name}: DELTET/DELTA_AT must hold pairs of values")
    rows = []
    for i in range(0, len(values), 2):
        tai_minus_utc = values[i]
        if tai_minus_utc != int(tai_minus_utc) or not tokens[i + 1].startswith("@"):
            raise PipelineError(f"{reader.file_name}: unexpected DELTET/DELTA_AT entry {tokens[i]} {tokens[i + 1]}")
        rows.append((*parse_at_date(tokens[i + 1]), int(tai_minus_utc)))
    return tuple(rows)


def read_time_data(lsk: Path, source_id: str, log: Callable[[str], None] = print) -> PackTime:
    """Leap seconds (DELTET/DELTA_AT) and TDB - TT model parameters (DELTET/DELTA_T_A, K, EB, M)."""
    reader = KernelReader(lsk)
    with kernel_pool(lsk):
        sizes = {"DELTA_T_A": 1, "K": 1, "EB": 1, "M": 2}
        values = {name: reader.numbers(f"DELTET/{name}") for name in sizes}
        for name, size in sizes.items():
            found = values[name]
            if found is None or len(found) != size:
                raise PipelineError(f"{lsk.name}: DELTET/{name} must hold {size} value(s)")
        rows = leap_second_rows(reader)
    log("  " + reader.report())
    m = values["M"]
    assert m is not None
    return PackTime(
        source_id=source_id,
        locator=f"{lsk.name} DELTET/DELTA_AT, DELTET/DELTA_T_A, DELTET/K, DELTET/EB, DELTET/M",
        tt_minus_tai_s=values["DELTA_T_A"][0],  # type: ignore[index]
        k_s=values["K"][0],  # type: ignore[index]
        eb=values["EB"][0],  # type: ignore[index]
        m0_rad=m[0],
        m1_rad_per_s=m[1],
        leap_seconds=rows,
    )
