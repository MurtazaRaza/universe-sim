"""The source registry (data/sources.json) and the lock file (data/sources.lock.json).

sources.json declares which sources and files the pipeline may use; it mirrors docs/DATA_SOURCES.md.
sources.lock.json pins the exact bytes of every downloaded file (size, SHA-256, MD5, the publisher's MD5
where one exists, and the retrieval time). Both are committed (ADR-0005).
"""

from __future__ import annotations

import hashlib
import re
from dataclasses import asdict, dataclass
from pathlib import Path, PurePosixPath
from typing import Any

from . import PipelineError
from .jsonio import canonical_json_bytes, read_json, write_json

SCHEMA_VERSION = 1

_SOURCE_ID = re.compile(r"S-[A-Z0-9]+(-[A-Z0-9]+)*")
_SHA256 = re.compile(r"[0-9a-f]{64}")
_MD5 = re.compile(r"[0-9a-f]{32}")
_UTC = re.compile(r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z")


@dataclass(frozen=True)
class SourceFile:
    """A file a source provides, as declared in sources.json."""

    name: str
    path: str  # relative to data/raw, POSIX separators
    url: str
    publisher_checksums: str | None  # URL of the publisher's MD5 list (NAIF aa_checksums.txt), if any


@dataclass(frozen=True)
class Source:
    """A registry entry (docs/DATA_SOURCES.md)."""

    id: str
    title: str
    provider: str
    tier: int
    url: str
    citation: str
    license: str
    files: tuple[SourceFile, ...]


@dataclass(frozen=True)
class LockEntry:
    """One pinned file in sources.lock.json."""

    source_id: str
    name: str
    path: str
    url: str
    size: int
    sha256: str
    md5: str
    publisher_md5: str | None
    publisher_checksums: str | None
    retrieved_utc: str

    @property
    def key(self) -> tuple[str, str]:
        return (self.source_id, self.name)


def _require(obj: dict[str, Any], key: str, kind: type, where: str) -> Any:
    if key not in obj:
        raise PipelineError(f"{where}: missing field {key!r}")
    value = obj[key]
    if not isinstance(value, kind) or (kind is int and isinstance(value, bool)):
        raise PipelineError(f"{where}: field {key!r} must be {kind.__name__}")
    return value


def _optional_str(obj: dict[str, Any], key: str, where: str) -> str | None:
    value = obj.get(key)
    if value is not None and not isinstance(value, str):
        raise PipelineError(f"{where}: field {key!r} must be a string or null")
    return value


def _check_https(url: str, where: str) -> None:
    if not url.startswith("https://"):
        raise PipelineError(f"{where}: URL {url!r} must use https")


def _check_raw_path(path: str, where: str) -> None:
    pure = PurePosixPath(path)
    if pure.is_absolute() or "\\" in path or any(part in ("", ".", "..") for part in path.split("/")):
        raise PipelineError(f"{where}: path {path!r} must be a plain relative POSIX path under data/raw")


def parse_sources(document: Any, where: str = "sources.json") -> tuple[Source, ...]:
    """Validates a parsed sources.json document and returns its sources in file order."""
    if not isinstance(document, dict) or document.get("schema_version") != SCHEMA_VERSION:
        raise PipelineError(f"{where}: expected an object with schema_version {SCHEMA_VERSION}")
    entries = _require(document, "sources", list, where)
    sources: list[Source] = []
    seen_ids: set[str] = set()
    seen_paths: set[str] = set()
    for index, entry in enumerate(entries):
        at = f"{where}: sources[{index}]"
        if not isinstance(entry, dict):
            raise PipelineError(f"{at}: must be an object")
        source_id = _require(entry, "id", str, at)
        at = f"{where}: {source_id}"
        if not _SOURCE_ID.fullmatch(source_id):
            raise PipelineError(f"{at}: id must look like S-PROVIDER-NAME")
        if source_id in seen_ids:
            raise PipelineError(f"{at}: duplicate source id")
        seen_ids.add(source_id)
        tier = _require(entry, "tier", int, at)
        if tier not in (1, 2):
            raise PipelineError(f"{at}: tier must be 1 or 2")
        url = _require(entry, "url", str, at)
        _check_https(url, at)
        files: list[SourceFile] = []
        for file_index, item in enumerate(_require(entry, "files", list, at)):
            fat = f"{at}: files[{file_index}]"
            if not isinstance(item, dict):
                raise PipelineError(f"{fat}: must be an object")
            name = _require(item, "name", str, fat)
            path = _require(item, "path", str, fat)
            file_url = _require(item, "url", str, fat)
            checksums = _optional_str(item, "publisher_checksums", fat)
            _check_raw_path(path, fat)
            _check_https(file_url, fat)
            if checksums is not None:
                _check_https(checksums, fat)
            if PurePosixPath(path).name != name or not file_url.endswith("/" + name):
                raise PipelineError(f"{fat}: name, path and URL must all end with the same file name")
            if path in seen_paths:
                raise PipelineError(f"{fat}: path {path!r} is declared twice")
            seen_paths.add(path)
            files.append(SourceFile(name, path, file_url, checksums))
        sources.append(
            Source(
                id=source_id,
                title=_require(entry, "title", str, at),
                provider=_require(entry, "provider", str, at),
                tier=tier,
                url=url,
                citation=_require(entry, "citation", str, at),
                license=_require(entry, "license", str, at),
                files=tuple(files),
            )
        )
    return tuple(sources)


def load_sources(path: Path) -> tuple[Source, ...]:
    """Reads and validates sources.json."""
    return parse_sources(read_json(path), str(path))


def find_source(sources: tuple[Source, ...], source_id: str) -> Source:
    for source in sources:
        if source.id == source_id:
            return source
    raise PipelineError(f"source {source_id} is not declared in sources.json")


def _parse_lock(document: Any, where: str) -> dict[tuple[str, str], LockEntry]:
    if not isinstance(document, dict) or document.get("schema_version") != SCHEMA_VERSION:
        raise PipelineError(f"{where}: expected an object with schema_version {SCHEMA_VERSION}")
    result: dict[tuple[str, str], LockEntry] = {}
    for index, item in enumerate(_require(document, "files", list, where)):
        at = f"{where}: files[{index}]"
        if not isinstance(item, dict):
            raise PipelineError(f"{at}: must be an object")
        entry = LockEntry(
            source_id=_require(item, "source_id", str, at),
            name=_require(item, "name", str, at),
            path=_require(item, "path", str, at),
            url=_require(item, "url", str, at),
            size=_require(item, "size", int, at),
            sha256=_require(item, "sha256", str, at),
            md5=_require(item, "md5", str, at),
            publisher_md5=_optional_str(item, "publisher_md5", at),
            publisher_checksums=_optional_str(item, "publisher_checksums", at),
            retrieved_utc=_require(item, "retrieved_utc", str, at),
        )
        if entry.size < 0 or not _SHA256.fullmatch(entry.sha256) or not _MD5.fullmatch(entry.md5):
            raise PipelineError(f"{at}: invalid size or checksum")
        if entry.publisher_md5 is not None and not _MD5.fullmatch(entry.publisher_md5):
            raise PipelineError(f"{at}: invalid publisher_md5")
        if not _UTC.fullmatch(entry.retrieved_utc):
            raise PipelineError(f"{at}: retrieved_utc must look like 2026-10-20T12:00:00Z")
        _check_raw_path(entry.path, at)
        if entry.key in result:
            raise PipelineError(f"{at}: {entry.source_id}/{entry.name} is pinned twice")
        result[entry.key] = entry
    return result


def load_lock(path: Path) -> dict[tuple[str, str], LockEntry]:
    """Reads sources.lock.json. A missing file is an empty lock."""
    if not path.exists():
        return {}
    return _parse_lock(read_json(path), str(path))


def lock_document(entries: dict[tuple[str, str], LockEntry]) -> dict[str, Any]:
    """The JSON document for a set of lock entries, sorted by source id and file name."""
    return {"schema_version": SCHEMA_VERSION, "files": [asdict(entries[key]) for key in sorted(entries)]}


def write_lock(path: Path, entries: dict[tuple[str, str], LockEntry]) -> None:
    write_json(path, lock_document(entries))


def lock_digest(path: Path) -> str:
    """SHA-256 (hex) of the lock file's canonical JSON form; embedded in every pack built from it."""
    return hashlib.sha256(canonical_json_bytes(read_json(path))).hexdigest()
