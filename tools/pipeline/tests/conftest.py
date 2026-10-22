"""Shared fixtures. Tests use synthetic data only; tests that need the real kernels skip without them."""

from __future__ import annotations

import io
import json
from collections.abc import Iterator
from contextlib import contextmanager
from pathlib import Path

import pytest

from usim_pipeline import PipelineError
from usim_pipeline.repo import Repo

REAL_REPO = Repo.default()


class FakeResponse:
    def __init__(self, data: bytes, announced: int | None) -> None:
        self._stream = io.BytesIO(data)
        self.headers = {} if announced is None else {"Content-Length": str(announced)}

    def read(self, size: int = -1, /) -> bytes:
        return self._stream.read(size)


class FakeServer:
    """Serves bytes by URL; `announced` overrides the Content-Length header of a URL."""

    def __init__(self) -> None:
        self.files: dict[str, bytes] = {}
        self.announced: dict[str, int] = {}
        self.requests: list[str] = []

    @contextmanager
    def open(self, url: str) -> Iterator[FakeResponse]:
        self.requests.append(url)
        if url not in self.files:
            raise PipelineError(f"download of {url} failed: 404")
        data = self.files[url]
        yield FakeResponse(data, self.announced.get(url, len(data)))


def write_sources(repo: Repo, files: list[dict], extra_sources: list[dict] | None = None) -> None:
    document = {
        "schema_version": 1,
        "sources": [
            {
                "id": "S-TEST-A",
                "title": "Synthetic source A",
                "provider": "Test provider",
                "tier": 1,
                "url": "https://example.invalid/a/",
                "citation": "Citation A",
                "license": "Terms A",
                "files": files,
            },
            *(extra_sources or []),
        ],
    }
    repo.sources_file.parent.mkdir(parents=True, exist_ok=True)
    repo.sources_file.write_text(json.dumps(document), encoding="utf-8")


@pytest.fixture
def repo(tmp_path: Path) -> Repo:
    return Repo(tmp_path)


@pytest.fixture
def server() -> FakeServer:
    return FakeServer()


def real_kernels_available() -> bool:
    """The inputs of the default pack: the kernels and the star catalog."""
    naif = all((REAL_REPO.raw / "naif" / name).exists() for name in ("de440s.bsp", "pck00011.tpc", "gm_de440.tpc", "naif0012.tls"))
    return naif and all((REAL_REPO.raw / "cds" / "V50" / name).exists() for name in ("ReadMe", "catalog.gz"))


SATELLITE_KERNELS = ("mar099.bsp", "jup365.bsp", "sat441.bsp", "ura184_part-3.bsp", "nep097.bsp", "plu060.bsp")


def satellite_kernels_available() -> bool:
    """The optional inputs of the moons pack (usim-pipeline fetch --optional)."""
    return all((REAL_REPO.raw / "naif" / name).exists() for name in SATELLITE_KERNELS)


needs_kernels = pytest.mark.skipif(not real_kernels_available(), reason="real kernels not fetched (run usim-pipeline fetch)")
needs_satellite_kernels = pytest.mark.skipif(
    not (real_kernels_available() and satellite_kernels_available()),
    reason="satellite kernels not fetched (run usim-pipeline fetch --optional)",
)
