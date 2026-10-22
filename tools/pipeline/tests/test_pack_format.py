"""Pack format v1: writer and reader round trip, determinism and validation (P2-07). Synthetic content only."""

from __future__ import annotations

import dataclasses
import hashlib
import struct

import numpy
import pytest

from usim_pipeline import PipelineError
from usim_pipeline.pack.binary import HEADER, SECTION, canonical, read_header, read_pack, write_pack
from usim_pipeline.pack.model import Field, PackApproximation, PackQuantity, Unit, naif
from usim_pipeline.reference.test_pack import synthetic_content


def test_round_trip_preserves_everything():
    content = synthetic_content()
    header, back = read_pack(write_pack(content))
    assert back == canonical(content)
    assert header.format_major == 1 and header.format_minor == 2
    assert header.lock_sha256 == content.lock_sha256
    assert back.bodies[4].name == "Test Körper"
    assert back.approximations == [PackApproximation(naif(1003), naif(1000), "Synthetic: no segment; placed at its primary")]
    assert back.segments[1].records.tobytes() == content.segments[1].records.tobytes()


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
        (lambda d: _rehash(_patch(d, HEADER.size + 8, struct.pack("<Q", 5000))), "misaligned or out of bounds"),
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
