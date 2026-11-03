"""Artistic assets of the Unity viewer (A-04, ADR-0015 decision 6).

data/catalog/artistic-assets.json lists the original or licensed art the viewer uses: effect textures, artist's
impressions, icons, UI art and models, each under UniverseSim/Assets/UniverseSim/Art/ with its author, origin and
license. An artist's impression also names its body and the references it is based on (SPEC FR-REN-14). The list
becomes the last section of ATTRIBUTION.md, after the surface maps that `textures` writes; `attribution` rewrites that
section alone, so adding an asset needs none of the maps' large sources.
"""

from __future__ import annotations

from collections.abc import Callable
from dataclasses import dataclass
from pathlib import Path, PurePosixPath
from typing import Any

from . import PipelineError
from .jsonio import read_json
from .repo import Repo

SCHEMA_VERSION = 1
CATALOG_NAME = "artistic-assets.json"
ASSET_ROOT = "UniverseSim/Assets/UniverseSim"
ART_FOLDER = "Art"
SECTION_HEADING = "## Artistic assets"

# The kinds of asset and how ATTRIBUTION.md names them.
KINDS = {
    "effect_texture": "effect texture",
    "artist_impression": "artist's impression",
    "icon": "icon",
    "ui_art": "UI art",
    "model": "model",
}
ORIGINS = ("original", "licensed")

_REQUIRED = ("file", "kind", "title", "author", "origin", "license")
_OPTIONAL = ("license_url", "body", "body_name", "references", "notes")


@dataclass(frozen=True)
class Reference:
    """What an artist's impression is based on: an image or a description, with its credit."""

    title: str
    url: str
    credit: str


@dataclass(frozen=True)
class ArtisticAsset:
    file: str  # relative to UniverseSim/Assets/UniverseSim/, under Art/
    kind: str
    title: str
    author: str
    origin: str
    license: str
    license_url: str
    body: int | None
    body_name: str
    references: tuple[Reference, ...]
    notes: str


def _text(entry: dict, key: str, where: str, required: bool) -> str:
    value = entry.get(key, "")
    if not isinstance(value, str) or (required and not value.strip()):
        raise PipelineError(f"{where}: '{key}' {'is required and ' if required else ''}must be non-empty text")
    return value


def parse_artistic_assets(document: Any, where: str = CATALOG_NAME) -> tuple[ArtisticAsset, ...]:
    """Validates a parsed artistic-assets.json document (not whether the files exist; see check_files)."""
    if not isinstance(document, dict) or document.get("schema_version") != SCHEMA_VERSION:
        raise PipelineError(f"{where}: expected an object with schema_version {SCHEMA_VERSION}")
    unknown = set(document) - {"schema_version", "description", "assets"}
    if unknown:
        raise PipelineError(f"{where}: unknown keys {sorted(unknown)}")
    entries = document.get("assets")
    if not isinstance(entries, list):
        raise PipelineError(f"{where}: 'assets' must be a list")
    assets: list[ArtisticAsset] = []
    seen: set[str] = set()
    for index, entry in enumerate(entries):
        at = f"{where}: assets[{index}]"
        if not isinstance(entry, dict):
            raise PipelineError(f"{at}: must be an object")
        unknown = set(entry) - {*_REQUIRED, *_OPTIONAL}
        if unknown:
            raise PipelineError(f"{at}: unknown keys {sorted(unknown)}")
        for key in _REQUIRED:
            _text(entry, key, at, required=True)
        path = PurePosixPath(entry["file"])
        if path.is_absolute() or ".." in path.parts or "\\" in entry["file"] or path.parts[:1] != (ART_FOLDER,):
            raise PipelineError(f"{at}: 'file' must be a path under {ART_FOLDER}/ (relative to {ASSET_ROOT}/)")
        if entry["file"] in seen:
            raise PipelineError(f"{at}: {entry['file']} is listed twice")
        seen.add(entry["file"])
        if entry["kind"] not in KINDS:
            raise PipelineError(f"{at}: unknown kind {entry['kind']!r} (known: {', '.join(KINDS)})")
        if entry["origin"] not in ORIGINS:
            raise PipelineError(f"{at}: 'origin' must be one of {', '.join(ORIGINS)}")
        license_url = _text(entry, "license_url", at, required=False)
        if license_url and not license_url.startswith("https://"):
            raise PipelineError(f"{at}: license_url must use https")
        body = entry.get("body")
        if body is not None and (isinstance(body, bool) or not isinstance(body, int)):
            raise PipelineError(f"{at}: 'body' must be a NAIF ID")
        body_name = _text(entry, "body_name", at, required=False)
        references_in = entry.get("references", [])
        if not isinstance(references_in, list):
            raise PipelineError(f"{at}: 'references' must be a list")
        references = []
        for r_index, item in enumerate(references_in):
            r_at = f"{at}: references[{r_index}]"
            if not isinstance(item, dict) or set(item) - {"title", "url", "credit"}:
                raise PipelineError(f"{r_at}: must be an object with title, url and credit")
            for key in ("title", "url", "credit"):
                _text(item, key, r_at, required=True)
            if not item["url"].startswith("https://"):
                raise PipelineError(f"{r_at}: url must use https")
            references.append(Reference(item["title"], item["url"], item["credit"]))
        if entry["kind"] == "artist_impression":
            # FR-REN-14: an impression names its body and lists what it is based on.
            if body is None or not body_name:
                raise PipelineError(f"{at}: an artist's impression needs 'body' and 'body_name'")
            if not references:
                raise PipelineError(f"{at}: an artist's impression needs its references")
        assets.append(
            ArtisticAsset(
                file=entry["file"],
                kind=entry["kind"],
                title=entry["title"],
                author=entry["author"],
                origin=entry["origin"],
                license=entry["license"],
                license_url=license_url,
                body=body,
                body_name=body_name,
                references=tuple(references),
                notes=_text(entry, "notes", at, required=False),
            )
        )
    return tuple(sorted(assets, key=lambda a: a.file))


def check_files(repo: Repo, assets: tuple[ArtisticAsset, ...]) -> None:
    """Every listed file exists, and every file under Art/ (other than Unity's .meta files) is listed."""
    root = repo.root / ASSET_ROOT
    listed = {a.file for a in assets}
    for asset in assets:
        if not (root / asset.file).is_file():
            raise PipelineError(f"{CATALOG_NAME}: {asset.file} does not exist under {ASSET_ROOT}/")
    art = root / ART_FOLDER
    if art.is_dir():
        for path in sorted(art.rglob("*")):
            if path.is_file() and path.suffix != ".meta":
                relative = path.relative_to(root).as_posix()
                if relative not in listed:
                    raise PipelineError(f"{ASSET_ROOT}/{relative} is not listed in data/catalog/{CATALOG_NAME}")


def load_artistic_assets(repo: Repo) -> tuple[ArtisticAsset, ...]:
    """Reads and validates the catalog and checks it against the files."""
    assets = parse_artistic_assets(read_json(repo.catalog / CATALOG_NAME))
    check_files(repo, assets)
    return assets


def attribution_section(assets: tuple[ArtisticAsset, ...]) -> list[str]:
    """The last section of ATTRIBUTION.md, as lines."""
    lines = [
        SECTION_HEADING,
        "",
        f"Original or licensed art (ADR-0015 decision 6), listed in `data/catalog/{CATALOG_NAME}`; each file is under "
        f"`{ART_FOLDER}/`.",
        "An artist's impression is labeled as such in the viewer and drawn only in the stylized style (FR-REN-14).",
        "",
    ]
    if not assets:
        lines += ["None yet: every effect, icon, cursor and model is drawn by code.", ""]
        return lines
    for a in assets:
        kind = KINDS[a.kind] + (f" of {a.body_name} (NAIF ID {a.body})" if a.kind == "artist_impression" else "")
        license_text = a.license + (f" ({a.license_url})" if a.license_url else "")
        lines += [
            f"### {a.title} (`{a.file}`)",
            "",
            f"- Kind: {kind}.",
            f"- Author: {a.author}.",
            f"- Origin: {a.origin}.",
            f"- License: {license_text}.",
            *(f"- Reference: {r.title}, {r.credit}, {r.url}" for r in a.references),
            *([f"- Notes: {a.notes}"] if a.notes else []),
            "",
        ]
    return lines


def splice_attribution(text: str, section: list[str]) -> str:
    """ATTRIBUTION.md with its artistic section (the last one) replaced, or appended if it has none."""
    marker = "\n" + SECTION_HEADING + "\n"
    head = text.split(marker, 1)[0] if marker in text else text.rstrip("\n") + "\n"
    return head.rstrip("\n") + "\n\n" + "\n".join(section)


def write_attribution(repo: Repo, attribution_path: str, log: Callable[[str], None] = print) -> Path:
    """Rewrites the artistic section of ATTRIBUTION.md from the catalog, keeping the surface maps above it."""
    path = repo.root / attribution_path
    if not path.is_file():
        raise PipelineError(f"{path} does not exist: run usim-pipeline textures first")
    assets = load_artistic_assets(repo)
    path.write_text(splice_attribution(path.read_text(encoding="utf-8"), attribution_section(assets)), encoding="utf-8")
    log(f"wrote the artistic assets of {path} ({len(assets)} assets)")
    return path
