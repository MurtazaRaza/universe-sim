"""The registry, the lock file, fetch and verify (P2-01, P2-02)."""

from __future__ import annotations

import hashlib
import json

import pytest

from usim_pipeline import PipelineError
from usim_pipeline.fetch import fetch, parse_checksum_list, verify
from usim_pipeline.jsonio import read_json
from usim_pipeline.sources import load_lock, load_sources, lock_digest, parse_sources

from .conftest import write_sources

URL = "https://example.invalid/a/data.bin"
LIST = "https://example.invalid/a/aa_checksums.txt"
DATA = b"synthetic file content\n" * 100


def declare(repo, checksums: str | None = LIST, url: str = URL) -> None:
    write_sources(repo, [{"name": "data.bin", "path": "test/data.bin", "url": url, "publisher_checksums": checksums}])


def md5(data: bytes) -> str:
    return hashlib.md5(data, usedforsecurity=False).hexdigest()


def serve(server, data: bytes = DATA, listed: str | None = None) -> None:
    server.files[URL] = data
    server.files[LIST] = f"{listed or md5(data)}  data.bin\n0123456789abcdef0123456789abcdef  other.bin\n".encode()


def quiet(_: str) -> None:
    pass


def run(repo, server, **kwargs):
    return fetch(repo, opener=server.open, now=lambda: "2026-01-02T03:04:05Z", log=quiet, **kwargs)


def test_real_registry_is_valid():
    from .conftest import REAL_REPO

    sources = load_sources(REAL_REPO.sources_file)
    assert {"S-JPL-DE440", "S-NAIF-PCK11", "S-NAIF-GM440", "S-NAIF-LSK"} <= {s.id for s in sources}
    assert all(s.tier in (1, 2) for s in sources)


@pytest.mark.parametrize(
    "change, message",
    [
        (lambda s: s.update(id="bad id"), "id must look like"),
        (lambda s: s.update(tier=3), "tier must be 1 or 2"),
        (lambda s: s.update(url="http://example.invalid/"), "must use https"),
        (lambda s: s["files"][0].update(path="../escape/data.bin"), "plain relative POSIX path"),
        (lambda s: s["files"][0].update(path="/abs/data.bin"), "plain relative POSIX path"),
        (lambda s: s["files"][0].update(url="https://example.invalid/a/other.bin"), "same file name"),
        (lambda s: s.pop("citation"), "missing field 'citation'"),
    ],
)
def test_registry_validation(change, message):
    source = {
        "id": "S-TEST-A", "title": "t", "provider": "p", "tier": 1, "url": "https://example.invalid/", "citation": "c",
        "license": "l", "files": [{"name": "data.bin", "path": "test/data.bin", "url": URL, "publisher_checksums": None}],
    }  # fmt: skip
    change(source)
    with pytest.raises(PipelineError, match=message):
        parse_sources({"schema_version": 1, "sources": [source]})


def test_duplicate_json_keys_are_rejected(tmp_path):
    path = tmp_path / "dup.json"
    path.write_text('{"a": 1, "a": 2}')
    with pytest.raises(PipelineError, match="duplicate JSON key"):
        read_json(path)


def test_checksum_list_parsing():
    listing = parse_checksum_list("3917EE56769DB332790C751E2168843D  de440s.bsp\nnot a line\nabc  short.bsp\n")
    assert listing == {"de440s.bsp": "3917ee56769db332790c751e2168843d"}


def test_fetch_pins_a_new_file_after_checking_the_publisher_md5(repo, server):
    declare(repo)
    serve(server)
    result = run(repo, server)
    assert result.actions == [("S-TEST-A/data.bin", "pinned")]
    assert (repo.raw / "test" / "data.bin").read_bytes() == DATA
    entry = load_lock(repo.lock_file)[("S-TEST-A", "data.bin")]
    assert (entry.size, entry.sha256, entry.md5, entry.publisher_md5) == (
        len(DATA), hashlib.sha256(DATA).hexdigest(), md5(DATA), md5(DATA))
    assert entry.retrieved_utc == "2026-01-02T03:04:05Z"
    assert verify(repo, log=quiet) == []


def test_fetch_without_publisher_list_pins_by_sha256(repo, server):
    declare(repo, checksums=None)
    server.files[URL] = DATA
    run(repo, server)
    entry = load_lock(repo.lock_file)[("S-TEST-A", "data.bin")]
    assert entry.publisher_md5 is None and entry.publisher_checksums is None
    assert server.requests == [URL]


def test_local_copy_matching_the_publisher_md5_is_pinned_without_downloading(repo, server):
    declare(repo)
    serve(server)
    (repo.raw / "test").mkdir(parents=True)
    (repo.raw / "test" / "data.bin").write_bytes(DATA)
    result = run(repo, server)
    assert result.actions == [("S-TEST-A/data.bin", "pinned")]
    assert server.requests == [LIST]  # the checksum list only
    entry = load_lock(repo.lock_file)[("S-TEST-A", "data.bin")]
    assert (entry.sha256, entry.publisher_md5) == (hashlib.sha256(DATA).hexdigest(), md5(DATA))
    assert verify(repo, log=quiet) == []


def test_local_copy_not_matching_the_publisher_md5_is_downloaded_again(repo, server):
    declare(repo)
    serve(server)
    (repo.raw / "test").mkdir(parents=True)
    (repo.raw / "test" / "data.bin").write_bytes(DATA[:100])  # an interrupted copy
    assert run(repo, server).actions == [("S-TEST-A/data.bin", "pinned")]
    assert URL in server.requests
    assert (repo.raw / "test" / "data.bin").read_bytes() == DATA


def test_local_copy_without_publisher_list_is_not_trusted(repo, server):
    declare(repo, checksums=None)
    server.files[URL] = DATA
    (repo.raw / "test").mkdir(parents=True)
    (repo.raw / "test" / "data.bin").write_bytes(DATA)
    run(repo, server)
    assert server.requests == [URL]


def test_publisher_md5_mismatch_fails_and_leaves_nothing(repo, server):
    declare(repo)
    serve(server, listed="0" * 32)
    with pytest.raises(PipelineError, match="does not match the publisher"):
        run(repo, server)
    assert not repo.lock_file.exists()
    assert not (repo.raw / "test").exists() or not any((repo.raw / "test").iterdir())


def test_file_missing_from_publisher_list_fails(repo, server):
    declare(repo)
    server.files[URL] = DATA
    server.files[LIST] = b"0123456789abcdef0123456789abcdef  other.bin\n"
    with pytest.raises(PipelineError, match="not listed"):
        run(repo, server)


def test_size_check_against_content_length(repo, server):
    declare(repo)
    serve(server)
    server.announced[URL] = len(DATA) + 1
    with pytest.raises(PipelineError, match="announced"):
        run(repo, server)


def test_existing_lock_is_verified_not_changed(repo, server):
    declare(repo)
    serve(server)
    run(repo, server)
    before = repo.lock_file.read_bytes()
    server.requests.clear()
    result = run(repo, server)
    assert result.actions == [("S-TEST-A/data.bin", "verified")] and not result.lock_changed
    assert server.requests == []  # nothing downloaded
    assert repo.lock_file.read_bytes() == before


def test_missing_local_copy_is_downloaded_and_checked_against_the_lock(repo, server):
    declare(repo)
    serve(server)
    run(repo, server)
    (repo.raw / "test" / "data.bin").unlink()
    assert run(repo, server).actions == [("S-TEST-A/data.bin", "downloaded")]


def test_changed_publisher_file_is_refused_until_update(repo, server):
    declare(repo)
    serve(server)
    run(repo, server)
    before = repo.lock_file.read_bytes()
    (repo.raw / "test" / "data.bin").unlink()
    serve(server, data=DATA + b"changed")
    with pytest.raises(PipelineError, match="does not match the lock file"):
        run(repo, server)
    assert repo.lock_file.read_bytes() == before
    assert run(repo, server, update=True).actions == [("S-TEST-A/data.bin", "repinned")]
    assert load_lock(repo.lock_file)[("S-TEST-A", "data.bin")].size == len(DATA) + len(b"changed")


def test_update_of_an_unchanged_file_keeps_its_retrieval_record(repo, server):
    declare(repo)
    serve(server)
    run(repo, server)
    before = repo.lock_file.read_bytes()
    result = fetch(repo, opener=server.open, now=lambda: "2030-01-01T00:00:00Z", log=quiet, update=True)
    assert result.actions == [("S-TEST-A/data.bin", "verified")]
    assert repo.lock_file.read_bytes() == before


def test_stale_lock_entries_need_update(repo, server):
    declare(repo)
    serve(server)
    run(repo, server)
    write_sources(repo, [])
    with pytest.raises(PipelineError, match="no longer declares"):
        run(repo, server)
    run(repo, server, update=True)
    assert load_lock(repo.lock_file) == {}


def test_verify_reports_modified_and_missing_files(repo, server):
    declare(repo)
    serve(server)
    run(repo, server)
    path = repo.raw / "test" / "data.bin"
    path.write_bytes(DATA[:-1] + b"X")
    assert any("does not match" in p for p in verify(repo, log=quiet))
    path.unlink()
    assert any("missing" in p for p in verify(repo, log=quiet))


def test_non_https_downloads_are_refused():
    from usim_pipeline.fetch import https_open

    with pytest.raises(PipelineError, match="non-HTTPS"):
        with https_open("http://example.invalid/file"):
            pass


def test_lock_digest_ignores_formatting(repo, server):
    declare(repo)
    serve(server)
    run(repo, server)
    digest = lock_digest(repo.lock_file)
    document = json.loads(repo.lock_file.read_text())
    repo.lock_file.write_text(json.dumps(document, separators=(",", ":")).replace("\n", "\r\n"))
    assert lock_digest(repo.lock_file) == digest
