"""The fetch and verify commands: download declared files and pin or check their checksums."""

from __future__ import annotations

import datetime
import hashlib
import os
import re
import urllib.request
from collections.abc import Callable, Iterator
from contextlib import AbstractContextManager, contextmanager
from dataclasses import dataclass
from pathlib import Path
from typing import Protocol

from . import PipelineError, __version__
from .repo import Repo
from .sources import LockEntry, Source, SourceFile, load_lock, load_sources, write_lock

_CHUNK = 1 << 20
_MD5_LINE = re.compile(r"([0-9a-fA-F]{32})\s+\*?(\S+)")


class Response(Protocol):
    """The part of an HTTP response fetch uses (urllib's response satisfies it)."""

    headers: object

    def read(self, size: int = -1, /) -> bytes: ...


Opener = Callable[[str], AbstractContextManager[Response]]


@contextmanager
def https_open(url: str) -> Iterator[Response]:
    """Opens an HTTPS URL with certificate verification (the default for urllib)."""
    if not url.startswith("https://"):
        raise PipelineError(f"refusing to download over a non-HTTPS URL: {url}")
    request = urllib.request.Request(url, headers={"User-Agent": f"usim-pipeline/{__version__}"})
    try:
        with urllib.request.urlopen(request, timeout=120) as response:
            yield response
    except OSError as error:  # URLError and socket errors
        raise PipelineError(f"download of {url} failed: {error}") from None


@dataclass(frozen=True)
class Digests:
    size: int
    sha256: str
    md5: str


def file_digests(path: Path) -> Digests:
    """Size, SHA-256 and MD5 of a file."""
    sha256 = hashlib.sha256()
    md5 = hashlib.md5(usedforsecurity=False)
    size = 0
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(_CHUNK), b""):
            sha256.update(chunk)
            md5.update(chunk)
            size += len(chunk)
    return Digests(size, sha256.hexdigest(), md5.hexdigest())


def parse_checksum_list(text: str) -> dict[str, str]:
    """Parses an MD5 list in md5sum format ("<md5>  <file name>" per line), as in NAIF aa_checksums.txt."""
    result: dict[str, str] = {}
    for line in text.splitlines():
        match = _MD5_LINE.fullmatch(line.strip())
        if match:
            result[match.group(2)] = match.group(1).lower()
    return result


def _download(url: str, destination: Path, opener: Opener) -> tuple[Path, Digests]:
    """Downloads `url` next to `destination` (as <name>.part) and returns the part file and its digests.

    Fails if the server announced a Content-Length that differs from the bytes received.
    """
    destination.parent.mkdir(parents=True, exist_ok=True)
    part = destination.with_name(destination.name + ".part")
    sha256 = hashlib.sha256()
    md5 = hashlib.md5(usedforsecurity=False)
    size = 0
    try:
        with opener(url) as response, part.open("wb") as out:
            announced = getattr(response, "headers", {}).get("Content-Length")  # type: ignore[union-attr]
            for chunk in iter(lambda: response.read(_CHUNK), b""):
                out.write(chunk)
                sha256.update(chunk)
                md5.update(chunk)
                size += len(chunk)
        if announced is not None and int(announced) != size:
            raise PipelineError(f"{url}: received {size} bytes, the server announced {announced}")
    except BaseException:
        part.unlink(missing_ok=True)
        raise
    return part, Digests(size, sha256.hexdigest(), md5.hexdigest())


def _utc_now() -> str:
    return datetime.datetime.now(datetime.UTC).strftime("%Y-%m-%dT%H:%M:%SZ")


@dataclass
class FetchResult:
    """What fetch did per file: 'verified' (local copy matches the lock), 'downloaded' (matches the lock),
    'pinned' (new lock entry) or 'repinned' (lock entry replaced by --update)."""

    actions: list[tuple[str, str]]
    lock_changed: bool


def _matches(entry: LockEntry, digests: Digests) -> bool:
    return (entry.size, entry.sha256, entry.md5) == (digests.size, digests.sha256, digests.md5)


def fetch(
    repo: Repo,
    *,
    update: bool = False,
    opener: Opener = https_open,
    now: Callable[[], str] = _utc_now,
    log: Callable[[str], None] = print,
) -> FetchResult:
    """Downloads every file declared in sources.json into data/raw and pins it in sources.lock.json.

    With an existing lock entry the download must match it exactly (the lock is never changed silently);
    `update=True` re-pins files instead and drops lock entries no longer declared. New files are checked
    against the publisher's MD5 list when the source declares one.
    """
    sources = load_sources(repo.sources_file)
    lock = load_lock(repo.lock_file)
    declared = {(source.id, file.name): (source, file) for source in sources for file in source.files}

    stale = sorted(set(lock) - set(declared))
    if stale and not update:
        names = ", ".join(f"{s}/{n}" for s, n in stale)
        raise PipelineError(f"the lock file pins files sources.json no longer declares ({names}); run fetch --update")

    checksum_lists: dict[str, dict[str, str]] = {}
    actions: list[tuple[str, str]] = []
    new_lock = {key: lock[key] for key in lock if key in declared}
    changed = bool(stale)

    for key in sorted(declared):
        source, file = declared[key]
        destination = repo.raw / file.path
        label = f"{source.id}/{file.name}"
        entry = lock.get(key)

        if entry is not None and not update:
            if (entry.url, entry.path) != (file.url, file.path):
                raise PipelineError(f"{label}: URL or path changed in sources.json; run fetch --update to re-pin")
            if destination.exists():
                if _matches(entry, file_digests(destination)):
                    actions.append((label, "verified"))
                    log(f"verified   {label}")
                    continue
                log(f"mismatch   {label}: the local copy differs from the lock file, downloading again")
            part, digests = _download(file.url, destination, opener)
            if not _matches(entry, digests):
                part.unlink(missing_ok=True)
                raise PipelineError(
                    f"{label}: the downloaded file (size {digests.size}, sha256 {digests.sha256}) does not match "
                    f"the lock file (size {entry.size}, sha256 {entry.sha256}). The publisher may have replaced "
                    "it; review the change, then run fetch --update to re-pin."
                )
            os.replace(part, destination)
            actions.append((label, "downloaded"))
            log(f"downloaded {label} ({digests.size} bytes, matches the lock file)")
            continue

        part, digests = _download(file.url, destination, opener)
        publisher_md5 = None
        if file.publisher_checksums is not None:
            if file.publisher_checksums not in checksum_lists:
                checksum_lists[file.publisher_checksums] = _read_checksum_list(file.publisher_checksums, opener)
            publisher_md5 = checksum_lists[file.publisher_checksums].get(file.name)
            if publisher_md5 is None:
                part.unlink(missing_ok=True)
                raise PipelineError(f"{label}: not listed in {file.publisher_checksums}")
            if publisher_md5 != digests.md5:
                part.unlink(missing_ok=True)
                raise PipelineError(
                    f"{label}: MD5 {digests.md5} does not match the publisher's {publisher_md5} "
                    f"({file.publisher_checksums})"
                )
        os.replace(part, destination)
        new_entry = LockEntry(
            source_id=source.id,
            name=file.name,
            path=file.path,
            url=file.url,
            size=digests.size,
            sha256=digests.sha256,
            md5=digests.md5,
            publisher_md5=publisher_md5,
            publisher_checksums=file.publisher_checksums,
            retrieved_utc=now(),
        )
        action = "pinned" if entry is None else "repinned"
        if entry is not None and _matches(entry, digests) and (entry.url, entry.path) == (file.url, file.path):
            # --update of an unchanged file keeps the original retrieval record.
            new_entry = entry
            action = "verified"
        new_lock[key] = new_entry
        changed = changed or new_entry != entry
        actions.append((label, action))
        checked = " (publisher MD5 verified)" if publisher_md5 else " (no publisher checksum list)"
        log(f"{action:<10} {label} ({digests.size} bytes, sha256 {digests.sha256}){checked}")

    if changed:
        write_lock(repo.lock_file, new_lock)
        log(f"wrote {repo.lock_file}")
    return FetchResult(actions, changed)


def _read_checksum_list(url: str, opener: Opener) -> dict[str, str]:
    with opener(url) as response:
        text = response.read().decode("utf-8", errors="replace")
    listing = parse_checksum_list(text)
    if not listing:
        raise PipelineError(f"{url}: no MD5 entries found")
    return listing


def verify(repo: Repo, log: Callable[[str], None] = print) -> list[str]:
    """Checks every declared file in data/raw against the lock file. Returns the problems found."""
    sources = load_sources(repo.sources_file)
    lock = load_lock(repo.lock_file)
    problems: list[str] = []
    for source in sources:
        for file in source.files:
            label = f"{source.id}/{file.name}"
            entry = lock.get((source.id, file.name))
            path = repo.raw / file.path
            if entry is None:
                problems.append(f"{label}: not pinned in the lock file (run fetch)")
            elif (entry.url, entry.path) != (file.url, file.path):
                problems.append(f"{label}: sources.json and the lock file disagree on URL or path")
            elif not path.exists():
                problems.append(f"{label}: {path} is missing (run fetch)")
            elif not _matches(entry, file_digests(path)):
                problems.append(f"{label}: {path} does not match the lock file")
            else:
                log(f"ok         {label}")
    for problem in problems:
        log(f"FAILED     {problem}")
    return problems


def locked_file(repo: Repo, sources: tuple[Source, ...], source_id: str, name: str) -> tuple[Path, LockEntry]:
    """The verified local path and lock entry of a declared file. Fails if it is missing or modified."""
    lock = load_lock(repo.lock_file)
    entry = lock.get((source_id, name))
    declared: SourceFile | None = next(
        (f for s in sources if s.id == source_id for f in s.files if f.name == name), None
    )
    if declared is None:
        raise PipelineError(f"{source_id}/{name} is not declared in sources.json")
    if entry is None:
        raise PipelineError(f"{source_id}/{name} is not pinned in the lock file (run fetch)")
    path = repo.raw / entry.path
    if not path.exists() or not _matches(entry, file_digests(path)):
        raise PipelineError(f"{path} is missing or does not match the lock file (run fetch)")
    return path, entry
