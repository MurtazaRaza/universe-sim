"""Locations of the pipeline's inputs and outputs inside the repository."""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path


@dataclass(frozen=True)
class Repo:
    """The repository root and the data paths below it. Tests point `root` at a temporary directory."""

    root: Path

    @staticmethod
    def default() -> Repo:
        # tools/pipeline/usim_pipeline/repo.py -> the repository root is three levels above the package.
        return Repo(Path(__file__).resolve().parents[3])

    @property
    def data(self) -> Path:
        return self.root / "data"

    @property
    def sources_file(self) -> Path:
        return self.data / "sources.json"

    @property
    def lock_file(self) -> Path:
        return self.data / "sources.lock.json"

    @property
    def raw(self) -> Path:
        return self.data / "raw"

    @property
    def catalog(self) -> Path:
        return self.data / "catalog"

    @property
    def manual(self) -> Path:
        return self.catalog / "manual"

    @property
    def packs(self) -> Path:
        return self.data / "packs"

    @property
    def reference(self) -> Path:
        return self.data / "reference"

    @property
    def core_fixtures(self) -> Path:
        return self.root / "core" / "tests" / "fixtures"
