"""Checks on the real kernels and the committed outputs (skipped until `usim-pipeline fetch` has run).

- P2-08: building twice from the same lock file gives identical bytes, and the committed default pack is
  what the current pipeline builds.
- P2-03: the pack's ephemeris records equal the raw bytes of the SPK file (read without SpiceyPy), and its
  lunar orientation records those of the binary PCK (P6-05); so do the moons pack's Earth orientation records
  (P6-06, read for its window without building that pack).
- P2-09 / P2-10: the committed fixtures are what the current generators produce.
"""

from __future__ import annotations

import math
import re
from pathlib import Path

import pytest

from usim_pipeline.build import build, load_pack_definitions
from usim_pipeline.kernels import TextKernel, read_pck_segments, read_spk_segments
from usim_pipeline.orientation import read_orientation
from usim_pipeline.pack.binary import read_pack
from usim_pipeline.pack.model import Frame, PackOrientationSegment
from usim_pipeline.reference import test_pack, unit_fixtures

from .conftest import REAL_REPO, needs_earth_pck, needs_kernels


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
def test_orientation_records_equal_the_raw_pck_bytes(built):
    _, content = read_pack(built[0].read_bytes())
    assert [o.body for o in content.orientations] == [(1, 301)]
    model = content.orientations[0]
    frame_kernel = TextKernel(REAL_REPO.raw / "naif" / "moon_de440_250416.tf")
    pck_frame = int((frame_kernel.text(f"FRAME_{model.pck_frame_name}") or ["0"])[0])
    class_id = int((frame_kernel.text(f"FRAME_{pck_frame}_CLASS_ID") or ["0"])[0])
    _assert_raw_records(model.segments, REAL_REPO.raw / "naif" / "moon_pa_de440_200625.bpc", class_id)
    # The orientation covers the ephemeris span, and no more than one record beyond it at each end.
    start = min(s.start_s for s in content.segments)
    end = max(s.end_s for s in content.segments)
    assert model.segments[0].start_s <= start and model.segments[-1].end_s >= end
    assert model.segments[0].init_s > start - model.segments[0].interval_s


def _assert_raw_records(segments: tuple[PackOrientationSegment, ...], pck: Path, class_id: int) -> None:
    """The segments' records are the bytes of the binary PCK's segments named in their locators."""
    raw = pck.read_bytes()
    assert raw[88:96] == b"LTL-IEEE"
    by_index = {s.index: s for s in read_pck_segments(pck, class_id)}
    for segment in segments:
        index = int(segment.locator.split(" segment ")[1].split(" ")[0])
        source = by_index[index]
        # The pack keeps the records overlapping its span: a contiguous run starting at some record.
        first = round((segment.init_s - source.init_s) / source.interval_s)
        start = (source.first_address - 1 + first * source.record_size) * 8
        expected = raw[start : start + segment.record_count * segment.record_size * 8]
        assert segment.records.astype("<f8").tobytes() == expected


@needs_earth_pck
def test_the_moons_pack_earth_orientation_records_equal_the_raw_pck_bytes():
    moons = next(d for d in load_pack_definitions(REAL_REPO.catalog / "packs.json") if d.name == "solar-system-moons")
    definition = next(o for o in moons.orientation if o.body == 399)
    assert moons.window is not None and definition.frame_kernel_file is None
    pck = REAL_REPO.raw / "naif" / definition.pck_file
    model = read_orientation(definition, pck, None, moons.window, quiet)
    assert (model.frame_name, model.pck_frame_name, model.offset_source_id) == ("ITRF93", "ITRF93", None)
    assert {s.base_frame for s in model.segments} == {Frame.ECLIPJ2000}
    _assert_raw_records(model.segments, pck, 3000)
    # Contiguous segments covering the window, with no more than one record beyond it at each end.
    assert all(a.end_s == b.start_s for a, b in zip(model.segments, model.segments[1:]))
    first, last = model.segments[0], model.segments[-1]
    assert first.start_s <= moons.window[0] and last.end_s >= moons.window[1]
    assert first.init_s > moons.window[0] - first.interval_s
    assert last.init_s + (last.record_count - 1) * last.interval_s < moons.window[1]


_NUMBER = re.compile(r"-?(?:\d+\.\d*|\.\d+|\d+)(?:[eE][-+]?\d+)?")

# CSPICE's results differ in their last bits between platforms. Measured 2026-11-02, Windows x64 against the
# fixtures generated on macOS arm64 (SpiceyPy 8.2.0): 10 of the file's numbers differ, 9 by 1 to 2 ulp of the
# largest number on their line and one near-zero residual (TDB from TT at J2000: 2.75e-21 s against 0.0), far
# below an ulp of the inputs on its line. Text and structure must still match exactly.
_FIXTURE_ULPS = 4


def _fixture_differences(generated: str, committed: str, ulps: int = _FIXTURE_ULPS) -> list[str]:
    """Where two generated fixture files differ beyond `ulps` units in the last place of the largest number on
    each line; the text between the numbers must be identical."""
    a_lines, b_lines = generated.splitlines(), committed.splitlines()
    if len(a_lines) != len(b_lines):
        return [f"{len(a_lines)} lines generated, {len(b_lines)} committed"]
    problems = []
    for number, (a, b) in enumerate(zip(a_lines, b_lines), start=1):
        if a == b:
            continue
        if _NUMBER.split(a) != _NUMBER.split(b):
            problems.append(f"line {number}: the text differs: {a!r} != {b!r}")
            continue
        xs = [float(x) for x in _NUMBER.findall(a)]
        ys = [float(y) for y in _NUMBER.findall(b)]
        tolerance = ulps * math.ulp(max(abs(y) for y in ys))
        problems += [f"line {number}: {x!r} != {y!r}" for x, y in zip(xs, ys) if abs(x - y) > tolerance]
    return problems


@needs_kernels
def test_committed_unit_fixtures_are_current(tmp_path):
    out = unit_fixtures.generate(REAL_REPO, tmp_path / "spice_reference.hpp", log=quiet)
    assert _fixture_differences(out.read_text(), (REAL_REPO.core_fixtures / "spice_reference.hpp").read_text()) == []


def test_fixture_comparison_allows_only_last_bit_differences():
    committed = "inline constexpr Case k[] = {\n    {32.18392726322383, 2.7542962514259453e-21},\n};\n"
    assert _fixture_differences(committed.replace("2.7542962514259453e-21", "0.0"), committed) == []
    assert _fixture_differences(committed.replace("32.18392726322383", "32.18392726322384"), committed) == []
    assert _fixture_differences(committed.replace("32.18392726322383", "32.1839272632239"), committed) != []
    assert _fixture_differences(committed.replace("k[]", "j[]"), committed) != []
    assert _fixture_differences(committed + "\n", committed) != []


def test_committed_test_pack_is_current(tmp_path):
    test_pack.generate(REAL_REPO, out_dir=tmp_path, log=quiet)
    for name in (test_pack.FILE_NAME, test_pack.HEADER_NAME):
        committed = REAL_REPO.core_fixtures / name
        if committed.read_bytes().startswith(b"version https://git-lfs"):
            pytest.skip("test pack is a Git LFS pointer (run git lfs pull)")
        assert (tmp_path / name).read_bytes() == committed.read_bytes(), name
