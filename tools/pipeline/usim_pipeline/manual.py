"""Manual catalog entries: data/catalog/manual/*.json (SPEC section 10, P2-06).

Manual transcription is allowed only when no machine-readable Tier 1 source provides a value. Every entry
must say exactly where the value was copied from; the pipeline rejects entries missing any of the required
fields, entries citing undeclared sources, and entries for fields a kernel already provides.

Format:
    {
      "schema_version": 1,
      "entries": [
        {
          "naif_id": 999,
          "field": "gm",                    gm, mean_radius, radius_a, radius_b, radius_c, mean_temperature,
                                            surface_pressure or reference_pressure
          "value": 869.6,                   in `unit`
          "unit": "km3_per_s2",             must be the field's unit
          "uncertainty": null,              optional one-sigma value in `unit`
          "source_id": "S-JPL-SSD-PARAMS",  declared in data/sources.json
          "url": "https://...",             the page the value was copied from
          "accessed": "2026-10-20",         date the page was read (YYYY-MM-DD)
          "table": "Table 1, row Pluto, column GM",
          "transcriber_note": "Who copied it and how it was checked."
        }
      ]
    }

Instead of "value", an entry may give the source's own text and unit, "text": "-65" and "text_unit": "celsius" (to
kelvin) or "bar" (to pascals). The pipeline converts it by definition in exact decimal arithmetic and records the
conversion as the quantity's derivation (ADR-0017).
"""

from __future__ import annotations

import datetime
import math
import re
from dataclasses import dataclass
from decimal import Decimal
from pathlib import Path

from . import PipelineError
from .jsonio import read_json
from .pack.model import FIELD_UNITS, Field, Unit
from .sources import Source

REQUIRED_TEXT = ("source_id", "url", "accessed", "table", "transcriber_note")

# Unit conversions by definition, NIST Special Publication 811 (2008), Appendix B.8 (read 2026-11-02): T/K =
# t/degC + 273.15, and 1 bar = 1.0 E+05 Pa (exact). text_unit -> (the unit converted to, the conversion, a
# description for the derivation).
_DECIMAL_TEXT = re.compile(r"-?\d{1,3}(,\d{3})*(\.\d+)?|-?\d+(\.\d+)?")
CONVERSIONS = {
    "celsius": (Unit.K, lambda t: t + Decimal("273.15"), "T/K = t/degC + 273.15"),
    "bar": (Unit.PA, lambda t: t * Decimal(100000), "1 bar = 100000 Pa"),
}


@dataclass(frozen=True)
class ManualEntry:
    file: str  # path relative to data/catalog
    index: int
    naif_id: int
    field: Field
    unit: Unit
    value: float
    uncertainty: float | None
    source_id: str
    url: str
    accessed: str
    table: str
    transcriber_note: str
    derivation: str = ""  # the conversion of a "text" entry, empty for a "value" entry

    @property
    def locator(self) -> str:
        return f"{self.table} ({self.url}, accessed {self.accessed}; transcribed in data/catalog/{self.file})"


def _number(value: object, where: str) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(float(value)):
        raise PipelineError(f"{where}: must be a finite number")
    return float(value)


def parse_manual_file(document: object, file: str, sources: tuple[Source, ...]) -> list[ManualEntry]:
    """Validates one manual entry file."""
    if not isinstance(document, dict) or document.get("schema_version") != 1:
        raise PipelineError(f"{file}: expected an object with schema_version 1")
    entries = document.get("entries")
    if not isinstance(entries, list):
        raise PipelineError(f"{file}: 'entries' must be a list")
    declared = {s.id for s in sources}
    result = []
    for index, entry in enumerate(entries):
        where = f"{file}: entries[{index}]"
        if not isinstance(entry, dict):
            raise PipelineError(f"{where}: must be an object")
        for key in REQUIRED_TEXT:
            if not isinstance(entry.get(key), str) or not entry[key].strip():
                raise PipelineError(f"{where}: '{key}' is required and must be non-empty")
        known = {"naif_id", "field", "value", "text", "text_unit", "unit", "uncertainty", *REQUIRED_TEXT}
        unknown = set(entry) - known
        if unknown:
            raise PipelineError(f"{where}: unknown keys {sorted(unknown)}")
        naif_id = entry.get("naif_id")
        if isinstance(naif_id, bool) or not isinstance(naif_id, int):
            raise PipelineError(f"{where}: 'naif_id' must be an integer")
        try:
            field = Field[str(entry.get("field")).upper()]
            unit = Unit[str(entry.get("unit")).upper()]
        except KeyError:
            raise PipelineError(f"{where}: unknown field {entry.get('field')!r} or unit {entry.get('unit')!r}") from None
        if FIELD_UNITS[field] != unit:
            raise PipelineError(f"{where}: field {field.name.lower()} needs unit {FIELD_UNITS[field].name.lower()}")
        uncertainty = entry.get("uncertainty")
        if uncertainty is not None:
            uncertainty = _number(uncertainty, f"{where}: uncertainty")
            if uncertainty < 0:
                raise PipelineError(f"{where}: uncertainty must not be negative")
        if entry["source_id"] not in declared:
            raise PipelineError(f"{where}: source {entry['source_id']} is not declared in sources.json")
        if not entry["url"].startswith("https://"):
            raise PipelineError(f"{where}: url must use https")
        try:
            datetime.date.fromisoformat(entry["accessed"])
        except ValueError:
            raise PipelineError(f"{where}: 'accessed' must be a date like 2026-10-20") from None
        value, derivation = _value(entry, unit, where)
        result.append(
            ManualEntry(
                file=file,
                index=index,
                naif_id=naif_id,
                field=field,
                unit=unit,
                value=value,
                uncertainty=uncertainty,
                source_id=entry["source_id"],
                url=entry["url"],
                accessed=entry["accessed"],
                table=entry["table"],
                transcriber_note=entry["transcriber_note"],
                derivation=derivation,
            )
        )
    return result


def _value(entry: dict, unit: Unit, where: str) -> tuple[float, str]:
    """The entry's value in `unit` and, for a "text" entry, the conversion that gave it."""
    if ("value" in entry) == ("text" in entry):
        raise PipelineError(f"{where}: give either 'value' or 'text' with 'text_unit'")
    if "value" in entry:
        if "text_unit" in entry:
            raise PipelineError(f"{where}: 'text_unit' goes with 'text'")
        return _number(entry["value"], f"{where}: value"), ""
    text, text_unit = entry.get("text"), entry.get("text_unit")
    if not isinstance(text, str) or not _DECIMAL_TEXT.fullmatch(text):
        raise PipelineError(f"{where}: 'text' must be a decimal number as the source writes it, for example '12,104'")
    if text_unit not in CONVERSIONS:
        raise PipelineError(f"{where}: 'text_unit' must be one of {sorted(CONVERSIONS)}")
    target, convert, rule = CONVERSIONS[text_unit]
    if target != unit:
        raise PipelineError(f"{where}: text in {text_unit} converts to {target.name.lower()}, not {unit.name.lower()}")
    converted = convert(Decimal(text.replace(",", "")))
    return float(converted), f"converted from {text} {text_unit} ({rule}, NIST SP 811 Appendix B.8)"


def load_manual_entries(catalog_dir: Path, sources: tuple[Source, ...]) -> list[ManualEntry]:
    """All entries of data/catalog/manual/*.json, in file name order. Duplicate (body, field) pairs fail."""
    entries: list[ManualEntry] = []
    manual_dir = catalog_dir / "manual"
    for path in sorted(manual_dir.glob("*.json")) if manual_dir.is_dir() else []:
        relative = path.relative_to(catalog_dir).as_posix()
        entries.extend(parse_manual_file(read_json(path), relative, sources))
    seen: set[tuple[int, Field]] = set()
    for entry in entries:
        key = (entry.naif_id, entry.field)
        if key in seen:
            raise PipelineError(f"{entry.file}: {entry.field.name.lower()} of {entry.naif_id} is entered twice")
        seen.add(key)
    return entries
