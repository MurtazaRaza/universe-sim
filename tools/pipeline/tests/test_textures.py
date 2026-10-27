"""Surface map textures (P6-07): the manifest, the three ways a map states its geometry (ISIS label, GeoTIFF
tags, transcribed values), the PDS3 memory-mapped layout, and the reprojection to the viewer's grid. The maps
here are synthetic: a smooth function of longitude and latitude sampled at each source pixel's position, so a
wrong longitude direction, edge, registration or latitude type shows up as a mismatch on the output grid.
"""

from __future__ import annotations

import io
import json
import math
from pathlib import Path

import numpy
import pytest
from PIL import Image, TiffImagePlugin, TiffTags

from usim_pipeline import PipelineError
from usim_pipeline.sources import load_sources
from usim_pipeline.textures import (
    Geometry,
    encode_jpeg,
    geotiff_geometry,
    isis_geometry,
    load_manifest,
    manual_geometry,
    pds3_raster,
    pillow_raster,
    reproject,
    to_rgb8,
)

from .conftest import REAL_REPO

OUT_W, OUT_H = 64, 32
RADIUS_M = 100000.0


def surface(longitude_deg: numpy.ndarray, latitude_deg: numpy.ndarray) -> numpy.ndarray:
    """A smooth, asymmetric test pattern (east longitude, planetocentric latitude, degrees), 0 to 255."""
    lon, lat = numpy.radians(longitude_deg), numpy.radians(latitude_deg)
    return 128.0 + 70.0 * numpy.cos(lat) * numpy.sin(lon - 0.5) + 30.0 * numpy.sin(lat) + 10.0 * numpy.sin(2 * lon) * numpy.cos(lat)


def expected_output() -> numpy.ndarray:
    lon = -180.0 + (numpy.arange(OUT_W) + 0.5) * 360.0 / OUT_W
    lat = 90.0 - (numpy.arange(OUT_H) + 0.5) * 180.0 / OUT_H
    return surface(lon[None, :], lat[:, None])


def as_image(values: numpy.ndarray) -> Image.Image:
    return Image.fromarray(numpy.clip(numpy.floor(values + 0.5), 0, 255).astype(numpy.uint8), "L")


def isis_label(direction: str, center: float, upper_left_x: float, latitude_type: str = "Planetocentric",
               polar_m: float = RADIUS_M, width: int = 720, height: int = 360, step_deg: float = 0.5) -> str:
    resolution = RADIUS_M * math.radians(step_deg)
    return (
        "Object = IsisCube\n  Object = Core\n    Group = Dimensions\n"
        f"      Samples = {width}\n      Lines   = {height}\n      Bands   = 1\n    End_Group\n  End_Object\n"
        "  Group = Mapping\n    ProjectionName     = SimpleCylindrical\n"
        f"    CenterLongitude    = {center}\n    TargetName         = Test\n"
        f"    EquatorialRadius   = {RADIUS_M} <meters>\n    PolarRadius        = {polar_m} <meters>\n"
        f"    LatitudeType       = {latitude_type}\n    LongitudeDirection = {direction}\n"
        f"    UpperLeftCornerX   = {upper_left_x}\n    UpperLeftCornerY   = {RADIUS_M * math.pi / 2}\n"
        f"    PixelResolution    = {resolution} <meters/pixel>\n  End_Group\nEnd_Object\nEnd\n"
    )


def isis_source(direction: str, center: float, upper_left_x: float, latitude_of=lambda lat: lat) -> numpy.ndarray:
    """Samples the pattern where ISIS puts each pixel: x = R (lon_east - center_east), sample centers at
    x = UpperLeftCornerX + (s + 0.5) PixelResolution (ISIS SimpleCylindrical::SetGround, PFPixelMapper)."""
    resolution = RADIUS_M * math.radians(0.5)
    x = upper_left_x + (numpy.arange(720) + 0.5) * resolution
    center_east = -center if direction == "PositiveWest" else center
    lon_east = center_east + numpy.degrees(x / RADIUS_M)
    lat_map = 90.0 - (numpy.arange(360) + 0.5) * 0.5
    return surface(lon_east[None, :], latitude_of(lat_map)[:, None])


@pytest.mark.parametrize(
    "direction, center, upper_left_x",
    [
        ("PositiveWest", 180.0, 0.0),  # as Enceladus: the left edge at the center longitude
        ("PositiveEast", 0.0, -RADIUS_M * math.pi),  # as Tethys: centered on 0
        ("PositiveEast", 180.0, -RADIUS_M * math.pi),  # as Ganymede: centered on 180
        ("PositiveWest", 0.0, -RADIUS_M * math.pi),  # as Io
    ],
)
def test_isis_maps_land_on_the_viewer_grid(direction, center, upper_left_x):
    source = isis_source(direction, center, upper_left_x)
    geometry = isis_geometry(isis_label(direction, center, upper_left_x), 720, 360, "test")
    out = reproject(pillow_raster(as_image(source)), geometry, OUT_W, OUT_H, "test")[:, :, 0]
    assert numpy.abs(out - expected_output()).max() < 1.0
    # The same map mirrored east to west does not fit.
    wrong = reproject(pillow_raster(as_image(source[:, ::-1])), geometry, OUT_W, OUT_H, "test")[:, :, 0]
    assert numpy.abs(wrong - expected_output()).max() > 20.0


def test_planetographic_latitudes_are_converted_with_the_ellipsoid():
    polar = RADIUS_M / 1.25  # an exaggerated oblateness

    def centric(graphic_deg):
        return numpy.degrees(numpy.arctan((polar / RADIUS_M) ** 2 * numpy.tan(numpy.radians(graphic_deg))))

    source = isis_source("PositiveEast", 0.0, -RADIUS_M * math.pi, centric)
    label = isis_label("PositiveEast", 0.0, -RADIUS_M * math.pi, "Planetographic", polar)
    geometry = isis_geometry(label, 720, 360, "test")
    assert geometry.planetographic and geometry.radii_km == (RADIUS_M / 1000, polar / 1000)
    out = reproject(pillow_raster(as_image(source)), geometry, OUT_W, OUT_H, "test")[:, :, 0]
    assert numpy.abs(out - expected_output()).max() < 1.5
    as_centric = isis_geometry(isis_label("PositiveEast", 0.0, -RADIUS_M * math.pi), 720, 360, "test")
    wrong = reproject(pillow_raster(as_image(source)), as_centric, OUT_W, OUT_H, "test")[:, :, 0]
    assert numpy.abs(wrong - expected_output()).max() > 5.0


def test_point_registered_maps_drop_the_repeated_column():
    # As PIA07782: 3601 x 1801 pixels, the first column centered on 180 W, the last repeating it.
    lon = -180.0 + numpy.arange(721) * 0.5
    lat = 90.0 - numpy.arange(361) * 0.5
    source = surface(lon[None, :], lat[:, None])
    spec = {"kind": "manual", "first_column_longitude_deg": -180.0, "first_row_latitude_deg": 90.0,
            "registration": "point", "latitude": "planetocentric", "locator": "test"}  # fmt: skip
    geometry = manual_geometry(spec, 721, 361, "test")
    assert geometry.pixel_deg == 0.5 and geometry.west_edge_deg == -180.25 and geometry.north_edge_deg == 90.25
    out = reproject(pillow_raster(as_image(source)), geometry, OUT_W, OUT_H, "test")[:, :, 0]
    assert numpy.abs(out - expected_output()).max() < 1.0


def test_a_map_a_fraction_of_a_pixel_wider_than_360_degrees_uses_the_first_360():
    # As the New Horizons mosaics (35.255651 pixels per degree, 12693 columns): 719.4 pixels per turn here,
    # 720 columns, so the last 0.6 of a pixel repeats the start. Rows run past the south pole likewise.
    step = 360.0 / 719.4
    lon = -180.0 + (numpy.arange(720) + 0.5) * step
    lat = 90.0 - (numpy.arange(360) + 0.5) * step
    source = surface(lon[None, :], lat[:, None])
    geometry = Geometry(-180.0, 90.0, step, "area", False)
    out = reproject(pillow_raster(as_image(source)), geometry, OUT_W, OUT_H, "test")[:, :, 0]
    assert numpy.abs(out - expected_output()).max() < 1.0


def test_a_map_a_fraction_of_a_pixel_short_of_360_degrees_is_seamless():
    # As Ganymede, whose label's rounded pixel size leaves 11520 columns 1.3e-4 of a pixel short of 360
    # degrees: the gap takes the last column's value, so a uniform map stays uniform across the seam (here
    # inside output column 1, the map's west edge at 175 W).
    step = 360.0 / (720 + 5.0e-4)
    geometry = Geometry(-175.0, 90.0, step, "area", False)
    flat = reproject(pillow_raster(as_image(numpy.full((360, 720), 200.0))), geometry, OUT_W, OUT_H, "test")
    assert numpy.abs(flat - 200.0).max() < 1e-6
    lon = -175.0 + (numpy.arange(720) + 0.5) * step
    lat = 90.0 - (numpy.arange(360) + 0.5) * step
    source = surface(lon[None, :], lat[:, None])
    out = reproject(pillow_raster(as_image(source)), geometry, OUT_W, OUT_H, "test")[:, :, 0]
    assert numpy.abs(out - expected_output()).max() < 1.0
    too_short = Geometry(-175.0, 90.0, 360.0 / (720 + 2.0e-3), "area", False)
    with pytest.raises(PipelineError, match="360 degrees"):
        reproject(pillow_raster(as_image(source)), too_short, OUT_W, OUT_H, "test")


def test_a_map_must_cover_the_globe():
    geometry = Geometry(-180.0, 90.0, 0.5, "area", False)
    with pytest.raises(PipelineError, match="360 degrees"):
        reproject(pillow_raster(as_image(numpy.zeros((360, 700)))), geometry, OUT_W, OUT_H, "test")
    half = Geometry(-180.0, 90.0, 0.5, "area", False)
    with pytest.raises(PipelineError, match="latitude range"):
        reproject(pillow_raster(as_image(numpy.zeros((100, 720)))), half, OUT_W, OUT_H, "test")


def geotiff(values: numpy.ndarray, keys: list[int], doubles: list[float], tie: tuple, scale: float) -> Image.Image:
    info = TiffImagePlugin.ImageFileDirectory_v2()
    info[33550] = (scale, scale, 0.0)
    info.tagtype[33550] = TiffTags.DOUBLE
    info[33922] = tie
    info.tagtype[33922] = TiffTags.DOUBLE
    info[34735] = tuple(keys)
    info.tagtype[34735] = TiffTags.SHORT
    info[34736] = tuple(doubles)
    info.tagtype[34736] = TiffTags.DOUBLE
    buffer = io.BytesIO()
    as_image(values).save(buffer, format="TIFF", tiffinfo=info)
    buffer.seek(0)
    image = Image.open(buffer)
    image.load()
    return image


def test_geographic_geotiffs_give_their_geometry_and_ellipsoid():
    # As the Blue Marble: geographic, degrees, pixel is area, an ellipsoid by semi-major axis and inverse flattening.
    lon = -180.0 + (numpy.arange(720) + 0.5) * 0.5
    lat = 90.0 - (numpy.arange(360) + 0.5) * 0.5
    keys = [1, 1, 0, 5, 1024, 0, 1, 2, 1025, 0, 1, 1, 2054, 0, 1, 9102, 2057, 34736, 1, 1, 2059, 34736, 1, 0]
    image = geotiff(surface(lon[None, :], lat[:, None]), keys, [4.0, 6378137.0], (0.0, 0.0, 0.0, -180.0, 90.0, 0.0), 0.5)
    geometry = geotiff_geometry(image, "test")
    assert (geometry.first_column_longitude_deg, geometry.first_row_latitude_deg, geometry.pixel_deg) == (-180.0, 90.0, 0.5)
    assert geometry.registration == "area" and geometry.planetographic
    assert geometry.radii_km == (6378.137, 6378.137 * 0.75)
    # A sphere (semi-minor equal to semi-major) has planetocentric latitudes.
    sphere = [1, 1, 0, 5, 1024, 0, 1, 2, 1025, 0, 1, 1, 2054, 0, 1, 9102, 2057, 34736, 1, 0, 2058, 34736, 1, 1]
    image = geotiff(surface(lon[None, :], lat[:, None]), sphere, [198200.0, 198200.0], (0.0, 0.0, 0.0, -180.0, 90.0, 0.0), 0.5)
    geometry = geotiff_geometry(image, "test")
    assert not geometry.planetographic
    out = reproject(pillow_raster(image), geometry, OUT_W, OUT_H, "test")[:, :, 0]
    assert numpy.abs(out - expected_output()).max() < 1.0
    projected = [1, 1, 0, 2, 1024, 0, 1, 1, 2054, 0, 1, 9102]
    with pytest.raises(PipelineError, match="geographic"):
        geotiff_geometry(geotiff(numpy.zeros((360, 720)), projected, [0.0], (0.0, 0.0, 0.0, 0.0, 0.0, 0.0), 0.5), "test")


def test_pds3_layout_maps_the_tiff_bytes_and_must_match_the_header(tmp_path):
    values = surface(numpy.linspace(-180, 180, 720)[None, :], numpy.linspace(90, -90, 360)[:, None])
    path = tmp_path / "map.tif"
    as_image(values).save(path, format="TIFF")
    with Image.open(path) as tiff:
        offset = tiff.tag_v2[273][0]
        assert len(tiff.tag_v2[273]) == 1  # one strip, so the bytes are contiguous

    def label(start: int, name: str = "map.tif") -> str:
        return (
            f'PDS_VERSION_ID = PDS3\n^IMAGE = ("{name}", {start} <BYTES>)\nOBJECT = IMAGE\n  LINES = 360\n'
            "  LINE_SAMPLES = 720\n  SAMPLE_BITS = 8\n  SAMPLE_TYPE = LSB_UNSIGNED_INTEGER\n  BANDS = 1\n"
            "  BAND_STORAGE_TYPE = BAND_SEQUENTIAL\nEND_OBJECT = IMAGE\nEND\n"
        )

    raster = pds3_raster(path, label(offset + 1), "test")  # PDS3 byte pointers count from 1
    with Image.open(path) as tiff:
        assert numpy.array_equal(raster.rows(10, 20), pillow_raster(tiff.convert("L")).rows(10, 20))
    with pytest.raises(PipelineError, match="does not match"):
        pds3_raster(path, label(offset), "test")
    with pytest.raises(PipelineError, match="does not point"):
        pds3_raster(path, label(offset + 1, "other.tif"), "test")


def test_jpeg_output_is_reproducible_and_grayscale_becomes_rgb():
    image = expected_output()[:, :, None]
    rgb = to_rgb8(image)
    assert rgb.shape == (OUT_H, OUT_W, 3) and numpy.array_equal(rgb[:, :, 0], rgb[:, :, 2])
    first, second = encode_jpeg(rgb, 90), encode_jpeg(rgb, 90)
    assert first == second
    decoded = numpy.asarray(Image.open(io.BytesIO(first)), dtype=numpy.float64)
    assert numpy.abs(decoded[:, :, 0] - expected_output()).max() < 6.0


def test_the_manifest_names_registered_files_and_selected_bodies():
    manifest = load_manifest(REAL_REPO.catalog / "textures.json")
    sources = {s.id: s for s in load_sources(REAL_REPO.sources_file)}
    selected = {b["naif_id"] for b in json.loads((REAL_REPO.catalog / "bodies-moons.json").read_text())["bodies"]}
    assert (manifest.width, manifest.height) == (2048, 1024)
    for entry in manifest.entries:
        assert entry.body in selected
        files = {f.name: f for f in sources[entry.source_id].files}
        named = [entry.image_file, entry.layout_file, entry.geometry.get("label")]
        for name in filter(None, named):
            assert name in files and files[name].optional, name
        if entry.layout_file is not None:
            assert files[entry.image_file].publisher_checksums is not None  # USGS publishes an MD5 per map


def test_the_manifest_rejects_malformed_entries(tmp_path):
    good = json.loads((REAL_REPO.catalog / "textures.json").read_text())
    for change in (
        lambda d: d["textures"].append(dict(d["textures"][0])),
        lambda d: d["textures"][0].pop("credit"),
        lambda d: d["textures"][0]["geometry"].update(kind="guess"),
        lambda d: d["output"].update(width=1000),
    ):
        data = json.loads(json.dumps(good))
        change(data)
        path = tmp_path / "textures.json"
        path.write_text(json.dumps(data))
        with pytest.raises(PipelineError):
            load_manifest(path)


def test_manual_geometry_is_planetocentric_and_complete():
    spec = {"kind": "manual", "first_column_longitude_deg": -180.0, "first_row_latitude_deg": 90.0,
            "registration": "area", "latitude": "planetocentric", "locator": "x"}  # fmt: skip
    assert manual_geometry(spec, 4096, 2048, "test").pixel_deg == 360.0 / 4096
    for bad in ({**spec, "latitude": "planetographic"}, {k: v for k, v in spec.items() if k != "locator"}):
        with pytest.raises(PipelineError):
            manual_geometry(bad, 4096, 2048, "test")


def committed_outputs() -> list[Path]:
    manifest = load_manifest(REAL_REPO.catalog / "textures.json")
    directory = REAL_REPO.root / manifest.directory
    return [directory / e.output_name for e in manifest.entries] + [directory / "BodyTextures.json"]


def _texture_sources_present() -> bool:
    try:
        from usim_pipeline.fetch import locked_file

        sources = load_sources(REAL_REPO.sources_file)
        for entry in load_manifest(REAL_REPO.catalog / "textures.json").entries:
            for name in filter(None, [entry.image_file, entry.layout_file, entry.geometry.get("label")]):
                locked_file(REAL_REPO, sources, entry.source_id, name)
    except PipelineError:
        return False
    return True


@pytest.mark.skipif(not _texture_sources_present(), reason="texture sources not fetched (usim-pipeline fetch --optional)")
def test_committed_textures_are_current(tmp_path):
    from usim_pipeline.repo import Repo
    from usim_pipeline.textures import ATTRIBUTION_PATH, build_textures

    # Build into a copy of the repository's layout, reading the real sources.
    class Mirror(Repo):
        @property
        def raw(self) -> Path:
            return REAL_REPO.raw

    mirror = Mirror(tmp_path)
    (tmp_path / "data").symlink_to(REAL_REPO.data)
    built = build_textures(mirror, log=lambda _: None)
    for path in built:
        committed = REAL_REPO.root / path.relative_to(tmp_path)
        assert committed.read_bytes() == path.read_bytes(), committed
    assert (REAL_REPO.root / ATTRIBUTION_PATH).exists()
