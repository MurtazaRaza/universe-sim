"""JSON helpers: strict reading, stable writing and a canonical form for hashing."""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any

from . import PipelineError


def _reject_duplicates(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise PipelineError(f"duplicate JSON key {key!r}")
        result[key] = value
    return result


def read_json(path: Path) -> Any:
    """Parses a UTF-8 JSON file. Duplicate keys are an error, not silently last-wins."""
    try:
        text = path.read_text(encoding="utf-8")
    except FileNotFoundError:
        raise PipelineError(f"{path} does not exist") from None
    try:
        return json.loads(text, object_pairs_hook=_reject_duplicates)
    except json.JSONDecodeError as error:
        raise PipelineError(f"{path}: invalid JSON: {error}") from None
    except PipelineError as error:
        raise PipelineError(f"{path}: {error}") from None


def write_json(path: Path, value: Any) -> None:
    """Writes `value` as indented UTF-8 JSON with a trailing newline (keys in insertion order)."""
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def canonical_json_bytes(value: Any) -> bytes:
    """A byte form that does not depend on formatting or key order: sorted keys, no whitespace, UTF-8.

    Used to hash the lock file, so line endings or indentation do not change the hash.
    """
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False, allow_nan=False).encode(
        "utf-8"
    )
