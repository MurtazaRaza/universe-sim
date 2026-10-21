"""data/reference/horizons/: captures of the JPL Horizons API for VAL-02 (end-to-end planetary states).

Each file starts with '#' lines recording the full request URL, the API signature and the retrieval time,
followed by the Horizons result text verbatim. Rows between $$SOE and $$EOE are CSV: JDTDB, calendar date
(TDB), X, Y, Z (km), VX, VY, VZ (km/s); geometric states in the ICRF relative to the Solar System
Barycenter. Horizons computes planetary states from DE441 (stated in each result's header).
"""

from __future__ import annotations

import datetime
import json
import time
import urllib.parse
from collections.abc import Callable
from pathlib import Path

from .. import PipelineError
from ..fetch import https_open
from ..repo import Repo

API = "https://ssd.jpl.nasa.gov/api/horizons.api"
TARGETS = (10, 1, 2, 3, 4, 5, 6, 7, 8, 9, 199, 299, 399, 301)
CENTER = "500@0"
START, STOP, STEP = "1900-01-01 00:00", "2100-01-01 00:00", "397 d"
PAUSE_S = 1.0  # between requests, to be gentle with the service


def request_url(target: int) -> str:
    params = {
        "format": "json",
        "COMMAND": f"'{target}'",
        "OBJ_DATA": "'NO'",
        "MAKE_EPHEM": "'YES'",
        "EPHEM_TYPE": "'VECTORS'",
        "CENTER": f"'{CENTER}'",
        "START_TIME": f"'{START}'",
        "STOP_TIME": f"'{STOP}'",
        "STEP_SIZE": f"'{STEP}'",
        "TIME_TYPE": "'TDB'",
        "REF_SYSTEM": "'ICRF'",
        "REF_PLANE": "'FRAME'",
        "VEC_TABLE": "'2'",
        "VEC_CORR": "'NONE'",
        "OUT_UNITS": "'KM-S'",
        "CSV_FORMAT": "'YES'",
        "VEC_LABELS": "'NO'",
    }
    return API + "?" + urllib.parse.urlencode(params, quote_via=urllib.parse.quote)


def capture(repo: Repo, opener=https_open, log: Callable[[str], None] = print) -> list[Path]:
    out_dir = repo.reference / "horizons"
    out_dir.mkdir(parents=True, exist_ok=True)
    written = []
    for index, target in enumerate(TARGETS):
        if index:
            time.sleep(PAUSE_S)
        url = request_url(target)
        with opener(url) as response:
            document = json.loads(response.read().decode("utf-8"))
        result = document.get("result", "")
        if "error" in document or "$$SOE" not in result or "$$EOE" not in result:
            raise PipelineError(f"Horizons query for {target} failed: {document.get('error', 'no ephemeris in result')}")
        retrieved = datetime.datetime.now(datetime.UTC).strftime("%Y-%m-%dT%H:%M:%SZ")
        signature = document.get("signature", {})
        header = [
            f"VAL-02 reference: JPL Horizons API capture (S-JPL-HORIZONS), target {target} relative to the SSB.",
            "Captured by `tools/pipeline/usim-pipeline reference horizons`. Do not edit by hand.",
            f"Request: {url}",
            f"API signature: source {signature.get('source')!r}, version {signature.get('version')!r}",
            f"Retrieved: {retrieved}",
            "The Horizons result text follows verbatim.",
        ]
        path = out_dir / f"val02_{target}_wrt_0.txt"
        path.write_text("\n".join(f"# {line}" for line in header) + "\n" + result, encoding="utf-8")
        rows = result.split("$$SOE", 1)[1].split("$$EOE", 1)[0].strip().count("\n") + 1
        log(f"wrote {path} ({rows} states)")
        written.append(path)
    return written
