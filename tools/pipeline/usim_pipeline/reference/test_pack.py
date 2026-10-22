"""The synthetic round-trip fixture (P2-09): core/tests/fixtures/test_pack_v1.uspack, written by the
pipeline's pack writer, and test_pack_expected.hpp, its expected contents for the C++ reader tests.

All values are made up for the test and are not physical data. The content exercises every section and the
optional parts of each record: uncertainties, derived values, nutation terms with quadratic phase angles,
SPK types 2 and 3, coverage that ends inside the last record, a body placed at its primary (format 1.1), a
non-ASCII name, and (format 1.2) stars with and without color and proper motion, and rings with and without
optical depth, including a gap.
"""

from __future__ import annotations

import hashlib
from collections.abc import Callable
from pathlib import Path

import numpy

from ..catalog import MEAN_RADIUS_DERIVATION, volumetric_mean_radius
from ..pack.binary import canonical, read_pack, write_pack
from ..pack.model import (
    BodyKind,
    Field,
    Frame,
    PackBody,
    PackContent,
    PackApproximation,
    PackFile,
    PackQuantity,
    PackRing,
    PackRotation,
    PackSegment,
    PackSource,
    PackStar,
    PackStarCatalog,
    PackTime,
    Unit,
    naif,
)
from ..repo import Repo
from .common import cpp_double

FILE_NAME = "test_pack_v1.uspack"
HEADER_NAME = "test_pack_expected.hpp"
TT_MINUS_TAI_S = 32.184  # definitional (IAU 1991); the reader checks it against usim::constants
SHA256_LENGTHS = (0, 1, 55, 56, 57, 63, 64, 65, 119, 120, 1000)


def _records(count: int, size: int, init: float, interval: float, scale: float) -> numpy.ndarray:
    records = numpy.zeros((count, size), dtype=numpy.float64)
    for i in range(count):
        records[i, 0] = init + (i + 0.5) * interval
        records[i, 1] = interval / 2
        records[i, 2:] = [scale * (i + 1) + 0.25 * k for k in range(size - 2)]
    return records


def synthetic_content() -> PackContent:
    a, b, c = 3.5, 2.5, 1.5
    return PackContent(
        metadata={
            "pack.name": "synthetic-test",
            "pack.version": "0.0.1",
            "pack.description": "Synthetic pack for the reader round-trip test (P2-09). Values are made up, not physical data.",
            "pack.window": "none",
        },
        lock_sha256=hashlib.sha256(b"usim synthetic test lock").hexdigest(),
        sources=[
            PackSource("S-TEST-A", "Synthetic source A", "Test provider", "https://example.invalid/a", "Citation A", "Terms A", 1),
            PackSource("S-TEST-B", "Synthetic source B", "Test provider", "https://example.invalid/b", "Citation B", "Terms B", 2),
        ],
        files=[
            PackFile("S-TEST-A", "synthetic-a.bin", "https://example.invalid/a/synthetic-a.bin", "2000-01-01T00:00:00Z",
                     1234, hashlib.sha256(b"a").hexdigest(), hashlib.md5(b"a", usedforsecurity=False).hexdigest(), True),
            PackFile("S-TEST-B", "synthetic-b.txt", "https://example.invalid/b/synthetic-b.txt", "2001-02-03T04:05:06Z",
                     56, hashlib.sha256(b"b").hexdigest(), hashlib.md5(b"b", usedforsecurity=False).hexdigest(), False),
        ],  # fmt: skip
        bodies=[
            PackBody(naif(0), BodyKind.BARYCENTER, "Test Root", None),
            PackBody(naif(1000), BodyKind.STAR, "Test Star", naif(0)),
            PackBody(naif(1001), BodyKind.PLANET, "Test Planet", naif(1000)),
            PackBody(naif(1002), BodyKind.MOON, "Test Moon", naif(1001)),
            PackBody(naif(1003), BodyKind.DWARF_PLANET, "Test Körper", naif(1000)),
        ],
        quantities=[
            PackQuantity(naif(1000), Field.GM, Unit.KM3_PER_S2, 1.25, None, "S-TEST-A", "synthetic-a.bin GM"),
            PackQuantity(naif(1001), Field.RADIUS_A, Unit.KM, a, None, "S-TEST-B", "synthetic-b.txt RADII[0]"),
            PackQuantity(naif(1001), Field.RADIUS_B, Unit.KM, b, None, "S-TEST-B", "synthetic-b.txt RADII[1]"),
            PackQuantity(naif(1001), Field.RADIUS_C, Unit.KM, c, None, "S-TEST-B", "synthetic-b.txt RADII[2]"),
            PackQuantity(naif(1001), Field.MEAN_RADIUS, Unit.KM, volumetric_mean_radius(a, b, c), None, "S-TEST-B",
                         "synthetic-b.txt RADII", MEAN_RADIUS_DERIVATION.format(keyword="RADII")),
            PackQuantity(naif(1002), Field.GM, Unit.KM3_PER_S2, 0.0625, 0.001953125, "S-TEST-A", "synthetic-a.bin GM2"),
        ],  # fmt: skip
        rotations=[
            PackRotation(naif(1000), Frame.ICRF, "S-TEST-A", "synthetic-a.bin ROT1", (1.0,), (2.0,), (3.0, 4.0)),
            PackRotation(
                naif(1001), Frame.ICRF, "S-TEST-B", "synthetic-b.txt ROT2", pole_ra=(10.0, 0.5), pole_dec=(60.0,),
                pm=(100.0, 720.25, -0.125), nut_prec_ra=(0.5, -0.25), nut_prec_dec=(0.125,), nut_prec_pm=(1.0, 2.0),
                angles_body=1000, phase_degree=2, nut_prec_angles=(30.0, 1.5, 0.25, 45.0, -2.0, 0.0),
            ),
        ],  # fmt: skip
        time=PackTime(
            "S-TEST-A", "synthetic-a.bin TIME", TT_MINUS_TAI_S, 0.001, 0.0125, 1.5, 2.5e-7,
            ((1980, 1, 1, 20), (1990, 7, 1, 21), (2001, 1, 1, 22)),
        ),  # fmt: skip
        segments=[
            PackSegment(naif(1000), naif(0), Frame.ICRF, 2, -864000.0, 432000.0, -864000.0, 432000.0,
                        _records(3, 11, -864000.0, 432000.0, 10.0), "S-TEST-A", "synthetic-a.bin segment 0"),
            PackSegment(naif(1001), naif(1000), Frame.ICRF, 3, 3600.0, 150000.0, 0.0, 86400.0,
                        _records(2, 14, 0.0, 86400.0, -2.0), "S-TEST-B", "synthetic-b.txt segment 0"),
            PackSegment(naif(1002), naif(1001), Frame.ICRF, 2, -86400.0, 86400.0, -86400.0, 172800.0,
                        _records(1, 5, -86400.0, 172800.0, 0.5), "S-TEST-A", "synthetic-a.bin segment 1"),
        ],  # fmt: skip
        approximations=[PackApproximation(naif(1003), naif(1000), "Synthetic: no segment; placed at its primary")],
        star_catalog=PackStarCatalog(
            "S-TEST-B", 86400.0, "synthetic-b.txt STARS", "Synthetic: made up for the test",
            (
                PackStar((3, 11), 0.0, 0.0, None, None, 4.0, None, ""),
                PackStar((4, 2), 1.0, -0.5, 1.0e-12, -2.0e-12, 1.5, 0.25, "Syn A"),
                PackStar((4, 7), 6.0, 1.25, None, None, -0.5, 1.75, "Syn B"),
            ),
        ),
        rings=[
            PackRing(naif(1000), "Star Ring", False, 5.0, 6.0, "S-TEST-A", "synthetic-a.bin RING1"),
            PackRing(naif(1001), "Test Ring", False, 10.0, 20.0, "S-TEST-B", "synthetic-b.txt RING2",
                     (0.25, 0.5), "S-TEST-A", "synthetic-a.bin DEPTH2"),
            PackRing(naif(1001), "Test Gap", True, 12.0, 13.0, "S-TEST-B", "synthetic-b.txt GAP"),
        ],
    )  # fmt: skip


def _cpp_string(text: str) -> str:
    """A C++ string literal of the UTF-8 bytes of `text`; other than printable ASCII, bytes become separate
    "\\xNN" pieces so a following character cannot extend the escape."""
    pieces: list[str] = []
    current = ""
    for byte in text.encode("utf-8"):
        ch = chr(byte)
        if byte < 0x80 and ch.isprintable() and ch not in '"\\':
            current += ch
            continue
        if current:
            pieces.append(f'"{current}"')
            current = ""
        pieces.append(f'"\\x{byte:02x}"')
    if current or not pieces:
        pieces.append(f'"{current}"')
    return " ".join(pieces)


def _array(name: str, values) -> tuple[str | None, str]:
    """A declaration of a constexpr double array and a span expression for it ({} when empty)."""
    values = list(values)
    if not values:
        return None, "{}"
    return f"inline constexpr double {name}[] = {{{', '.join(cpp_double(v) for v in values)}}};", f"std::span<const double>{{{name}}}"


def expected_header(content: PackContent, data: bytes) -> str:
    header, _ = read_pack(data)
    out = [
        "// Generated by `tools/pipeline/usim-pipeline reference test-pack`. Do not edit by hand.",
        "// Expected contents of test_pack_v1.uspack, the synthetic round-trip fixture (P2-09). The values are",
        "// made up for the test and are not physical data.",
        "#pragma once",
        "",
        "#include <cstddef>",
        "#include <cstdint>",
        "#include <span>",
        "",
        "namespace usim::test::pack {",
        "",
        f'inline constexpr const char* k_file_name = "{FILE_NAME}";',
        f"inline constexpr std::uint64_t k_file_size = {len(data)};",
        f'inline constexpr const char* k_content_sha256 = "{header.content_sha256}";',
        f'inline constexpr const char* k_lock_sha256 = "{content.lock_sha256}";',
        "",
        "struct MetaRow {",
        "    const char* key;",
        "    const char* value;",
        "};",
        "inline constexpr MetaRow k_metadata[] = {",
        *(f"    {{{_cpp_string(k)}, {_cpp_string(content.metadata[k])}}}," for k in sorted(content.metadata)),
        "};",
        "",
        "struct SourceRow {",
        "    const char* id;",
        "    const char* title;",
        "    const char* provider;",
        "    const char* url;",
        "    const char* citation;",
        "    const char* license;",
        "    std::int32_t tier;",
        "};",
        "inline constexpr SourceRow k_sources[] = {",
        *(
            f"    {{{', '.join(_cpp_string(t) for t in (s.id, s.title, s.provider, s.url, s.citation, s.license))}, {s.tier}}},"
            for s in content.sources
        ),
        "};",
        "",
        "struct FileRow {",
        "    const char* source_id;",
        "    const char* name;",
        "    const char* url;",
        "    const char* retrieved_utc;",
        "    std::uint64_t size;",
        "    const char* sha256;",
        "    const char* md5;",
        "    bool publisher_checksum_verified;",
        "};",
        "inline constexpr FileRow k_files[] = {",
        *(
            f"    {{{', '.join(_cpp_string(t) for t in (f.source_id, f.name, f.url, f.retrieved_utc))}, {f.size}, "
            f'"{f.sha256}", "{f.md5}", {str(f.publisher_checksum_verified).lower()}}},'
            for f in content.files
        ),
        "};",
        "",
        "struct BodyRow {",
        "    std::int64_t naif_id;",
        "    std::int32_t kind;",
        "    const char* name;",
        "    bool has_primary;",
        "    std::int64_t primary_naif_id;",
        "};",
        "inline constexpr BodyRow k_bodies[] = {",
        *(
            f"    {{{b.id[1]}, {int(b.kind)}, {_cpp_string(b.name)}, {str(b.primary is not None).lower()}, "
            f"{b.primary[1] if b.primary else 0}}},"
            for b in content.bodies
        ),
        "};",
        "",
        "struct QuantityRow {",
        "    std::int64_t naif_id;",
        "    std::int32_t field; ///< 1 gm, 2 mean_radius, 3 radius_a, 4 radius_b, 5 radius_c",
        "    std::int32_t unit;  ///< usim::catalog::Unit",
        "    double value;",
        "    bool has_uncertainty;",
        "    double uncertainty;",
        "    const char* source_id;",
        "    const char* locator;",
        "    const char* derivation;",
        "};",
        "inline constexpr QuantityRow k_quantities[] = {",
        *(
            f"    {{{q.body[1]}, {int(q.field)}, {int(q.unit)}, {cpp_double(q.value)}, {str(q.uncertainty is not None).lower()}, "
            f"{cpp_double(q.uncertainty or 0.0)}, {_cpp_string(q.source_id)}, {_cpp_string(q.locator)}, {_cpp_string(q.derivation)}}},"
            for q in content.quantities
        ),
        "};",
        "",
    ]
    rotation_rows = []
    for i, r in enumerate(content.rotations):
        spans = []
        for part in ("pole_ra", "pole_dec", "pm", "nut_prec_ra", "nut_prec_dec", "nut_prec_pm", "nut_prec_angles"):
            declaration, span = _array(f"k_rotation{i}_{part}", getattr(r, part))
            if declaration:
                out.append(declaration)
            spans.append(span)
        rotation_rows.append(
            f"    {{{r.body[1]}, {_cpp_string(r.source_id)}, {_cpp_string(r.locator)}, {r.phase_degree}, {r.angles_body}, "
            + ", ".join(spans) + "},"
        )
    out += [
        "",
        "struct RotationRow {",
        "    std::int64_t naif_id;",
        "    const char* source_id;",
        "    const char* locator;",
        "    std::int32_t phase_degree;",
        "    std::int64_t angles_naif_id;",
        "    std::span<const double> pole_ra;",
        "    std::span<const double> pole_dec;",
        "    std::span<const double> pm;",
        "    std::span<const double> nut_prec_ra;",
        "    std::span<const double> nut_prec_dec;",
        "    std::span<const double> nut_prec_pm;",
        "    std::span<const double> nut_prec_angles;",
        "};",
        "inline constexpr RotationRow k_rotations[] = {",
        *rotation_rows,
        "};",
        "",
    ]
    t = content.time
    assert t is not None
    out += [
        f"inline constexpr const char* k_time_source_id = {_cpp_string(t.source_id)};",
        f"inline constexpr const char* k_time_locator = {_cpp_string(t.locator)};",
        f"inline constexpr double k_tdb_k_s = {cpp_double(t.k_s)};",
        f"inline constexpr double k_tdb_eb = {cpp_double(t.eb)};",
        f"inline constexpr double k_tdb_m0_rad = {cpp_double(t.m0_rad)};",
        f"inline constexpr double k_tdb_m1_rad_per_s = {cpp_double(t.m1_rad_per_s)};",
        "",
        "struct LeapRow {",
        "    std::int32_t year;",
        "    std::int32_t month;",
        "    std::int32_t day;",
        "    std::int32_t tai_minus_utc_s;",
        "};",
        "inline constexpr LeapRow k_leap_seconds[] = {",
        *(f"    {{{y}, {m}, {d}, {n}}}," for y, m, d, n in t.leap_seconds),
        "};",
        "",
    ]
    segment_rows = []
    for i, s in enumerate(content.segments):
        declaration, span = _array(f"k_segment{i}_records", s.records.reshape(-1))
        out.append(declaration or "")
        segment_rows.append(
            f"    {{{s.target[1]}, {s.center[1]}, {s.spk_type}, {cpp_double(s.start_s)}, {cpp_double(s.end_s)}, "
            f"{cpp_double(s.init_s)}, {cpp_double(s.interval_s)}, {s.record_size}, {s.record_count}, {span}, "
            f"{_cpp_string(s.source_id)}, {_cpp_string(s.locator)}}},"
        )
    out += [
        "",
        "/// SHA-256 (Python hashlib) of `length` bytes 'a', at the padding boundaries of the algorithm.",
        "struct Sha256Case {",
        "    std::size_t length;",
        "    const char* sha256;",
        "};",
        "inline constexpr Sha256Case k_sha256_cases[] = {",
        *(f'    {{{n}, "{hashlib.sha256(b"a" * n).hexdigest()}"}},' for n in SHA256_LENGTHS),
        "};",
        "",
        "struct SegmentRow {",
        "    std::int64_t target_naif_id;",
        "    std::int64_t center_naif_id;",
        "    std::int32_t spk_type;",
        "    double start_tdb_s;",
        "    double end_tdb_s;",
        "    double init_tdb_s;",
        "    double interval_s;",
        "    std::uint32_t record_size;",
        "    std::uint32_t record_count;",
        "    std::span<const double> records;",
        "    const char* source_id;",
        "    const char* locator;",
        "};",
        "inline constexpr SegmentRow k_segments[] = {",
        *segment_rows,
        "};",
        "",
        "struct ApproximationRow {",
        "    std::int64_t naif_id;",
        "    std::int64_t proxy_naif_id;",
        "    const char* reason;",
        "};",
        "inline constexpr ApproximationRow k_approximations[] = {",
        *(f"    {{{a.body[1]}, {a.proxy[1]}, {_cpp_string(a.reason)}}}," for a in content.approximations),
        "};",
        "",
    ]
    catalog = content.star_catalog
    assert catalog is not None
    out += [
        f"inline constexpr const char* k_star_source_id = {_cpp_string(catalog.source_id)};",
        f"inline constexpr const char* k_star_locator = {_cpp_string(catalog.locator)};",
        f"inline constexpr const char* k_star_derivation = {_cpp_string(catalog.derivation)};",
        f"inline constexpr double k_star_epoch_tdb_s = {cpp_double(catalog.epoch_tdb_s)};",
        "",
        "struct StarRow {",
        "    std::int32_t id_namespace;",
        "    std::int64_t id;",
        "    const char* designation;",
        "    double ra_rad;",
        "    double dec_rad;",
        "    bool has_proper_motion;",
        "    double pm_ra_cos_dec_rad_per_s;",
        "    double pm_dec_rad_per_s;",
        "    double v_magnitude;",
        "    bool has_color;",
        "    double b_minus_v;",
        "};",
        "inline constexpr StarRow k_stars[] = {",
        *(
            f"    {{{s.id[0]}, {s.id[1]}, {_cpp_string(s.designation)}, {cpp_double(s.ra_rad)}, {cpp_double(s.dec_rad)}, "
            f"{str(s.pm_ra_cosdec_rad_per_s is not None).lower()}, {cpp_double(s.pm_ra_cosdec_rad_per_s or 0.0)}, "
            f"{cpp_double(s.pm_dec_rad_per_s or 0.0)}, {cpp_double(s.v_mag)}, {str(s.b_v is not None).lower()}, "
            f"{cpp_double(s.b_v or 0.0)}}},"
            for s in catalog.stars
        ),
        "};",
        "",
        "struct RingRow {",
        "    std::int64_t naif_id;",
        "    const char* name;",
        "    bool gap;",
        "    double inner_km;",
        "    double outer_km;",
        "    const char* radii_source_id;",
        "    const char* radii_locator;",
        "    bool has_optical_depth;",
        "    double optical_depth_smallest;",
        "    double optical_depth_largest;",
        "    const char* optical_depth_source_id;",
        "    const char* optical_depth_locator;",
        "};",
        "inline constexpr RingRow k_rings[] = {",
        *(
            f"    {{{r.body[1]}, {_cpp_string(r.name)}, {str(r.gap).lower()}, {cpp_double(r.inner_km)}, "
            f"{cpp_double(r.outer_km)}, {_cpp_string(r.radii_source_id)}, {_cpp_string(r.radii_locator)}, "
            f"{str(r.optical_depth is not None).lower()}, {cpp_double((r.optical_depth or (0.0, 0.0))[0])}, "
            f"{cpp_double((r.optical_depth or (0.0, 0.0))[1])}, {_cpp_string(r.optical_depth_source_id or '')}, "
            f"{_cpp_string(r.optical_depth_locator)}}},"
            for r in content.rings
        ),
        "};",
        "",
        "} // namespace usim::test::pack",
    ]
    return "\n".join(out) + "\n"


def generate(repo: Repo, out_dir: Path | None = None, log: Callable[[str], None] = print) -> list[Path]:
    out_dir = out_dir or repo.core_fixtures
    content = synthetic_content()
    data = write_pack(content)
    _, back = read_pack(data)
    if back != canonical(content):
        raise AssertionError("synthetic pack does not round-trip through the Python reader")
    out_dir.mkdir(parents=True, exist_ok=True)
    pack_path = out_dir / FILE_NAME
    header_path = out_dir / HEADER_NAME
    pack_path.write_bytes(data)
    header_path.write_text(expected_header(back, data), encoding="utf-8")
    log(f"wrote {pack_path} ({len(data)} bytes) and {header_path}")
    return [pack_path, header_path]
