"""Pack format v1: writer and reader round trip, determinism and validation (P2-07). Synthetic content only."""

from __future__ import annotations

import dataclasses
import hashlib
import struct

import numpy
import pytest

from usim_pipeline import PipelineError
from usim_pipeline.pack.binary import COUNTS, HEADER, SECTION, canonical, read_header, read_pack, write_pack
from usim_pipeline.pack.model import Field, Frame, PackApproximation, PackQuantity, Unit, naif
from usim_pipeline.reference.test_pack import synthetic_content


def test_round_trip_preserves_everything():
    content = synthetic_content()
    header, back = read_pack(write_pack(content))
    assert back == canonical(content)
    assert header.format_major == 1 and header.format_minor == 4
    assert header.lock_sha256 == content.lock_sha256
    assert back.bodies[4].name == "Test Körper"
    assert back.approximations == [PackApproximation(naif(1003), naif(1000), "Synthetic: no segment; placed at its primary")]
    assert back.segments[1].records.tobytes() == content.segments[1].records.tobytes()
    assert back.orientations[0].segments[1].records.tobytes() == content.orientations[0].segments[1].records.tobytes()


def test_output_is_deterministic_and_order_independent_for_quantities():
    content = synthetic_content()
    shuffled = dataclasses.replace(content, quantities=list(reversed(content.quantities)),
                                   metadata=dict(reversed(list(content.metadata.items()))))  # fmt: skip
    assert write_pack(content) == write_pack(content) == write_pack(shuffled)


def test_layout_is_aligned_and_hashed():
    data = write_pack(synthetic_content())
    header = read_header(data)
    assert len(data) % 8 == 0 and header.file_size == len(data)
    assert all(offset % 8 == 0 for offset, _ in header.sections.values())
    assert header.content_sha256 == hashlib.sha256(data[HEADER.size :]).hexdigest()
    assert data[:8] == b"USIMPACK" and data[12:16] == bytes([4, 3, 2, 1])


def _patch(data: bytes, offset: int, value: bytes) -> bytes:
    return data[:offset] + value + data[offset + len(value) :]


def _rehash(data: bytes) -> bytes:
    return _patch(data, 32, hashlib.sha256(data[HEADER.size :]).digest())


@pytest.mark.parametrize(
    "corrupt, message",
    [
        (lambda d: _patch(d, 0, b"NOTAPACK"), "bad magic"),
        (lambda d: _patch(d, 8, struct.pack("<H", 2)), "not supported"),
        (lambda d: _patch(d, 12, struct.pack("<I", 0x04030201)), "endianness"),
        (lambda d: d[:-8], "file size"),
        (lambda d: _patch(d, len(d) - 1, b"\x01"), "content hash mismatch"),
        (lambda d: b"version https://git-lfs.github.com/spec/v1\noid sha256:abc\n", "Git LFS pointer"),
        (lambda d: d[:100], "shorter than the header"),
        # Point the second section at the first one's offset (and fix the hash, so validation must catch it).
        (lambda d: _rehash(_patch(d, HEADER.size + SECTION.size + 8, d[HEADER.size + 8 : HEADER.size + 16])), "overlap"),
        (lambda d: _rehash(_patch(d, HEADER.size + 8, struct.pack("<Q", 1 << 30))), "misaligned or out of bounds"),
    ],
)
def test_reader_rejects_damaged_packs(corrupt, message):
    with pytest.raises(PipelineError, match=message):
        read_pack(corrupt(write_pack(synthetic_content())))


@pytest.mark.parametrize(
    "change, message",
    [
        (lambda c: c.quantities.append(PackQuantity(naif(1000), Field.RADIUS_A, Unit.KM3_PER_S2, 1.0, None, "S-TEST-A", "x")), "unit"),
        (lambda c: c.quantities.append(PackQuantity(naif(1000), Field.GM, Unit.KM3_PER_S2, 2.0, None, "S-TEST-A", "x")), "twice"),
        (lambda c: c.quantities.append(PackQuantity(naif(1003), Field.GM, Unit.KM3_PER_S2, 2.0, None, "S-NOPE", "x")), "unknown source"),
        (lambda c: c.quantities.append(PackQuantity(naif(1003), Field.GM, Unit.KM3_PER_S2, float("nan"), None, "S-TEST-A", "x")), "finite"),
        (lambda c: c.quantities.append(PackQuantity(naif(1003), Field.GM, Unit.KM3_PER_S2, 1.0, -1.0, "S-TEST-A", "x")), "uncertainty"),
        (lambda c: c.quantities.append(PackQuantity(naif(77), Field.GM, Unit.KM3_PER_S2, 1.0, None, "S-TEST-A", "x")), "not in the pack"),
        (lambda c: c.bodies.append(dataclasses.replace(c.bodies[1])), "duplicate body"),
        (lambda c: c.bodies.append(dataclasses.replace(c.bodies[1], id=naif(5000), primary=naif(6000))), "primary"),
        (lambda c: c.segments.append(dataclasses.replace(c.segments[0], spk_type=5)), "SPK type"),
        (lambda c: c.segments.append(dataclasses.replace(c.segments[0], records=numpy.zeros((2, 12)))), "record size"),
        (lambda c: c.segments.append(dataclasses.replace(c.segments[0], end_s=1.0e9)), "past the last record"),
        (lambda c: c.segments.append(dataclasses.replace(c.segments[0], target=naif(4242))), "not in the pack"),
        (lambda c: c.rotations.append(dataclasses.replace(c.rotations[1], body=naif(1002), phase_degree=4)), "phase angle"),
        (lambda c: c.rotations.append(dataclasses.replace(c.rotations[0], body=naif(1002), pm=())), "1 to 3"),
        (lambda c: c.approximations.append(PackApproximation(naif(1002), naif(1001), "x")), "own ephemeris segment"),
        (lambda c: c.approximations.append(PackApproximation(naif(1003), naif(1000), "x")), "twice"),
        (lambda c: c.approximations.append(PackApproximation(naif(1001), naif(0), "x")), "not the body's primary"),
        (lambda c: c.approximations.append(PackApproximation(naif(0), naif(0), "x")), "not the body's primary"),
        (lambda c: c.approximations.append(PackApproximation(naif(4242), naif(0), "x")), "not in the pack"),
        (lambda c: c.orientations.append(dataclasses.replace(c.orientations[0])), "two orientation models"),
        (lambda c: c.orientations.append(dataclasses.replace(c.orientations[0], body=naif(4242))), "not in the pack"),
        (lambda c: c.orientations.append(dataclasses.replace(c.orientations[0], body=naif(1003), segments=())), "no segments"),
        (lambda c: c.orientations.append(dataclasses.replace(c.orientations[1], body=naif(1003), offset_axes=(3, 4, 1))), "axes"),
        (lambda c: c.orientations.append(dataclasses.replace(c.orientations[0], body=naif(1003), frame_name="")), "frame names"),
        (lambda c: c.orientations.append(dataclasses.replace(c.orientations[1], body=naif(1003), offset_source_id="S-NOPE")), "unknown source"),
        (lambda c: c.orientations.append(dataclasses.replace(c.orientations[1], body=naif(1003), offset_locator="")), "needs a locator"),
        (lambda c: c.orientations.append(dataclasses.replace(c.orientations[0], body=naif(1003), offset_angles=(1.0, 0.0, 0.0))), "without an offset"),
        (lambda c: c.orientations.append(dataclasses.replace(c.orientations[0], body=naif(1003), frame_name="OTHER")), "without an offset"),
        (lambda c: c.orientations.append(dataclasses.replace(c.orientations[0], body=naif(1003), offset_locator="x")), "without an offset"),
        (lambda c: c.orientations.append(dataclasses.replace(
            c.orientations[0], body=naif(1003),
            segments=(dataclasses.replace(c.orientations[0].segments[0], records=numpy.zeros((2, 10))),))), "record size"),
        (lambda c: c.orientations.append(dataclasses.replace(
            c.orientations[0], body=naif(1003),
            segments=(dataclasses.replace(c.orientations[0].segments[0], end_s=1.0e9),))), "past the last record"),
    ],
)
def test_writer_rejects_content_a_reader_would_refuse(change, message):
    content = synthetic_content()
    change(content)
    with pytest.raises(PipelineError, match=message):
        write_pack(content)


def test_a_reader_skips_unknown_sections_and_appx_is_optional():
    # Rename APPX to an id the reader does not know: the pack still reads, as a format 1.0 pack would,
    # without approximate positions.
    data = write_pack(synthetic_content())
    header = read_header(data)
    index = list(header.sections).index("APPX")
    renamed = _rehash(_patch(data, HEADER.size + index * SECTION.size, b"ZZZZ"))
    _, back = read_pack(renamed)
    assert back.approximations == []
    assert len(back.segments) == 3


def test_star_and_ring_sections_are_optional():
    data = write_pack(synthetic_content())
    header = read_header(data)
    for name in ("STAR", "RING"):
        index = list(header.sections).index(name)
        data = _patch(data, HEADER.size + index * SECTION.size, b"ZZZ" + name[0].encode())
    _, back = read_pack(_rehash(data))
    assert back.star_catalog is None and back.rings == []
    # A pack without stars and rings has no STAR or RING section at all.
    plain = dataclasses.replace(synthetic_content(), star_catalog=None, rings=[])
    assert {"STAR", "RING"}.isdisjoint(read_header(write_pack(plain)).sections)


def test_surface_conditions_go_to_their_own_optional_section():
    content = synthetic_content()
    data = write_pack(content)
    header = read_header(data)
    assert header.format_minor == 4 and "SURF" in header.sections
    surface = {Field.MEAN_TEMPERATURE, Field.SURFACE_PRESSURE, Field.REFERENCE_PRESSURE}
    # QNTY keeps the format 1.0 fields; SURF holds the four surface records, in the QNTY record layout.
    qnty_count = struct.unpack_from("<I", data, header.sections["QNTY"][0])[0]
    surf_count = struct.unpack_from("<I", data, header.sections["SURF"][0])[0]
    assert qnty_count == sum(q.field not in surface for q in content.quantities)
    assert surf_count == sum(q.field in surface for q in content.quantities) == 4
    # A reader that does not know SURF (format 1.3) reads the rest.
    index = list(header.sections).index("SURF")
    _, back = read_pack(_rehash(_patch(data, HEADER.size + index * SECTION.size, b"ZZZS")))
    assert [q for q in back.quantities if q.field in surface] == []
    assert len(back.quantities) == qnty_count
    plain = dataclasses.replace(content, quantities=[q for q in content.quantities if q.field not in surface])
    assert "SURF" not in read_header(write_pack(plain)).sections


@pytest.mark.parametrize(
    "field, unit, value, message",
    [
        (Field.MEAN_TEMPERATURE, Unit.K, 0.0, "must be positive"),
        (Field.REFERENCE_PRESSURE, Unit.PA, -1.0, "must be positive"),
        (Field.SURFACE_PRESSURE, Unit.PA, -1.0, "must not be negative"),
        (Field.SURFACE_PRESSURE, Unit.K, 1.0, "expected PA"),
    ],
)
def test_writer_rejects_invalid_surface_conditions(field, unit, value, message):
    content = synthetic_content()
    content.quantities.append(PackQuantity(naif(1003), field, unit, value, None, "S-TEST-A", "x"))
    with pytest.raises(PipelineError, match=message):
        write_pack(content)


def test_a_body_has_at_most_one_kind_of_reference_surface():
    content = synthetic_content()  # the test planet has a reference pressure
    content.quantities.append(PackQuantity(naif(1001), Field.SURFACE_PRESSURE, Unit.PA, 1.0, None, "S-TEST-A", "x"))
    with pytest.raises(PipelineError, match="both a surface pressure and a reference pressure"):
        write_pack(content)


def test_a_surface_field_in_qnty_is_rejected():
    # Move SURF's records into QNTY by swapping the two section ids: each section then holds the other's fields.
    data = write_pack(synthetic_content())
    header = read_header(data)
    names = list(header.sections)
    swapped = _patch(data, HEADER.size + names.index("QNTY") * SECTION.size, b"SURF")
    swapped = _patch(swapped, HEADER.size + names.index("SURF") * SECTION.size, b"QNTY")
    with pytest.raises(PipelineError, match="does not belong in section"):
        read_pack(_rehash(swapped))


def test_orientation_section_is_optional_and_shares_the_coefficient_data():
    content = synthetic_content()
    data = write_pack(content)
    header = read_header(data)
    index = list(header.sections).index("ORNT")
    _, back = read_pack(_rehash(_patch(data, HEADER.size + index * SECTION.size, b"ZZZO")))
    assert back.orientations == [] and len(back.segments) == 3
    # The orientation records follow the ephemeris records in EPHD.
    ephemeris_doubles = sum(s.records.size for s in content.segments)
    orientation_doubles = sum(g.records.size for o in content.orientations for g in o.segments)
    assert header.sections["EPHD"][1] == 8 * (ephemeris_doubles + orientation_doubles)
    plain = dataclasses.replace(content, orientations=[])
    assert "ORNT" not in read_header(write_pack(plain)).sections


def test_orientation_coverage_may_end_a_rounding_error_past_the_last_record():
    content = synthetic_content()
    model = content.orientations[1]
    segment = model.segments[-1]
    records_end = segment.init_s + segment.record_count * segment.interval_s
    within = dataclasses.replace(segment, end_s=records_end + 5.0e-7)
    content.orientations[1] = dataclasses.replace(model, segments=(*model.segments[:-1], within))
    _, back = read_pack(write_pack(content))
    assert back.orientations[1].segments[-1].end_s == records_end + 5.0e-7
    beyond = dataclasses.replace(segment, end_s=records_end + 2.0e-6)
    content.orientations[1] = dataclasses.replace(model, segments=(*model.segments[:-1], beyond))
    with pytest.raises(PipelineError, match="past the last record"):
        write_pack(content)
    # Ephemeris segments keep the exact rule.
    plain = synthetic_content()
    s = plain.segments[0]
    plain.segments[0] = dataclasses.replace(s, end_s=s.init_s + s.record_count * s.interval_s + 5.0e-7)
    with pytest.raises(PipelineError, match="past the last record"):
        write_pack(plain)


def test_an_orientation_model_without_an_offset_round_trips_and_has_source_index_0():
    content = synthetic_content()
    model = next(o for o in content.orientations if o.offset_source_id is None)
    assert {s.base_frame for s in model.segments} == {Frame.ICRF, Frame.ECLIPJ2000}
    data = write_pack(content)
    _, back = read_pack(data)
    assert back.orientations == sorted(content.orientations, key=lambda o: o.body[1])
    # Model 0 (sorted by body) has no offset: a non-zero offset source index is refused.
    offset, _ = read_header(data).sections["ORNT"]
    at = offset + COUNTS.size + 12
    assert struct.unpack_from("<I", data, at)[0] == 0
    with pytest.raises(PipelineError, match="offset source index 0"):
        read_pack(_rehash(_patch(data, at, struct.pack("<I", 1))))
