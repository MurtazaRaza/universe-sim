"""Ephemeris segment extraction (P2-03) and trimming to a time window (P2-07)."""

from __future__ import annotations

from . import PipelineError
from .kernels import NAIF_FRAME_J2000, SpkSegment, record_range
from .pack.model import Frame, PackSegment, naif

_FRAMES = {NAIF_FRAME_J2000: Frame.ICRF}


def trim_records(segment: SpkSegment, window: tuple[float, float] | None) -> tuple[int, int] | None:
    """Indices [first, last] of the records overlapping `window` (TDB seconds past J2000), the whole segment
    for no window, or None if the segment does not reach into the window."""
    return record_range(segment.init_s, segment.interval_s, segment.record_count, segment.start_s, segment.end_s, window)


def to_pack_segment(segment: SpkSegment, source_id: str, window: tuple[float, float] | None) -> PackSegment | None:
    """The segment's records overlapping the window, as a pack segment, or None if none overlap.

    Records are copied bit for bit. The coverage of a trimmed segment is the span of the records kept
    (clipped to the original coverage), so it can extend slightly beyond the window.
    """
    frame = _FRAMES.get(segment.frame)
    if frame is None:
        raise PipelineError(f"{segment.file_name} segment {segment.index}: frame code {segment.frame} is not supported")
    kept = trim_records(segment, window)
    if kept is None:
        return None
    first, last = kept
    init = segment.init_s + first * segment.interval_s
    return PackSegment(
        target=naif(segment.target),
        center=naif(segment.center),
        frame=frame,
        spk_type=segment.data_type,
        start_s=max(segment.start_s, init),
        end_s=min(segment.end_s, segment.init_s + (last + 1) * segment.interval_s),
        init_s=init,
        interval_s=segment.interval_s,
        records=segment.records[first : last + 1].copy(),
        source_id=source_id,
        locator=f"{segment.file_name} segment {segment.index} ({segment.name})",
    )
