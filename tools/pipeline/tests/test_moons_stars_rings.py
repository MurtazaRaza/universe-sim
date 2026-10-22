"""Phase 6 inputs: optional source files and packs, satellite segments read by window and center (P6-01), the
star catalog parser (FR-REN-09) and the ring transcription (P6-04). Synthetic data, except the tests marked
as needing the real kernels."""

from __future__ import annotations

import dataclasses
import gzip
import json
import math

import pytest

from usim_pipeline import PipelineError
from usim_pipeline.build import load_pack_definitions
from usim_pipeline.fetch import fetch, locked_file, verify
from usim_pipeline.kernels import read_spk_segments, record_range
from usim_pipeline.pack.binary import write_pack
from usim_pipeline.pack.model import PackRing, naif
from usim_pipeline.reference.test_pack import synthetic_content
from usim_pipeline.rings import load_rings, parse_optical_depth, parse_radii
from usim_pipeline.sources import load_sources
from usim_pipeline.stars import ARCSEC_TO_RAD, JULIAN_YEAR_S, check_readme, parse_catalog

from .conftest import REAL_REPO, needs_kernels, needs_satellite_kernels, write_sources

URL = "https://example.invalid/a/big.bin"


def quiet(_: str) -> None:
    pass


# ---- Optional files (fetch, verify) ----


def declare_optional(repo) -> None:
    write_sources(repo, [{"name": "big.bin", "path": "test/big.bin", "url": URL, "publisher_checksums": None, "optional": True}])


def test_optional_file_is_skipped_unless_asked(repo, server):
    declare_optional(repo)
    server.files[URL] = b"large data"
    result = fetch(repo, opener=server.open, now=lambda: "2026-01-02T03:04:05Z", log=quiet)
    assert result.actions == [("S-TEST-A/big.bin", "skipped")] and server.requests == []
    result = fetch(repo, optional=True, opener=server.open, now=lambda: "2026-01-02T03:04:05Z", log=quiet)
    assert result.actions == [("S-TEST-A/big.bin", "pinned")]
    # Once pinned, a missing optional file is not a problem for verify or a plain fetch, and the lock keeps it.
    (repo.raw / "test" / "big.bin").unlink()
    before = repo.lock_file.read_bytes()
    assert verify(repo, log=quiet) == []
    assert fetch(repo, opener=server.open, log=quiet).actions == [("S-TEST-A/big.bin", "skipped")]
    assert repo.lock_file.read_bytes() == before
    with pytest.raises(PipelineError, match="fetch --optional"):
        locked_file(repo, load_sources(repo.sources_file), "S-TEST-A", "big.bin")


def test_optional_must_be_boolean(repo):
    write_sources(repo, [{"name": "big.bin", "path": "test/big.bin", "url": URL, "publisher_checksums": None, "optional": "yes"}])
    with pytest.raises(PipelineError, match="optional"):
        load_sources(repo.sources_file)


# ---- Pack definitions ----


def pack_entry(**extra) -> dict:
    ref = {"source_id": "S-TEST-A", "file": "x"}
    entry = {"name": "p", "version": "1.0.0", "description": "d", "bodies": "b.json", "names": "S-TEST-A",
             "ephemeris": [ref], "shape_and_rotation": ref, "gm": ref, "time": ref, "window": None}  # fmt: skip
    entry.update(extra)
    return entry


def test_pack_definitions_read_centers_optional_stars_and_rings(tmp_path):
    path = tmp_path / "packs.json"
    entry = pack_entry(
        optional=True, rings="rings.json", stars={"source_id": "S-TEST-A", "file": "c.gz", "readme": "ReadMe"},
        ephemeris=[{"source_id": "S-TEST-A", "file": "x"}, {"source_id": "S-TEST-A", "file": "s.bsp", "centers": [5]}],
    )  # fmt: skip
    path.write_text(json.dumps({"schema_version": 1, "packs": [entry]}))
    (definition,) = load_pack_definitions(path)
    assert definition.optional and definition.rings == "rings.json"
    assert [r.centers for r in definition.ephemeris] == [None, (5,)]
    assert definition.stars is not None and definition.stars[1].file == "ReadMe"


@pytest.mark.parametrize(
    "extra, message",
    [
        ({"ephemeris": [{"source_id": "S", "file": "x", "centers": []}]}, "centers"),
        ({"gm": {"source_id": "S", "file": "x", "centers": [5]}}, "expected"),
        ({"optional": 1}, "optional"),
        ({"stars": {"source_id": "S", "file": "c.gz"}}, "stars"),
        ({"unknown": True}, "needs exactly"),
    ],
)
def test_pack_definitions_reject(tmp_path, extra, message):
    path = tmp_path / "packs.json"
    path.write_text(json.dumps({"schema_version": 1, "packs": [pack_entry(**extra)]}))
    with pytest.raises(PipelineError, match=message):
        load_pack_definitions(path)


def test_real_pack_definitions():
    definitions = {d.name: d for d in load_pack_definitions(REAL_REPO.catalog / "packs.json")}
    assert not definitions["solar-system"].optional
    moons = definitions["solar-system-moons"]
    assert moons.optional and moons.window is not None
    assert all(ref.centers is not None for ref in moons.ephemeris if ref.source_id == "S-NAIF-SAT")


# ---- Satellite segments by center and window ----


def test_record_range():
    assert record_range(0.0, 10.0, 5, 0.0, 50.0, None) == (0, 4)
    assert record_range(0.0, 10.0, 5, 0.0, 50.0, (15.0, 25.0)) == (1, 2)
    assert record_range(0.0, 10.0, 5, 0.0, 50.0, (50.0, 60.0)) is None


@needs_kernels
def test_windowed_read_equals_a_slice_of_the_full_read():
    spk = REAL_REPO.raw / "naif" / "de440s.bsp"
    window = (0.0, 86400.0 * 400)
    full = {(s.target, s.center): s for s in read_spk_segments(spk)}
    windowed = read_spk_segments(spk, lambda target, center: target == 301, window)
    assert [(s.target, s.center) for s in windowed] == [(301, 3)]
    part = windowed[0]
    whole = full[(301, 3)]
    first = round((part.init_s - whole.init_s) / whole.interval_s)
    assert part.records.tobytes() == whole.records[first : first + part.record_count].tobytes()
    assert part.start_s <= window[0] and part.end_s >= window[1]


@needs_satellite_kernels
def test_satellite_records_equal_the_raw_kernel_bytes():
    spk = REAL_REPO.raw / "naif" / "jup365.bsp"
    window = (0.0, 86400.0 * 30)
    (io,) = read_spk_segments(spk, lambda target, center: (target, center) == (501, 5), window)
    with spk.open("rb") as stream:
        stream.seek((io.first_address - 1) * 8)
        raw = stream.read(io.record_count * io.record_size * 8)
    assert io.records.astype("<f8").tobytes() == raw


# ---- Star catalog ----


def catalog_line(hr: int, name: str, ra: str, dec: str, vmag: str, bv: str, pm_ra: str, pm_de: str) -> str:
    line = [" "] * 197
    def put(first: int, text: str) -> None:
        line[first - 1 : first - 1 + len(text)] = list(text)
    put(1, f"{hr:4d}")
    put(5, name)
    put(76, ra)  # "HHMMSS.S"
    put(84, dec)  # "+DDMMSS"
    put(103, vmag)
    put(110, bv)
    put(149, pm_ra)
    put(155, pm_de)
    return "".join(line).rstrip()


def test_star_catalog_parsing():
    lines = [
        catalog_line(1, "Syn A", "012030.0", "-153000", " 4.50", "+0.25", "+0.100", "-0.200"),
        catalog_line(2, "", "", "", "", "", "", ""),  # a removed object
        catalog_line(3, "Syn B", "230000.0", "+890000", "-1.00", "", "", ""),
    ]
    catalog = parse_catalog("\n".join(lines).encode("ascii"), "S-TEST-A")
    assert [s.id[1] for s in catalog.stars] == [1, 3]
    a, b = catalog.stars
    assert a.designation == "Syn A" and b.designation == "Syn B"
    # 1 h 20 m 30 s = 4830 s of time = 72450 arcsec; -15 deg 30 arcmin = -55800 arcsec.
    assert a.ra_rad == 72450.0 * ARCSEC_TO_RAD and a.dec_rad == -55800.0 * ARCSEC_TO_RAD
    assert a.pm_ra_cosdec_rad_per_s == 0.1 * ARCSEC_TO_RAD / JULIAN_YEAR_S
    assert a.pm_dec_rad_per_s == -0.2 * ARCSEC_TO_RAD / JULIAN_YEAR_S
    assert (a.v_mag, a.b_v) == (4.5, 0.25)
    assert b.b_v is None and b.pm_ra_cosdec_rad_per_s is None and b.v_mag == -1.0
    assert math.isclose(b.dec_rad, math.radians(89.0))


def test_star_catalog_rejects_bad_lines():
    with pytest.raises(PipelineError, match="sign"):
        parse_catalog(catalog_line(1, "", "000000.0", "x000000", " 1.00", "", "", "").encode(), "S-TEST-A")
    with pytest.raises(PipelineError, match="no stars"):
        parse_catalog(b"", "S-TEST-A")


@needs_kernels
def test_the_pinned_readme_describes_the_columns_used(tmp_path):
    readme = REAL_REPO.raw / "cds" / "V50" / "ReadMe"
    check_readme(readme)
    changed = tmp_path / "ReadMe"
    changed.write_text(readme.read_text().replace(" 103-107", " 104-108"))
    with pytest.raises(PipelineError, match="Vmag"):
        check_readme(changed)


@needs_kernels
def test_real_star_catalog_has_the_stars_its_readme_announces():
    from usim_pipeline.stars import read_star_catalog

    catalog = read_star_catalog(REAL_REPO.raw / "cds" / "V50" / "catalog.gz", REAL_REPO.raw / "cds" / "V50" / "ReadMe", "S-CDS-BSC5")
    assert len(catalog.stars) == 9096  # "of which 9096 are stars" (ReadMe)
    with gzip.open(REAL_REPO.raw / "cds" / "V50" / "catalog.gz", "rb") as stream:
        assert len(stream.read().decode("ascii").splitlines()) == 9110


# ---- Rings ----


def test_ring_text_parsing():
    assert parse_radii("74,490 - 91,980", None) == (74490.0, 91980.0, "")
    assert parse_radii("88,690-88,720", None)[:2] == (88690.0, 88720.0)
    inner, outer, derivation = parse_radii("133,570", "325")
    assert (inner, outer) == (133407.5, 133732.5) and "half the width" in derivation
    assert parse_optical_depth("0.05 to 0.35") == (0.05, 0.35)
    assert parse_optical_depth("10^-5 to 10^-3") == (1e-5, 1e-3)
    for bad in ("~0", "0.5", "1 - 2"):
        with pytest.raises(PipelineError):
            parse_optical_depth(bad)
    with pytest.raises(PipelineError):
        parse_radii("285-440 km", None)


def ring_file(tmp_path, **changes) -> object:
    reference = {"source_id": "S-TEST-A", "url": "https://example.invalid/r", "accessed": "2026-10-22", "table": "t"}
    entry = {"naif_id": 1001, "name": "R", "gap": False, "radii": {"text": "10 - 20", **reference},
             "optical_depth": {"text": "0.1 to 0.2", **reference}, "transcriber_note": "synthetic"}  # fmt: skip
    entry.update(changes)
    path = tmp_path / "rings.json"
    path.write_text(json.dumps({"schema_version": 1, "rings": [entry]}))
    return path


def test_rings_load_with_locators(tmp_path, repo):
    write_sources(repo, [])
    sources = load_sources(repo.sources_file)
    (ring,) = load_rings(ring_file(tmp_path), sources, {1001})
    assert (ring.inner_km, ring.outer_km, ring.optical_depth) == (10.0, 20.0, (0.1, 0.2))
    assert '"10 - 20"' in ring.radii_locator and "accessed 2026-10-22" in ring.radii_locator
    assert load_rings(ring_file(tmp_path), sources, {999}) == []


@pytest.mark.parametrize(
    "changes, message",
    [
        ({"gap": True}, "gap has no optical depth"),
        ({"radii": {"text": "10 - 20", "source_id": "S-NOPE", "url": "https://x", "accessed": "2026-10-22", "table": "t"}}, "not declared"),
        ({"radii": {"text": "10 - 20", "source_id": "S-TEST-A", "url": "http://x", "accessed": "2026-10-22", "table": "t"}}, "https"),
        ({"transcriber_note": ""}, "transcriber_note"),
    ],
)
def test_rings_reject(tmp_path, repo, changes, message):
    write_sources(repo, [])
    with pytest.raises(PipelineError, match=message):
        load_rings(ring_file(tmp_path, **changes), load_sources(repo.sources_file), {1001})


def test_real_rings_file_loads():
    rings = load_rings(REAL_REPO.catalog / "rings.json", load_sources(REAL_REPO.sources_file), {699})
    main = {r.name: r for r in rings if not r.gap}
    assert set(main) == {"D Ring", "C Ring", "B Ring", "Cassini Division", "A Ring"}
    assert all(r.optical_depth_source_id == "S-PDS-RMS" and r.radii_source_id == "S-IAU-WGPSN" for r in main.values())
    # The main rings are contiguous in the Gazetteer, and every gap lies inside one of them.
    ordered = sorted(main.values(), key=lambda r: r.inner_km)
    assert all(a.outer_km == b.inner_km for a, b in zip(ordered, ordered[1:]))
    for gap in (r for r in rings if r.gap):
        assert any(r.inner_km <= gap.inner_km < gap.outer_km <= r.outer_km for r in ordered), gap.name


# ---- Pack validation of stars and rings ----


@pytest.mark.parametrize(
    "change, message",
    [
        (lambda c: c.rings.append(PackRing(naif(1002), "No plane", False, 1.0, 2.0, "S-TEST-A", "x")), "rotation model"),
        (lambda c: c.rings.append(PackRing(naif(1001), "Inverted", False, 2.0, 1.0, "S-TEST-A", "x")), "inner < outer"),
        (lambda c: c.rings.append(PackRing(naif(1001), "Gap", True, 1.0, 2.0, "S-TEST-A", "x", (0.0, 1.0), "S-TEST-A", "y")), "gap"),
        (lambda c: c.rings.append(PackRing(naif(1001), "Depth", False, 1.0, 2.0, "S-TEST-A", "x", (2.0, 1.0), "S-TEST-A", "y")), "optical depth"),
        (lambda c: setattr(c, "star_catalog", dataclasses.replace(c.star_catalog, stars=c.star_catalog.stars + (dataclasses.replace(c.star_catalog.stars[0], dec_rad=2.0, id=(4, 99)),))), "out of range"),
        (lambda c: setattr(c, "star_catalog", dataclasses.replace(c.star_catalog, stars=c.star_catalog.stars * 2)), "twice"),
        (lambda c: setattr(c, "star_catalog", dataclasses.replace(c.star_catalog, stars=(dataclasses.replace(c.star_catalog.stars[0], pm_dec_rad_per_s=1.0),))), "both components"),
    ],
)
def test_writer_rejects_bad_stars_and_rings(change, message):
    content = synthetic_content()
    change(content)
    with pytest.raises(PipelineError, match=message):
        write_pack(content)
