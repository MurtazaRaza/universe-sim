"""Checks on the real kernels and the committed outputs (skipped until `usim-pipeline fetch` has run).

- P2-08: building twice from the same lock file gives identical bytes, and the committed default pack is
  what the current pipeline builds.
- P2-03: the pack's ephemeris records equal the raw bytes of the SPK file (read without SpiceyPy).
- P2-09 / P2-10: the committed fixtures are what the current generators produce.
"""

from __future__ import annotations

from pathlib import Path

import pytest

from usim_pipeline.build import build, load_pack_definitions
from usim_pipeline.kernels import read_spk_segments
from usim_pipeline.pack.binary import read_pack
from usim_pipeline.reference import test_pack, unit_fixtures

from .conftest import REAL_REPO, needs_kernels


def quiet(_: str) -> None:
    pass


@pytest.fixture(scope="module")
def built(tmp_path_factory) -> tuple[Path, Path]:
    first = build(REAL_REPO, output_dir=tmp_path_factory.mktemp("first"), log=quiet)[0]
    second = build(REAL_REPO, output_dir=tmp_path_factory.mktemp("second"), log=quiet)[0]
    return first, second


@needs_kernels
def test_build_is_reproducible(built):
    first, second = built
    assert first.read_bytes() == second.read_bytes()


@needs_kernels
def test_committed_default_pack_is_current(built):
    definition = next(d for d in load_pack_definitions(REAL_REPO.catalog / "packs.json") if d.name == "solar-system")
    committed = REAL_REPO.packs / definition.file_name
    if not committed.exists() or committed.read_bytes().startswith(b"version https://git-lfs"):
        pytest.skip("default pack not present (run usim-pipeline build, or git lfs pull)")
    assert committed.read_bytes() == built[0].read_bytes()


@needs_kernels
def test_ephemeris_records_equal_the_raw_spk_bytes(built):
    _, content = read_pack(built[0].read_bytes())
    spk = REAL_REPO.raw / "naif" / "de440s.bsp"
    raw = spk.read_bytes()
    assert raw[88:96] == b"LTL-IEEE"  # DAF file record: little-endian IEEE doubles
    by_pair = {(s.target, s.center): s for s in read_spk_segments(spk)}
    assert len(content.segments) == len(by_pair)
    for segment in content.segments:
        source = by_pair[(segment.target[1], segment.center[1])]
        start = (source.first_address - 1) * 8
        expected = raw[start : start + source.record_count * source.record_size * 8]
        assert segment.records.astype("<f8").tobytes() == expected


@needs_kernels
def test_committed_unit_fixtures_are_current(tmp_path):
    out = unit_fixtures.generate(REAL_REPO, tmp_path / "spice_reference.hpp", log=quiet)
    assert out.read_text() == (REAL_REPO.core_fixtures / "spice_reference.hpp").read_text()


def test_committed_test_pack_is_current(tmp_path):
    test_pack.generate(REAL_REPO, out_dir=tmp_path, log=quiet)
    for name in (test_pack.FILE_NAME, test_pack.HEADER_NAME):
        committed = REAL_REPO.core_fixtures / name
        if committed.read_bytes().startswith(b"version https://git-lfs"):
            pytest.skip("test pack is a Git LFS pointer (run git lfs pull)")
        assert (tmp_path / name).read_bytes() == committed.read_bytes(), name
