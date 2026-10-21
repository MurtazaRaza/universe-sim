"""Shared helpers for reference outputs: number formatting and provenance headers."""

from __future__ import annotations

import platform
from collections.abc import Iterable
from pathlib import Path

from .. import PipelineError, __version__
from ..kernels import toolkit_versions
from ..sources import LockEntry


def exact(value: float) -> str:
    """Shortest text that reads back as the same double (Python repr); C++ strtod parses it exactly."""
    text = repr(float(value))
    if text in ("inf", "-inf", "nan"):
        raise PipelineError(f"non-finite reference value {value}")
    return text


def cpp_double(value: float) -> str:
    """A C++ double literal that the compiler parses to the same double."""
    text = exact(value)
    if "." not in text and "e" not in text:
        text += ".0"
    return text


def tool_line() -> str:
    tools = toolkit_versions()
    return (
        f"usim-pipeline {__version__}, Python {platform.python_version()}, SpiceyPy {tools['spiceypy']} "
        f"({tools['toolkit']}), numpy {tools['numpy']}"
    )


def kernel_lines(entries: Iterable[LockEntry]) -> list[str]:
    """One line per input file: name, URL, size, SHA-256 and retrieval time, from the lock file."""
    return [
        f"{e.name} ({e.source_id}): {e.url}, {e.size} bytes, sha256 {e.sha256}, retrieved {e.retrieved_utc}"
        for e in entries
    ]


def write_csv(path: Path, header: list[str], columns: list[str], rows: Iterable[Iterable[str]]) -> int:
    """Writes a CSV file whose provenance header lines start with '#'. Returns the number of rows."""
    lines = [f"# {line}" if line else "#" for line in header]
    lines.append(",".join(columns))
    count = 0
    for row in rows:
        values = list(row)
        if len(values) != len(columns) or any("," in v or "\n" in v for v in values):
            raise PipelineError(f"{path.name}: malformed row {values}")
        lines.append(",".join(values))
        count += 1
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return count
