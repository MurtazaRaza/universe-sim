"""Command line: fetch, verify, build, info and reference (ARCHITECTURE section 4)."""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from . import PipelineError, __version__
from .repo import Repo


def _cmd_fetch(repo: Repo, args: argparse.Namespace) -> int:
    from .fetch import fetch

    fetch(repo, update=args.update, optional=args.optional)
    return 0


def _cmd_verify(repo: Repo, args: argparse.Namespace) -> int:
    from .fetch import verify

    return 1 if verify(repo) else 0


def _cmd_build(repo: Repo, args: argparse.Namespace) -> int:
    from .build import build

    build(repo, names=args.pack or None, output_dir=args.out)
    return 0


def _cmd_info(repo: Repo, args: argparse.Namespace) -> int:
    from .info import describe

    paths = args.packs or sorted(repo.packs.glob("*.uspack"))
    if not paths:
        raise PipelineError(f"no packs given and none found in {repo.packs}")
    for index, path in enumerate(paths):
        if index:
            print()
        describe(path)
    return 0


def _cmd_reference(repo: Repo, args: argparse.Namespace) -> int:
    targets = ["unit-fixtures", "test-pack", "spice"] if args.target == "offline" else [args.target]
    for target in targets:
        if target == "unit-fixtures":
            from .reference.unit_fixtures import generate as unit_fixtures

            unit_fixtures(repo)
        elif target == "test-pack":
            from .reference.test_pack import generate as test_pack

            test_pack(repo)
        elif target == "spice":
            from .reference.spice_vectors import generate as spice

            spice(repo)
        else:
            from .reference.horizons import capture

            capture(repo)
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="usim-pipeline", description=__doc__)
    parser.add_argument("--version", action="version", version=f"usim-pipeline {__version__}")
    parser.add_argument("--root", type=Path, default=None, help="repository root (default: this checkout)")
    commands = parser.add_subparsers(dest="command", required=True, metavar="command")

    fetch = commands.add_parser("fetch", help="download declared files and pin them in data/sources.lock.json")
    fetch.add_argument("--update", action="store_true", help="re-pin files whose lock entries no longer match")
    fetch.add_argument(
        "--optional", action="store_true", help="also download optional files (large; only optional packs need them)"
    )
    fetch.set_defaults(run=_cmd_fetch)

    verify = commands.add_parser("verify", help="check data/raw against the lock file")
    verify.set_defaults(run=_cmd_verify)

    build = commands.add_parser(
        "build", help="build the packs defined in data/catalog/packs.json (optional packs only when named)"
    )
    build.add_argument("--pack", action="append", help="build only this pack (repeatable)")
    build.add_argument("--out", type=Path, default=None, help="output directory (default: data/packs)")
    build.set_defaults(run=_cmd_build)

    info = commands.add_parser("info", help="print the contents of packs (default: all in data/packs)")
    info.add_argument("packs", nargs="*", type=Path)
    info.set_defaults(run=_cmd_info)

    reference = commands.add_parser(
        "reference",
        help="generate reference data: unit-fixtures, test-pack, spice (all three: offline) or horizons (network)",
    )
    reference.add_argument("target", choices=["unit-fixtures", "test-pack", "spice", "offline", "horizons"])
    reference.set_defaults(run=_cmd_reference)

    args = parser.parse_args(argv)
    repo = Repo(args.root.resolve()) if args.root else Repo.default()
    try:
        return int(args.run(repo, args))
    except PipelineError as error:
        print(f"error: {error}", file=sys.stderr)
        return 1
