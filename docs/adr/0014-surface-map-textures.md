# ADR-0014: Surface map textures from mission mosaics, reprojected by the pipeline

| | |
|---|---|
| Status | Accepted; the plain spheres of decision 1 superseded in part by [ADR-0015](0015-stylized-presentation-over-real-data.md) (realistic style only) |
| Date | 2026-10-26 |

## Context

P6-07 (and P5-12, deferred to it) adds surface maps to the viewer's lit spheres. The project owner chose on 2026-10-23: maps for the planets and moons that have a mission mosaic, 2048 x 1024 pixels per body, committed through Git LFS.

Global maps are published in different conventions: longitude increasing east or west, the left edge at 0, 180 or another longitude (USGS labels name a projection center that is not always the image center), planetocentric or planetographic latitude, pixels registered by area or by point, and some a fraction of a pixel wider than 360 degrees. The raw files range from 17 MB to 1.9 GB. The viewer draws ellipsoids by scaling a unit sphere along the body's axes, as the core maps them to the host.

## Decision

1. **Sources** (registered in DATA_SOURCES.md, section 9, and `data/sources.json` as optional files, pinned like every input):

   | Source | Bodies |
   |---|---|
   | S-USGS-ASTRO, USGS Astrogeology global mosaics (GeoTIFF with ISIS and PDS3 labels and a published MD5) | Mercury, Mars, Phobos, Io, Europa, Ganymede, Callisto, Mimas (DLR basemap), Enceladus, Tethys, Dione, Rhea, Iapetus, Triton, Charon, Pluto |
   | S-NASA-SVS-MOON, the SVS CGI Moon Kit (LROC WAC color, 2025 map) | Moon |
   | S-NASA-EO-BMNG, Blue Marble: Next Generation, August 2004 | Earth |
   | S-NASA-PHOTOJOURNAL, PIA07782, Cassini's map of Jupiter (December 2000) | Jupiter |

   Pluto is included with Charon (the same New Horizons product family). No map: Venus and Titan (visible light shows only cloud or haze tops; the radar and near-infrared surface maps would mislead), Saturn (no official global map was found, only a fan reprojection), Uranus, Neptune, Deimos and the Uranian moons (no global mosaics). They stay plain lit spheres, and the info panel says so.
2. **One layout for every map**, produced by `usim-pipeline textures` from `data/catalog/textures.json`: equirectangular, east longitude, the left edge at 180 degrees, planetocentric latitude, 2048 x 1024, JPEG of quality 90 (4:4:4), grayscale stored as RGB.
3. **The map's own geometry, read from its label**: the ISIS Mapping group for USGS mosaics (the projection formulas of ISIS's SimpleCylindrical and Equirectangular classes and its pixel mapper, read from the ISIS source on 2026-10-23), GeoTIFF tags for geographic GeoTIFFs (Earth, Mimas), and for the two sources without a machine-readable label (Moon, Jupiter) values transcribed from the publisher's page with a locator. Planetographic latitudes are converted with the map's own ellipsoid.
4. **Resampling is an exact box filter** in the source's pixel coordinates (cumulative sums, wrapping in longitude, a fractional period allowed), so no aliasing and no sub-pixel shift. A map may be up to a thousandth of a pixel short of 360 degrees, because labels round the pixel size: Ganymede's PixelResolution is 1.1e-8 below the 32 pixels per degree its Scale states, which leaves it 1.3e-4 of a pixel short. The gap takes the last column's value. The 1.9 GB Mercury mosaic is memory-mapped through its PDS3 label (byte pointers count from 1) and processed in row chunks; the TIFF header must agree with the label (size, no compression, contiguous strips).
5. **Pillow 12.3.0** joins the pipeline's dependencies (pinned by hash) to decode TIFF and JPEG and to encode the JPEGs. It is a pipeline (build-time) dependency only; the core is unchanged.
6. **Unity**: a body with a map gets its own mesh (`SphereMesh.CreateMapped`), whose vertices are body-fixed directions in host axes from the core (`usim_axes_to_host`) and whose UVs are the planetocentric longitude and latitude of each vertex once scaled by the body's radii. `BodyLit` samples the map in place of its neutral color. The import settings (sRGB, mipmaps, repeat across the seam, clamp at the poles) are enforced by an asset postprocessor. The scene builder wires the maps from the generated manifest `BodyTextures.json`, and the info panel shows each map's content and credit. `ATTRIBUTION.md` is generated with the maps.
7. **Large downloads run from a script** (`tools/scripts/fetch-texture-sources.sh`), at the project owner's request: the mosaics total about 4.6 GB.

## Consequences

- Checked on 2026-10-26, all 19 maps:
  - The prime meridian and equator of the Earth and the Moon line up, and so does Jupiter's Great Red Spot against the gridded PIA07782.
  - Each map was compared with IAU Gazetteer features (S-IAU-WGPSN center points, planetocentric, east longitude). Features with a distinct form lie under their coordinates, at least one per map, for example Caloris, Olympus Mons, Hellas, Stickney, Pele, Loki Patera, Pwyll, Osiris, Galileo Regio, Valhalla, Herschel, Ali Baba, Odysseus, Dido, Inktomi, Engelier, Bubembe Regio, Dorothy and Sputnik Planitia.
  - In the viewer, the Earth seen from the Sun at 2026-10-21 12:00 UTC shows the Gulf of Guinea at the center of the lit disk, and the Moon seen from the Earth shows its near side.
- The 19 maps take 10.7 MB in Git LFS, 0.31 to 0.92 MB each.
- Some maps are not natural color, and the info panel says what each shows: Io's is false color (the product says the eye would see more muted colors), Mars and Triton are synthesized from other filters, and most icy moons are monochrome. Areas a mission did not image (for example Charon's south) are shown as the mosaic has them.
- Jupiter's clouds are those of December 2000. The Earth is cloud-free, in August.
- Rebuilding the maps needs the sources (`fetch-texture-sources.sh`). The pipeline test that compares the committed maps with a rebuild is skipped without them.

## Alternatives considered

- **Use each map as published, with a UV offset per texture**: every convention (direction, edges, registration, latitude type) would reach the Unity side, and maps with a fractional width could not be expressed.
- **Higher resolution (4096 x 2048)**: about 4 times the repository size; declined by the project owner.
- **Synthetic or artistic maps for the bodies without mosaics** (for example Saturn or Uranus): not from a verified source.
- **Decode with tifffile or GDAL**: tifffile needs extra codecs for compressed TIFFs, and GDAL is a large native dependency; Pillow handles every source here.
