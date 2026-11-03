"""Surface map textures for the Unity viewer (P6-07, ADR-0014). Visual assets, not simulation data.

Each map in data/catalog/textures.json is a global equirectangular image from a registered source (pinned in the
lock file). It is reprojected to the viewer's convention and written as a JPEG, together with
ATTRIBUTION.md and a manifest the Unity host reads:

- columns: east longitude, the left edge at -180 degrees (180 E = 180 W), each column 360 / width degrees;
- rows: planetocentric latitude, the top edge at +90 degrees, each row 180 / height degrees.

The map's own geometry comes from its label: an ISIS cube label (USGS mosaics; SimpleCylindrical and
Equirectangular with center latitude 0, as ISIS defines them: x = R (lon_east - center_east), y = R lat, and
sample s at the center of x = UpperLeftCornerX + (s - 0.5) PixelResolution, lines likewise from
UpperLeftCornerY downward), GeoTIFF tags (geographic, in degrees), or, for a source without a machine-readable
label, values transcribed from its page with a locator ("manual"). Planetographic latitudes are converted with
the map's ellipsoid: tan(planetographic) = (a / c)^2 tan(planetocentric).

Resampling averages the source over each output pixel's area (exact box filter in the source's pixel
coordinates, wrapping in longitude), so a reduction by any factor neither aliases nor shifts the map. Large
USGS images are read through their PDS3 label as memory-mapped band-sequential bytes (cross-checked against the
TIFF's own header), in row chunks.
"""

from __future__ import annotations

import io
import json
import math
import re
import zipfile
from collections.abc import Callable
from dataclasses import dataclass
from pathlib import Path

import numpy
from PIL import Image

from . import PipelineError
from .fetch import locked_file
from .jsonio import read_json
from .repo import Repo
from .sources import Source, find_source, load_sources

SCHEMA_VERSION = 1
MANIFEST_NAME = "BodyTextures.json"
ATTRIBUTION_PATH = "UniverseSim/Assets/UniverseSim/ATTRIBUTION.md"
ROW_CHUNK = 256  # source rows per horizontal pass
COLUMN_CHUNK = 256  # output columns per vertical pass
# A map may fall short of 360 degrees of longitude by this fraction of one of its pixels: labels round the pixel
# size (Ganymede's PixelResolution of 1435.721462 m is 1.1e-8 below its Scale of 32 pixels per degree, leaving
# its 11520 columns 1.3e-4 of a pixel short). The gap takes the last column's value.
SPAN_TOLERANCE_PIXELS = 1.0e-3

# Pillow's decompression bomb guard: the images it decodes here are at most 5400 x 2700 pixels. The large USGS
# TIFFs are only opened to read their headers (pds3_raster), with the guard lifted for that.
DECODE_LIMIT_PIXELS = 64 * 1024 * 1024


@dataclass(frozen=True)
class Geometry:
    """Where a map's pixels lie, in east longitude and in the map's latitude type, degrees."""

    first_column_longitude_deg: float  # the west edge (area registration) or center (point) of column 0
    first_row_latitude_deg: float  # the north edge (area) or center (point) of row 0
    pixel_deg: float  # pixel size in longitude and latitude
    registration: str  # "area" or "point"
    planetographic: bool  # latitudes are planetographic (geodetic) rather than planetocentric
    radii_km: tuple[float, float] | None = None  # (equatorial, polar), for planetographic latitudes
    locator: str = ""

    @property
    def west_edge_deg(self) -> float:
        return self.first_column_longitude_deg - (self.pixel_deg / 2 if self.registration == "point" else 0.0)

    @property
    def north_edge_deg(self) -> float:
        return self.first_row_latitude_deg + (self.pixel_deg / 2 if self.registration == "point" else 0.0)


@dataclass(frozen=True)
class TextureEntry:
    body: int
    name: str
    source_id: str
    image_file: str
    member: str | None  # a file inside a zip archive
    layout_file: str | None  # PDS3 label of a band-sequential image (USGS)
    geometry: dict
    content: str
    credit: str
    page: str

    @property
    def output_name(self) -> str:
        return f"body-{self.body}.jpg"


@dataclass(frozen=True)
class TextureManifest:
    directory: str
    width: int
    height: int
    jpeg_quality: int
    entries: tuple[TextureEntry, ...]


def load_manifest(path: Path) -> TextureManifest:
    data = read_json(path)
    where = str(path)
    if not isinstance(data, dict) or data.get("schema_version") != SCHEMA_VERSION:
        raise PipelineError(f"{where}: schema_version must be {SCHEMA_VERSION}")
    output = data.get("output")
    if not isinstance(output, dict) or set(output) != {"directory", "width", "height", "jpeg_quality"}:
        raise PipelineError(f"{where}: output needs directory, width, height and jpeg_quality")
    width, height, quality = output["width"], output["height"], output["jpeg_quality"]
    if not (isinstance(width, int) and isinstance(height, int) and width == 2 * height > 0):
        raise PipelineError(f"{where}: the output must be twice as wide as high")
    if not (isinstance(quality, int) and 1 <= quality <= 95):
        raise PipelineError(f"{where}: jpeg_quality must be 1 to 95")
    entries = []
    keys = {"body", "name", "image", "geometry", "content", "credit", "page"}
    for i, item in enumerate(data.get("textures", [])):
        at = f"{where}: textures[{i}]"
        if not isinstance(item, dict) or set(item) != keys:
            raise PipelineError(f"{at}: needs exactly {sorted(keys)}")
        image = item["image"]
        if not isinstance(image, dict) or not {"source_id", "file"} <= set(image) <= {"source_id", "file", "member", "layout"}:
            raise PipelineError(f"{at}: image needs source_id and file, and optionally member or layout")
        if not isinstance(item["body"], int) or not all(isinstance(item[k], str) and item[k] for k in ("name", "content", "credit", "page")):
            raise PipelineError(f"{at}: body must be a NAIF ID and name, content, credit and page non-empty text")
        if not isinstance(item["geometry"], dict) or item["geometry"].get("kind") not in ("isis", "geotiff", "manual"):
            raise PipelineError(f"{at}: geometry kind must be isis, geotiff or manual")
        entries.append(
            TextureEntry(
                body=item["body"], name=item["name"], source_id=image["source_id"], image_file=image["file"],
                member=image.get("member"), layout_file=image.get("layout"), geometry=item["geometry"],
                content=item["content"], credit=item["credit"], page=item["page"],
            )
        )  # fmt: skip
    if len({e.body for e in entries}) != len(entries):
        raise PipelineError(f"{where}: two maps for one body")
    return TextureManifest(output["directory"], width, height, quality, tuple(entries))


# --- Geometry ---------------------------------------------------------------------------------------------


def _pvl_number(text: str, key: str, where: str) -> float:
    match = re.search(rf"^\s*{key}\s*=\s*([-+0-9.eE]+)", text, flags=re.M)
    if match is None:
        raise PipelineError(f"{where}: {key} is missing")
    return float(match.group(1))


def _pvl_word(text: str, key: str, where: str) -> str:
    match = re.search(rf"^\s*{key}\s*=\s*\"?([A-Za-z_]+)\"?", text, flags=re.M)
    if match is None:
        raise PipelineError(f"{where}: {key} is missing")
    return match.group(1)


def isis_geometry(label: str, width: int, height: int, where: str) -> Geometry:
    """The geometry of an ISIS cube label's Mapping group (see the module documentation)."""
    found = re.search(r"Group = Mapping(.*?)End_Group", label, flags=re.S)
    if found is None:
        raise PipelineError(f"{where}: no Mapping group")
    mapping = found.group(1)
    projection = _pvl_word(mapping, "ProjectionName", where)
    if projection not in ("SimpleCylindrical", "Equirectangular"):
        raise PipelineError(f"{where}: projection {projection} is not SimpleCylindrical or Equirectangular")
    if projection == "Equirectangular" and re.search(r"^\s*CenterLatitude\s*=", mapping, flags=re.M):
        if _pvl_number(mapping, "CenterLatitude", where) != 0.0:
            raise PipelineError(f"{where}: Equirectangular with a center latitude other than 0")
    samples = int(_pvl_number(label, "Samples", where))
    lines = int(_pvl_number(label, "Lines", where))
    if (samples, lines) != (width, height):
        raise PipelineError(f"{where}: the label's {samples} x {lines} pixels differ from the image's {width} x {height}")
    direction = _pvl_word(mapping, "LongitudeDirection", where)
    if direction not in ("PositiveEast", "PositiveWest"):
        raise PipelineError(f"{where}: LongitudeDirection {direction}")
    latitude_type = _pvl_word(mapping, "LatitudeType", where)
    if latitude_type not in ("Planetocentric", "Planetographic"):
        raise PipelineError(f"{where}: LatitudeType {latitude_type}")
    equatorial_m = _pvl_number(mapping, "EquatorialRadius", where)
    polar_m = _pvl_number(mapping, "PolarRadius", where)
    center = _pvl_number(mapping, "CenterLongitude", where)
    center_east = -center if direction == "PositiveWest" else center
    resolution_m = _pvl_number(mapping, "PixelResolution", where)
    upper_left_x = _pvl_number(mapping, "UpperLeftCornerX", where)
    upper_left_y = _pvl_number(mapping, "UpperLeftCornerY", where)
    degrees_per_meter = 180.0 / (math.pi * equatorial_m)
    return Geometry(
        first_column_longitude_deg=center_east + upper_left_x * degrees_per_meter,
        first_row_latitude_deg=upper_left_y * degrees_per_meter,
        pixel_deg=resolution_m * degrees_per_meter,
        registration="area",
        planetographic=latitude_type == "Planetographic",
        radii_km=(equatorial_m / 1000.0, polar_m / 1000.0),
        locator="ISIS Mapping group",
    )


GEOKEY_MODEL_TYPE, GEOKEY_RASTER_TYPE, GEOKEY_ANGULAR_UNITS = 1024, 1025, 2054
GEOKEY_SEMI_MAJOR, GEOKEY_SEMI_MINOR, GEOKEY_INVERSE_FLATTENING, GEOKEY_PRIME_MERIDIAN = 2057, 2058, 2059, 2061
MODEL_TYPE_GEOGRAPHIC, RASTER_PIXEL_IS_AREA, RASTER_PIXEL_IS_POINT, ANGULAR_UNIT_DEGREE = 2, 1, 2, 9102


def _geokeys(image: Image.Image, where: str) -> dict[int, float | int]:
    tags = getattr(image, "tag_v2", None)
    if tags is None or 34735 not in tags:
        raise PipelineError(f"{where}: no GeoTIFF key directory")
    def values(tag: int) -> tuple:
        value = tags.get(tag, ())
        return tuple(value) if isinstance(value, (tuple, list)) else (value,)  # Pillow gives one value as a scalar

    directory = values(34735)
    doubles = values(34736)
    keys: dict[int, float | int] = {}
    for i in range(directory[3]):
        key, location, count, offset = directory[4 + 4 * i : 8 + 4 * i]
        if location == 0:
            keys[key] = offset
        elif location == 34736 and count == 1:
            keys[key] = doubles[offset]
    return keys


def geotiff_geometry(image: Image.Image, where: str) -> Geometry:
    """The geometry of a geographic GeoTIFF in degrees (tie point, pixel scale and GeoKeys)."""
    keys = _geokeys(image, where)
    if keys.get(GEOKEY_MODEL_TYPE) != MODEL_TYPE_GEOGRAPHIC or keys.get(GEOKEY_ANGULAR_UNITS) != ANGULAR_UNIT_DEGREE:
        raise PipelineError(f"{where}: not a geographic GeoTIFF in degrees")
    if keys.get(GEOKEY_PRIME_MERIDIAN, 0.0) != 0.0:
        raise PipelineError(f"{where}: prime meridian other than 0")
    raster = keys.get(GEOKEY_RASTER_TYPE, RASTER_PIXEL_IS_AREA)
    if raster not in (RASTER_PIXEL_IS_AREA, RASTER_PIXEL_IS_POINT):
        raise PipelineError(f"{where}: raster type {raster}")
    scale_x, scale_y, _ = image.tag_v2[33550]
    i, j, _, x, y, _ = image.tag_v2[33922]
    if scale_x != scale_y or scale_x <= 0:
        raise PipelineError(f"{where}: pixels must be square in degrees")
    a = keys.get(GEOKEY_SEMI_MAJOR)
    if a is None:
        raise PipelineError(f"{where}: no ellipsoid semi-major axis")
    if GEOKEY_SEMI_MINOR in keys:
        c = float(keys[GEOKEY_SEMI_MINOR])
    else:
        inverse_flattening = float(keys.get(GEOKEY_INVERSE_FLATTENING, 0.0))
        c = float(a) * (1.0 - 1.0 / inverse_flattening) if inverse_flattening else float(a)
    return Geometry(
        first_column_longitude_deg=x - i * scale_x,
        first_row_latitude_deg=y + j * scale_y,
        pixel_deg=scale_x,
        registration="point" if raster == RASTER_PIXEL_IS_POINT else "area",
        planetographic=c != a,  # geographic latitude on an ellipsoid is geodetic (planetographic)
        radii_km=(float(a) / 1000.0, c / 1000.0),
        locator="GeoTIFF tie point, pixel scale and GeoKeys",
    )


def manual_geometry(spec: dict, width: int, height: int, where: str) -> Geometry:
    keys = {"kind", "first_column_longitude_deg", "first_row_latitude_deg", "registration", "latitude", "locator"}
    if set(spec) != keys or spec["registration"] not in ("area", "point") or spec["latitude"] != "planetocentric":
        raise PipelineError(f"{where}: manual geometry needs {sorted(keys)}, area or point registration, planetocentric")
    columns = width if spec["registration"] == "area" else width - 1
    return Geometry(
        first_column_longitude_deg=float(spec["first_column_longitude_deg"]),
        first_row_latitude_deg=float(spec["first_row_latitude_deg"]),
        pixel_deg=360.0 / columns,
        registration=spec["registration"],
        planetographic=False,
        locator=str(spec["locator"]),
    )


# --- Images -----------------------------------------------------------------------------------------------


class _Raster:
    """A source image: its size, bands, and rows as float64 (rows, width, bands)."""

    def __init__(self, width: int, height: int, bands: int, read: Callable[[int, int], numpy.ndarray]) -> None:
        self.width, self.height, self.bands = width, height, bands
        self._read = read

    def rows(self, start: int, stop: int) -> numpy.ndarray:
        return self._read(start, stop)


def _pds3_value(label: str, key: str, where: str) -> str:
    match = re.search(rf"^\s*{key}\s*=\s*(.+?)\s*$", label, flags=re.M)
    if match is None:
        raise PipelineError(f"{where}: {key} is missing")
    return match.group(1)


def pds3_raster(image_path: Path, label: str, where: str) -> _Raster:
    """An 8-bit band-sequential image whose bytes start at the PDS3 label's ^IMAGE pointer, memory-mapped. PDS3
    byte pointers count from 1. The TIFF header must agree: same size, no compression, and strips that are
    contiguous from that offset (band after band)."""
    pointer = re.search(r'^\s*\^IMAGE\s*=\s*\(\s*"([^"]+)"\s*,\s*(\d+)\s*<BYTES>\s*\)', label, flags=re.M)
    if pointer is None or pointer.group(1) != image_path.name:
        raise PipelineError(f"{where}: ^IMAGE does not point into {image_path.name}")
    offset = int(pointer.group(2)) - 1  # the label's byte pointer counts from 1
    lines, samples = int(_pds3_value(label, "LINES", where)), int(_pds3_value(label, "LINE_SAMPLES", where))
    bands = int(_pds3_value(label, "BANDS", where))
    if (_pds3_value(label, "SAMPLE_BITS", where), _pds3_value(label, "SAMPLE_TYPE", where)) != ("8", "LSB_UNSIGNED_INTEGER"):
        raise PipelineError(f"{where}: only 8-bit unsigned samples are supported")
    if bands not in (1, 3) or (bands == 3 and _pds3_value(label, "BAND_STORAGE_TYPE", where) != "BAND_SEQUENTIAL"):
        raise PipelineError(f"{where}: one band, or three band-sequential bands")
    guard, Image.MAX_IMAGE_PIXELS = Image.MAX_IMAGE_PIXELS, None  # header only: nothing is decoded
    try:
        tiff = Image.open(image_path)
    finally:
        Image.MAX_IMAGE_PIXELS = guard
    with tiff:
        tags = tiff.tag_v2
        strips = numpy.asarray(tags.get(273, ()), dtype=numpy.int64).reshape(-1)
        counts = numpy.asarray(tags.get(279, ()), dtype=numpy.int64).reshape(-1)
        contiguous = (
            len(strips) == len(counts) > 0 and strips[0] == offset and counts.sum() == bands * lines * samples
            and bool(numpy.all(strips[1:] == strips[:-1] + counts[:-1]))
        )  # fmt: skip
        if tiff.size != (samples, lines) or tags.get(259, 1) != 1 or not contiguous:
            raise PipelineError(f"{where}: the TIFF header does not match the PDS3 label (size, compression, offset)")
        if bands == 3 and tags.get(284, 1) != 2:
            raise PipelineError(f"{where}: a three-band TIFF must be stored by plane")
    if image_path.stat().st_size < offset + bands * lines * samples:
        raise PipelineError(f"{where}: the file is shorter than the label says")
    data = numpy.memmap(image_path, dtype=numpy.uint8, mode="r", offset=offset, shape=(bands, lines, samples))

    def read(start: int, stop: int) -> numpy.ndarray:
        return numpy.moveaxis(numpy.asarray(data[:, start:stop, :], dtype=numpy.float64), 0, -1)

    return _Raster(samples, lines, bands, read)


def pillow_raster(image: Image.Image) -> _Raster:
    """A whole image decoded by Pillow (the small sources): RGB or grayscale, 8 bits per sample."""
    if image.mode not in ("RGB", "L"):
        raise PipelineError(f"image mode {image.mode} is not RGB or L")
    array = numpy.asarray(image)
    if array.ndim == 2:
        array = array[:, :, None]
    width, height = image.size

    def read(start: int, stop: int) -> numpy.ndarray:
        return array[start:stop].astype(numpy.float64)

    return _Raster(width, height, array.shape[2], read)


# --- Resampling -------------------------------------------------------------------------------------------


def _periodic_integral(cumulative: numpy.ndarray, values: numpy.ndarray, period: float, u: numpy.ndarray) -> numpy.ndarray:
    """The integral from 0 to each u (pixels) of the piecewise constant `values` (axis 1), repeated with a
    `period` of 360 degrees in pixels, which need not be whole: pixels past it (a map wider than 360
    degrees) are not used, and a gap before it (a map a fraction of a pixel short) takes the last pixel's
    value, in each turn as in the integral of a whole turn, so the seam is continuous."""
    n = values.shape[1]
    last = min(int(math.floor(period)), n - 1)
    per_turn = cumulative[:, last : last + 1, :] + (period - last) * values[:, last : last + 1, :]
    turns = numpy.floor(u / period)
    rest = u - turns * period
    k = numpy.minimum(numpy.floor(rest).astype(numpy.int64), n - 1)
    fraction = rest - k
    return turns[None, :, None] * per_turn + cumulative[:, k, :] + fraction[None, :, None] * values[:, k, :]


def _bounded_integral(cumulative: numpy.ndarray, values: numpy.ndarray, v: numpy.ndarray) -> numpy.ndarray:
    """The integral of the piecewise constant `values` (axis 0, m pixels) from 0 to each v in [0, m]."""
    m = values.shape[0]
    k = numpy.minimum(numpy.floor(v).astype(numpy.int64), m - 1)
    fraction = v - k
    return cumulative[k] + fraction[:, None, None] * values[k]


def reproject(raster: _Raster, geometry: Geometry, width: int, height: int, where: str) -> numpy.ndarray:
    """The map on the viewer's grid (module documentation), as float64 (height, width, bands)."""
    p = geometry.pixel_deg
    period = 360.0 / p  # pixels; a map may be up to two pixels wider (the last columns repeat the first)
    if not (period - SPAN_TOLERANCE_PIXELS <= raster.width <= period + 2.0):
        raise PipelineError(f"{where}: {raster.width} columns of {p} degrees do not cover 360 degrees")
    columns = min(raster.width, math.ceil(period - 1e-9))
    west = geometry.west_edge_deg
    boundaries = -180.0 + numpy.arange(width + 1) * (360.0 / width)
    u = (boundaries - west) / p
    u = u - numpy.floor(u[0] / period) * period  # start within the first period (values stay increasing)

    # Horizontal pass, in chunks of source rows: average each output column's longitude span.
    across = numpy.empty((raster.height, width, raster.bands), dtype=numpy.float32)
    for start in range(0, raster.height, ROW_CHUNK):
        stop = min(start + ROW_CHUNK, raster.height)
        values = raster.rows(start, stop)[:, :columns, :]
        cumulative = numpy.concatenate([numpy.zeros_like(values[:, :1]), numpy.cumsum(values, axis=1)], axis=1)
        integral = _periodic_integral(cumulative, values, period, u)
        across[start:stop] = (integral[:, 1:] - integral[:, :-1]) / numpy.diff(u)[None, :, None]

    # Vertical pass: each output row's planetocentric latitude span, as rows of the source.
    centric = numpy.radians(90.0 - numpy.arange(height + 1) * (180.0 / height))
    if geometry.planetographic and geometry.radii_km is not None:
        a, c = geometry.radii_km
        latitude = numpy.degrees(numpy.arctan((a / c) ** 2 * numpy.tan(centric)))
        latitude[0], latitude[-1] = 90.0, -90.0  # the poles (tan overflows to a finite value)
    else:
        latitude = numpy.degrees(centric)
    v = numpy.clip((geometry.north_edge_deg - latitude) / p, 0.0, float(raster.height))
    if numpy.any(numpy.diff(v) <= 0.0):
        raise PipelineError(f"{where}: the map does not cover the whole latitude range")
    out = numpy.empty((height, width, raster.bands), dtype=numpy.float64)
    for start in range(0, width, COLUMN_CHUNK):
        stop = min(start + COLUMN_CHUNK, width)
        values = across[:, start:stop, :].astype(numpy.float64)
        cumulative = numpy.concatenate([numpy.zeros_like(values[:1]), numpy.cumsum(values, axis=0)], axis=0)
        integral = _bounded_integral(cumulative, values, v)
        out[:, start:stop] = (integral[1:] - integral[:-1]) / numpy.diff(v)[:, None, None]
    return out


def to_rgb8(image: numpy.ndarray) -> numpy.ndarray:
    """Rounded to 8 bits, grayscale repeated in three channels."""
    rounded = numpy.clip(numpy.floor(image + 0.5), 0.0, 255.0).astype(numpy.uint8)
    return numpy.repeat(rounded, 3, axis=2) if rounded.shape[2] == 1 else rounded


def encode_jpeg(rgb: numpy.ndarray, quality: int) -> bytes:
    buffer = io.BytesIO()
    Image.fromarray(rgb, "RGB").save(buffer, format="JPEG", quality=quality, subsampling=0, optimize=False)
    return buffer.getvalue()


# --- The command ------------------------------------------------------------------------------------------


@dataclass(frozen=True)
class BuiltTexture:
    entry: TextureEntry
    geometry: Geometry
    source_width: int
    source_height: int
    jpeg: bytes
    files: tuple[tuple[str, str, int, str], ...]  # (file, sha256, size, URL) of the pinned inputs


def build_texture(repo: Repo, sources: tuple[Source, ...], manifest: TextureManifest, entry: TextureEntry) -> BuiltTexture:
    where = f"texture of naif:{entry.body} ({entry.name})"
    image_path, image_lock = locked_file(repo, sources, entry.source_id, entry.image_file)
    files = [(image_lock.name, image_lock.sha256, image_lock.size, image_lock.url)]
    spec = entry.geometry
    opened: Image.Image | None = None
    try:
        if entry.layout_file is not None:
            layout_path, layout_lock = locked_file(repo, sources, entry.source_id, entry.layout_file)
            files.append((layout_lock.name, layout_lock.sha256, layout_lock.size, layout_lock.url))
            raster = pds3_raster(image_path, layout_path.read_text(encoding="utf-8", errors="strict"), where)
        else:
            Image.MAX_IMAGE_PIXELS = DECODE_LIMIT_PIXELS
            if entry.member is not None:
                with zipfile.ZipFile(image_path) as archive:
                    opened = Image.open(io.BytesIO(archive.read(entry.member)))
            else:
                opened = Image.open(image_path)
            opened.load()
            raster = pillow_raster(opened)
        if spec["kind"] == "isis":
            label_path, label_lock = locked_file(repo, sources, entry.source_id, spec["label"])
            files.append((label_lock.name, label_lock.sha256, label_lock.size, label_lock.url))
            geometry = isis_geometry(label_path.read_text(encoding="utf-8"), raster.width, raster.height, where)
        elif spec["kind"] == "geotiff":
            if opened is None:
                raise PipelineError(f"{where}: GeoTIFF geometry needs an image read by Pillow")
            geometry = geotiff_geometry(opened, where)
        else:
            geometry = manual_geometry(spec, raster.width, raster.height, where)
        pixels = reproject(raster, geometry, manifest.width, manifest.height, where)
    finally:
        if opened is not None:
            opened.close()
    return BuiltTexture(entry, geometry, raster.width, raster.height, encode_jpeg(to_rgb8(pixels), manifest.jpeg_quality), tuple(files))


def _unity_manifest(manifest: TextureManifest, built: list[BuiltTexture]) -> str:
    """The manifest the Unity host reads (UnityEngine.JsonUtility: one object with an array)."""
    textures = [
        {
            "naifId": b.entry.body, "name": b.entry.name, "file": b.entry.output_name, "content": b.entry.content,
            "credit": b.entry.credit, "sourceId": b.entry.source_id, "page": b.entry.page,
        }
        for b in built
    ]  # fmt: skip
    return json.dumps({"textures": textures}, indent=2, ensure_ascii=False) + "\n"


def _attribution(manifest: TextureManifest, built: list[BuiltTexture], sources: tuple[Source, ...]) -> str:
    lines = [
        "# Attribution of visual assets",
        "",
        "Generated by `tools/pipeline/usim-pipeline textures` from `data/catalog/textures.json`. Do not edit by hand.",
        "",
        "The surface maps below are rendering assets, not simulation data (P6-07, [ADR-0014](../../../docs/adr/0014-surface-map-textures.md)).",
        f"Each was reprojected from the pinned source file to {manifest.width} x {manifest.height} pixels (equirectangular,",
        "planetocentric latitude, east longitude, left edge at 180 degrees) by averaging the source over each pixel,",
        f"and saved as a JPEG of quality {manifest.jpeg_quality}. Grayscale maps are stored as RGB.",
        "",
        "Bodies without a map (Venus, Saturn, Uranus, Neptune, Titan, Deimos and the Uranian moons) have no",
        "visible-light global mosaic from a verified source and are drawn as plain lit spheres.",
        "",
    ]
    for b in built:
        source = find_source(sources, b.entry.source_id)
        g = b.geometry
        latitude = "planetographic, converted with the map's ellipsoid" if g.planetographic else "planetocentric"
        lines += [
            f"## {b.entry.name} (`Textures/Bodies/{b.entry.output_name}`)",
            "",
            f"- Content: {b.entry.content}.",
            f"- Credit: {b.entry.credit}.",
            f"- Source: {source.id}, {source.title}. {source.provider}.",
            f"- Page: {b.entry.page}",
            *(f"- File: `{name}` ({size} bytes, SHA-256 {sha}), {url}" for name, sha, size, url in b.files),
            f"- Geometry: {b.source_width} x {b.source_height} pixels of {g.pixel_deg:.9g} degrees, {g.registration} registration, "
            f"first column at {g.first_column_longitude_deg:.9g} degrees east, first row at {g.first_row_latitude_deg:.9g} degrees, "
            f"{latitude} ({g.locator}).",
            f"- Terms: {source.license}",
            "",
        ]
    return "\n".join(lines)


def build_textures(repo: Repo, log: Callable[[str], None] = print) -> list[Path]:
    """Writes the textures of data/catalog/textures.json, the Unity manifest and ATTRIBUTION.md. Every source
    must be fetched (usim-pipeline fetch --optional)."""
    sources = load_sources(repo.sources_file)
    manifest = load_manifest(repo.catalog / "textures.json")
    out_dir = repo.root / manifest.directory
    out_dir.mkdir(parents=True, exist_ok=True)
    built = []
    written = []
    for entry in manifest.entries:
        texture = build_texture(repo, sources, manifest, entry)
        built.append(texture)
        path = out_dir / entry.output_name
        path.write_bytes(texture.jpeg)
        written.append(path)
        log(f"wrote {path} ({len(texture.jpeg)} bytes) from {entry.image_file}")
    (out_dir / MANIFEST_NAME).write_text(_unity_manifest(manifest, built), encoding="utf-8")
    # The surface maps, then the artistic assets (A-04), the section `usim-pipeline attribution` rewrites alone.
    from .artistic import attribution_section, load_artistic_assets, splice_attribution

    text = splice_attribution(_attribution(manifest, built, sources), attribution_section(load_artistic_assets(repo)))
    (repo.root / ATTRIBUTION_PATH).write_text(text, encoding="utf-8")
    written += [out_dir / MANIFEST_NAME, repo.root / ATTRIBUTION_PATH]
    log(f"wrote {out_dir / MANIFEST_NAME} and {repo.root / ATTRIBUTION_PATH}")
    return written
