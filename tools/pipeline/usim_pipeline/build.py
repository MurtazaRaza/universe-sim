"""The build command: compile verified sources into data packs (data/catalog/packs.json)."""

from __future__ import annotations

import hashlib
import os
from collections.abc import Callable
from dataclasses import dataclass
from pathlib import Path

from . import PipelineError, __version__
from .catalog import apply_manual_entries, build_bodies, load_selection, read_gms, read_shapes_and_rotations
from .ephemeris import to_pack_segment
from .fetch import locked_file
from .jsonio import read_json
from .kernels import check_environment, read_spk_segments, toolkit_versions
from .manual import load_manual_entries
from .pack.binary import write_pack
from .pack.model import PackContent, PackFile, PackSource
from .repo import Repo
from .sources import Source, find_source, load_lock, load_sources, lock_digest
from .tdb import format_tdb, parse_tdb
from .timedata import read_time_data


@dataclass(frozen=True)
class FileRef:
    source_id: str
    file: str


@dataclass(frozen=True)
class PackDefinition:
    name: str
    version: str
    description: str
    bodies: str
    names: str
    ephemeris: tuple[FileRef, ...]
    shape_and_rotation: FileRef
    gm: FileRef
    time: FileRef
    window: tuple[float, float] | None
    window_text: str

    @property
    def file_name(self) -> str:
        return f"{self.name}-{self.version}.uspack"


def _file_ref(value: object, where: str) -> FileRef:
    if not isinstance(value, dict) or set(value) != {"source_id", "file"}:
        raise PipelineError(f"{where}: expected {{source_id, file}}")
    return FileRef(str(value["source_id"]), str(value["file"]))


def load_pack_definitions(path: Path) -> list[PackDefinition]:
    document = read_json(path)
    if not isinstance(document, dict) or document.get("schema_version") != 1:
        raise PipelineError(f"{path}: expected an object with schema_version 1")
    result = []
    for index, entry in enumerate(document.get("packs", [])):
        where = f"{path.name}: packs[{index}]"
        keys = {"name", "version", "description", "bodies", "names", "ephemeris", "shape_and_rotation", "gm", "time", "window"}
        if not isinstance(entry, dict) or set(entry) != keys:
            raise PipelineError(f"{where}: needs exactly {sorted(keys)}")
        window = entry["window"]
        if window is None:
            span, text = None, "full coverage of the ephemeris files"
        else:
            if not isinstance(window, dict) or set(window) != {"start", "end"}:
                raise PipelineError(f"{where}: window must be null or {{start, end}}")
            span = (parse_tdb(window["start"]), parse_tdb(window["end"]))
            if not span[0] < span[1]:
                raise PipelineError(f"{where}: window start must be before its end")
            text = f"{window['start']} to {window['end']}"
        result.append(
            PackDefinition(
                name=str(entry["name"]), version=str(entry["version"]), description=str(entry["description"]),
                bodies=str(entry["bodies"]), names=str(entry["names"]),
                ephemeris=tuple(_file_ref(e, where) for e in entry["ephemeris"]),
                shape_and_rotation=_file_ref(entry["shape_and_rotation"], where), gm=_file_ref(entry["gm"], where),
                time=_file_ref(entry["time"], where), window=span, window_text=text,
            )
        )  # fmt: skip
    names = [d.name for d in result]
    if len(set(names)) != len(names):
        raise PipelineError(f"{path}: duplicate pack name")
    return result


def assemble(repo: Repo, definition: PackDefinition, sources: tuple[Source, ...], log: Callable[[str], None]) -> PackContent:
    """Reads every input of one pack definition and returns the pack content."""
    lock = load_lock(repo.lock_file)
    used_files: list[FileRef] = [*definition.ephemeris, definition.shape_and_rotation, definition.gm, definition.time]
    paths = {}
    for ref in used_files:
        paths[(ref.source_id, ref.file)], _ = locked_file(repo, sources, ref.source_id, ref.file)

    selection = load_selection(repo.catalog / definition.bodies)
    bodies = build_bodies(selection)
    shape = definition.shape_and_rotation
    quantities, rotations = read_shapes_and_rotations(selection, paths[(shape.source_id, shape.file)], shape.source_id, log)
    quantities += read_gms(selection, paths[(definition.gm.source_id, definition.gm.file)], definition.gm.source_id, log)
    quantities = apply_manual_entries(selection, quantities, load_manual_entries(repo.catalog, sources))
    time = read_time_data(paths[(definition.time.source_id, definition.time.file)], definition.time.source_id, log)

    selected = {b.id for b in bodies}
    segments = []
    for ref in definition.ephemeris:
        for spk_segment in read_spk_segments(paths[(ref.source_id, ref.file)]):
            segment = to_pack_segment(spk_segment, ref.source_id, definition.window)
            if segment is None:
                log(f"  skipped {spk_segment.target} wrt {spk_segment.center}: no records in the window")
                continue
            if segment.target not in selected:
                continue
            if segment.center not in selected:
                raise PipelineError(f"{ref.file}: center {spk_segment.center} of {spk_segment.target} is not selected")
            segments.append(segment)
    covered = {s.target for s in segments} | {s.center for s in segments}
    uncovered = [b for b in bodies if b.id not in covered]
    for b in uncovered:
        log(f"  note: {b.name} (naif:{b.id[1]}) has no ephemeris segment in this pack")

    cited = {definition.names, *(r.source_id for r in used_files), *(q.source_id for q in quantities)}
    pack_sources = [
        PackSource(s.id, s.title, s.provider, s.url, s.citation, s.license, s.tier)
        for s in sorted((find_source(sources, sid) for sid in cited), key=lambda s: s.id)
    ]
    files = []
    for ref in sorted(set(used_files), key=lambda r: (r.source_id, r.file)):
        entry = lock[(ref.source_id, ref.file)]
        files.append(
            PackFile(
                source_id=entry.source_id, name=entry.name, url=entry.url, retrieved_utc=entry.retrieved_utc,
                size=entry.size, sha256=entry.sha256, md5=entry.md5,
                publisher_checksum_verified=entry.publisher_md5 is not None and entry.publisher_md5 == entry.md5,
            )
        )  # fmt: skip
    tools = toolkit_versions()
    metadata = {
        "pack.name": definition.name,
        "pack.version": definition.version,
        "pack.description": definition.description,
        "pack.window": definition.window_text,
        "pipeline.version": __version__,
        "pipeline.tools": f"{tools['toolkit']} through SpiceyPy {tools['spiceypy']} (numpy {tools['numpy']})",
    }
    return PackContent(
        metadata=metadata, lock_sha256=lock_digest(repo.lock_file), sources=pack_sources, files=files,
        bodies=bodies, quantities=quantities, rotations=rotations, time=time, segments=segments,
    )  # fmt: skip


def build(
    repo: Repo, names: list[str] | None = None, output_dir: Path | None = None, log: Callable[[str], None] = print
) -> list[Path]:
    """Builds the packs in data/catalog/packs.json (or only `names`) into data/packs (or `output_dir`)."""
    check_environment()
    sources = load_sources(repo.sources_file)
    definitions = load_pack_definitions(repo.catalog / "packs.json")
    if names:
        unknown = set(names) - {d.name for d in definitions}
        if unknown:
            raise PipelineError(f"unknown pack(s) {sorted(unknown)}")
        definitions = [d for d in definitions if d.name in names]
    out_dir = output_dir or repo.packs
    out_dir.mkdir(parents=True, exist_ok=True)
    written = []
    for definition in definitions:
        log(f"building {definition.file_name}")
        content = assemble(repo, definition, sources, log)
        data = write_pack(content)
        path = out_dir / definition.file_name
        part = path.with_name(path.name + ".part")
        part.write_bytes(data)
        os.replace(part, path)
        coverage = [s for s in content.segments]
        span = (
            f"{format_tdb(min(s.start_s for s in coverage))} to {format_tdb(max(s.end_s for s in coverage))}"
            if coverage
            else "none"
        )
        log(
            f"wrote {path} ({len(data)} bytes, sha256 {hashlib.sha256(data).hexdigest()}): {len(content.bodies)} bodies, "
            f"{len(content.quantities)} quantities, {len(content.rotations)} rotation models, "
            f"{len(content.segments)} segments, coverage {span}"
        )
        written.append(path)
    return written
