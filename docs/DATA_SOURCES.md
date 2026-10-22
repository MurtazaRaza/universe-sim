# Data Sources Registry

| | |
|---|---|
| Status | Active |
| Last updated | 2026-10-22 |
| Related | [SPEC.md section 10](SPEC.md#10-data-provenance), [ADR-0005](adr/0005-data-provenance-and-storage.md) |

This is the human-readable registry of the sources the simulation may use. The machine-readable counterparts are `data/sources.json` (declared sources and files) and `data/sources.lock.json` (exact files, sizes, checksums and retrieval dates), maintained by `tools/pipeline/usim-pipeline fetch`. Keep the three consistent: a source is added here and to `sources.json` in the same change.

**Rule: a source must be listed here, with its tier, before any of its data is used.**

---

## 1. Source tiers

| Tier | Definition | Allowed use |
|---|---|---|
| **Tier 1: primary** | Published by the institution that produced the data (JPL, NAIF, IAU, IERS, ESA, MPC, IPAC) or in a peer-reviewed publication. | Simulation inputs. |
| **Tier 2: secondary** | Reputable compilations of Tier 1 data (for example the NASA NSSDC fact sheets). | Cross-checks and descriptive text only. A Tier 2 value may be used as an input only when no Tier 1 source exists, and it must then be labeled as such in the catalog and the UI. |
| **Not allowed** | Wikis, blogs, forums, uncited compilations, AI-generated content, values from memory. | Never. |

## 2. Verification and pinning

- Every downloaded file is pinned in `data/sources.lock.json` by URL, size, SHA-256 and retrieval timestamp.
- Where the publisher provides checksums, the pipeline verifies them too. NAIF provides MD5 lists in `aa_checksums.txt` in each kernel directory.
- API responses used as reference data (Horizons, SBDB) are saved with the full request parameters, the API version string and the retrieval timestamp.
- Each source records its terms of use. Check the terms before redistributing any data (for example inside a release build).

## 3. Solar System: ephemerides

The URLs in this section and the next were checked as reachable on 2026-10-20.

### S-JPL-DE440: JPL planetary and lunar ephemeris DE440 (Tier 1, baseline)

| | |
|---|---|
| Provides | Positions of the Sun, the Moon, the planets and the planet system barycenters (SPK type 2). |
| Files | `de440s.bsp` (31 MB, 1849-12-26 to 2150-01-22, the v1 default) and `de440.bsp` (114 MB, 1549-12-31 to 2650-01-25) |
| URL | https://naif.jpl.nasa.gov/pub/naif/generic_kernels/spk/planets/ |
| Publisher MD5 | `de440s.bsp` 3917ee56769db332790c751e2168843d, `de440.bsp` c9d581bfd84209dbeee8b1583939b148 (from `aa_checksums.txt`, 2026-10-20) |
| Docs | `de440_tech-comments.txt` in the same directory; https://ssd.jpl.nasa.gov/planets/eph_export.html |
| Citation | Park, R. S., Folkner, W. M., Williams, J. G., Boggs, D. H. (2021). The JPL Planetary and Lunar Ephemerides DE440 and DE441. *The Astronomical Journal*, 161:105. https://doi.org/10.3847/1538-3881/abd414 |
| Pinned | `de440s.bsp`: 32,726,016 bytes, SHA-256 c1c7feeab882263fc493a9d5a5b2ddd71b54826cdf65d8d17a76126b260a49f2, publisher MD5 verified, retrieved 2026-10-20 |
| Terms | NAIF rules (https://naif.jpl.nasa.gov/naif/rules.html, read 2026-10-20): kernels on the NAIF server may be downloaded and used by anyone; unmodified kernels may be redistributed; acknowledgement is encouraged. The same terms apply to every NAIF kernel below. The data pack holds re-encoded extracts with their attribution. |
| Contents | 14 segments of SPK type 2, all 1849-12-26 to 2150-01-22 TDB: barycenters 1 to 9 and the Sun relative to the SSB, Earth and Moon relative to the Earth-Moon barycenter, Mercury and Venus relative to their barycenters. No segments for the centers of Mars to Pluto; v1 places those planets at their system barycenters, flagged approximate ([ADR-0008](adr/0008-planet-positions-at-system-barycenters.md)). |
| Used by | The default data pack (ephemeris); VAL-01 reference states. |
| Notes | The v1 baseline; see [ADR-0004](adr/0004-ephemeris-baseline-de440.md). |

### S-JPL-DE441: long-span companion of DE440 (Tier 1)

| | |
|---|---|
| Provides | The same bodies over -13200 to +17191 (`de441_part-1.bsp` and `de441_part-2.bsp`, about 1.5 GB each). |
| URL | https://naif.jpl.nasa.gov/pub/naif/generic_kernels/spk/planets/ |
| Citation | Park et al. 2021 (as above). |
| Notes | JPL Horizons uses DE441 for planetary positions (observed in the Horizons API output header on 2026-10-20). A candidate for a future deep-time pack. |

### S-JPL-DE442: newer planetary ephemeris (Tier 1, candidate)

| | |
|---|---|
| Provides | The same bodies and span as DE440 (`de442.bsp` 114 MB, `de442s.bsp` 31 MB). |
| URL | https://naif.jpl.nasa.gov/pub/naif/generic_kernels/spk/planets/ (published there 2025-02-06) |
| Publisher MD5 | `de442s.bsp` cc49327e06088124c0e39d8dde9f0b58, `de442.bsp` 446656322267e7b819a26cb08a0d8718 |
| Docs | `de442_tech-comments.txt`: integrated 2024-05-13. Based on DE440, with Uranus occultation data, additional Mars orbiter and Juno ranging data, and an updated Uranus barycenter. |
| Notes | Candidate to replace DE440 later (OQ-01). |

### S-NAIF-SAT: JPL planetary satellite ephemerides (Tier 1, Phase 6)

| | |
|---|---|
| Provides | Moons of Mars, Jupiter, Saturn, Uranus, Neptune and Pluto. |
| URL | https://naif.jpl.nasa.gov/pub/naif/generic_kernels/spk/satellites/ |
| Docs | `AAREADME_Satellite_SPKs`, `aa_summaries.txt` (bodies and coverage per file), `aa_checksums.txt` |
| Files | `mar099.bsp` (Mars; MAR099 with DE440), `jup365.bsp` (Jupiter; JUP365, DE440), `sat441.bsp` (Saturn; SAT441, DE440), `ura184_part-3.bsp` (Uranus; URA184, DE442), `nep097.bsp` (Neptune; NEP097, DE441 fit), `plu060.bsp` (Pluto; PLU060, DE440): one file per system holding the planet center and the major moons relative to the system barycenter, all covering at least 1800 to 2199. Every segment is SPK type 2 in J2000. Declared **optional** in `data/sources.json`: `fetch --optional` downloads them (3.4 GB); `fetch` alone only checks local copies. |
| Citation | The solution names above, as merged by NAIF (each kernel's comments give the solution, the planetary ephemeris and the fit). URA184: Jacobson, R. A., Park, R. S. (2025). The Orbits of Uranus, Its Satellites and Rings, the Gravity Field of the Uranian System, and the Orientation of the Poles of Uranus and Its Satellites. *The Astronomical Journal*, 169(2), 65. |
| Pinned | `mar099.bsp` 1,227,574,272 bytes, SHA-256 9991e57b196bae1a096acc6e2afc6718102ee420d684695de0f0333064c046bc; `jup365.bsp` 1,136,581,632 bytes, SHA-256 dbf016c01ba4d022154838000cf3f06962cf958ddc503a366f7fe8f81495c5cb; `sat441.bsp` 661,592,064 bytes, SHA-256 d7e444a9ba7a52b8f448ff0747030789524d2f0c1212e4e2bd7f5fc3c96444d5; `ura184_part-3.bsp` 386,885,632 bytes, SHA-256 273cd4ccc470d1098562cd4e94fa48fc2b4c6b1c2896308e473c949b87937c53; `nep097.bsp` 105,262,080 bytes, SHA-256 5c1132fdc48c54e5d2e4eb663e1ed1a876911eae9433c203f29a5a1b5e1fa9b1; `plu060.bsp` 135,207,936 bytes, SHA-256 dfbb102491a26ed41ae08ca3f8963f22f0219df1d8f265ab87b9ad825a826fc6; publisher MD5 verified for all six, retrieved 2026-10-21 (downloaded with `tools/scripts/download-phase6-kernels.sh`, pinned by `fetch` from the local copies). |
| Used by | The optional moons pack `solar-system-moons` (P6-01, [ADR-0010](adr/0010-moons-in-an-optional-pack.md)): only the segments relative to each system barycenter, 2000 to 2100. VAL-01 moons-pack reference states. |
| Notes | File names encode the solution. Version numbers do not increase with date, so read `aa_summaries.txt`. The Uranus alternative `ura111xl-701` to `705` with `-799` (URA111, DE441, about 6.7 GB) was not used: URA184 is the newer solution and differs from URA111 by up to 1299 km (Titania) over 2000 to 2100 (ADR-0010). Center offsets and trimmed sizes over the de440s window: [ADR-0008](adr/0008-planet-positions-at-system-barycenters.md). |

### S-JPL-HORIZONS: JPL Horizons System (Tier 1, reference data)

| | |
|---|---|
| Provides | State vectors, observer tables and orbital elements for nearly all Solar System objects and many spacecraft. It can also generate SPK files for small bodies. |
| URL | https://ssd.jpl.nasa.gov/horizons/ ; API: https://ssd.jpl.nasa.gov/api/horizons.api ; API docs: https://ssd-api.jpl.nasa.gov/doc/horizons.html |
| Citation | Giorgini, J. D., Yeomans, D. K., Chamberlin, A. B., Chodas, P. W., Jacobson, R. A., Keesey, M. S., Lieske, J. H., Ostro, S. J., Standish, E. M., Wimberly, R. N. (1996). JPL's On-Line Solar System Data Service. *Bulletin of the American Astronomical Society*, 28(3), 1158 (as listed in the Horizons manual, read 2026-10-21). |
| Used by | `data/reference/horizons/` (VAL-02 captures, 2026-10-21): each file records the full request URL, the API signature and the retrieval time. Horizons reports DE441 as the source of planetary states. |
| Notes | API version 1.2 as of 2026-10-20. Used to generate reference vectors (VAL-02, VAL-05) and small-body SPKs (P7-02). Record the full query with every saved response. Reference data only, never a pack input. |

## 4. Solar System: physical constants, orientation and time

### S-NAIF-PCK11: NAIF generic planetary constants kernel (Tier 1)

| | |
|---|---|
| Provides | Radii (triaxial), pole orientation and prime meridian rotation models for the Sun, the planets, the satellites and selected small bodies. |
| File | `pck00011.tpc` (text kernel, 128 KB) |
| URL | https://naif.jpl.nasa.gov/pub/naif/generic_kernels/pck/ |
| Underlying source | Archinal, B. A., et al. (2018). Report of the IAU Working Group on Cartographic Coordinates and Rotational Elements: 2015. *Celestial Mechanics and Dynamical Astronomy*, 130:22. https://doi.org/10.1007/s10569-017-9805-5 (cited in the kernel header) |
| Checksums | No publisher checksum list (the `pck` directory has no `aa_checksums.txt`; checked 2026-10-20). Pinned: 131,226 bytes, SHA-256 3dff7b1dbeceaa01f25467767d3fa25816051c85d162d1edf04acb310ee28bb1, retrieved 2026-10-20. |
| Used by | The default data pack: triaxial radii (and the derived mean radius) and IAU rotation elements of the v1 bodies; VAL-04 reference orientations. |
| Notes | The kernel's comments record body-specific caveats (for example bodies for which the IAU report no longer gives rotational elements). Read them when building the catalog. Its Mars system phase angles are quadratic (`BODY4_MAX_PHASE_DEGREE = 2`), which CSPICE supports from N0067; `pck00011_n0066.tpc` is the variant for older toolkits. The NAIF `aareadme.txt` erratum: the constant 12.711923222 in angle M5 (line 1294) should be 12.71192322; NAIF states the effect on Phobos is at round-off level, and the pack keeps the file's value. CSPICE reads 9 of its 2896 numeric values one ulp away from their text ([ADR-0007](adr/0007-text-kernel-values.md)). |

### S-NAIF-GM440: gravitational parameters (Tier 1)

| | |
|---|---|
| Provides | GM in km^3/s^2 for the Sun, the planets, the system barycenters, the Moon, satellites and selected small bodies. |
| File | `gm_de440.tpc` |
| URL | https://naif.jpl.nasa.gov/pub/naif/generic_kernels/pck/ |
| Underlying sources | Per the file header: DE440 values for the Sun, the planets, the barycenters and the Moon (Park et al. 2021). JPL satellite solution release forms for the other satellites. SB441-N16 for the small-body perturbers. |
| Checksums | No publisher checksum list (the `pck` directory has no `aa_checksums.txt`; checked 2026-10-20). Pinned: 12,406 bytes, SHA-256 924ddf4fb9ead9fe8a1aa55780bcabde40b09d00065d58226e24b68d8092f140, retrieved 2026-10-20. |
| Used by | The default data pack: GM of the v1 bodies and barycenters (none for the SSB, which the file does not give). |
| Notes | GM values differ slightly between sources. For example, Earth's GM is 398600.43550702266 km^3/s^2 in `gm_de440.tpc` but 398600.435436 in the Horizons object data header (both checked 2026-10-20). Always record which file supplied a value. Per the file header, the values for 1 to 10, 199, 299, 301 and 399 are DE440's; those for the other planets (499 to 999) come from the JPL satellite solutions (for example SAT441L, URA116, NEP100/101, PLU058). CSPICE reads 36 of its 227 numeric values up to 5 ulps away from their text ([ADR-0007](adr/0007-text-kernel-values.md)). |

### S-NAIF-LSK: leap seconds kernel (Tier 1)

| | |
|---|---|
| Provides | The TAI - UTC leap second table and the constants SPICE uses for its TDB-TT model. |
| File | `naif0012.tls` (latest in the NAIF directory as of 2026-10-20) |
| URL | https://naif.jpl.nasa.gov/pub/naif/generic_kernels/lsk/ |
| Checksums | No publisher checksum list (the `lsk` directory has no `aa_checksums.txt`; checked 2026-10-20). `naif0012.tls`: 5257 bytes, SHA-256 678e32bdb5a744117a467cd9601cd6b373f0e9bc9bbde1371d5eee39600a039b (retrieved 2026-10-20). |
| Cross-check | IERS Bulletin C (leap second announcements): https://hpiers.obspm.fr/iers/bul/bulc/ |
| Used by | The default data pack (leap seconds and the TDB - TT model); the unit test fixtures (`usim-pipeline reference unit-fixtures`); VAL-03 reference conversions. |
| Notes | Check Bulletin C when updating. If a new leap second is announced, NAIF releases a new LSK. The kernel's last entry is TAI - UTC = 37 s from 2017-01-01. |

### S-NAIF-IDS: NAIF integer ID codes and body names (Tier 1)

| | |
|---|---|
| Provides | The NAIF integer ID of each body and its standard name (for example 3 = EARTH BARYCENTER, 999 = PLUTO). |
| URL | https://naif.jpl.nasa.gov/pub/naif/toolkit_docs/C/req/naif_ids.html |
| Form | Built into the SPICE Toolkit; the pipeline reads it with `bodc2n` from CSPICE N0067 (SpiceyPy 8.2.0). No file to pin; the toolkit version is recorded in each pack's metadata. |
| Used by | Body names in the data pack (title-cased for display, for example "Earth Barycenter"). |

### S-NAIF-MOONPA: high-precision lunar orientation (Tier 1, Phase 6 optional)

| | |
|---|---|
| File | `moon_pa_de440_200625.bpc` (binary PCK, 12 MB). Downloaded into `data/raw/naif/` on 2026-10-21 (size checked against the server); not yet registered in `sources.json` (P6-05). |
| URL | https://naif.jpl.nasa.gov/pub/naif/generic_kernels/pck/ |

### S-NAIF-EARTHPCK: high-precision Earth orientation (Tier 1, Phase 6 optional)

| | |
|---|---|
| Files | `earth_latest_high_prec.bpc` (updated frequently) and long-span combined or predict files |
| URL | https://naif.jpl.nasa.gov/pub/naif/generic_kernels/pck/ |

### S-JPL-SSD-PARAMS: JPL Solar System Dynamics parameter pages (Tier 1)

| Page | URL |
|---|---|
| Planetary physical parameters | https://ssd.jpl.nasa.gov/planets/phys_par.html |
| Planetary satellite physical parameters | https://ssd.jpl.nasa.gov/sats/phys_par/ |
| Planetary satellite mean elements | https://ssd.jpl.nasa.gov/sats/elem/ |
| Astrodynamic parameters | https://ssd.jpl.nasa.gov/astro_par.html |
| Approximate positions of the planets (Keplerian elements, fallback only) | https://ssd.jpl.nasa.gov/planets/approx_pos.html |

Notes: these are HTML tables. Values from them enter only through the manual entry mechanism (P2-06), with the page URL, access date and table name recorded.

### S-IAU-CONSTANTS: IAU resolutions for definitional constants (Tier 1)

| Item | Reference |
|---|---|
| Astronomical unit = 149,597,870,700 m exactly | IAU 2012 Resolution B2 |
| Nominal solar and planetary conversion constants | IAU 2015 Resolution B3; Prša, A., et al. (2016). *The Astronomical Journal*, 152:41. https://doi.org/10.3847/0004-6256/152/2/41 |

### S-IAU-SOFA: IAU Standards of Fundamental Astronomy (Tier 1, reference algorithms)

| | |
|---|---|
| Provides | Reference implementations for time scales, Earth rotation, precession and nutation. |
| URL | https://www.iausofa.org/ |
| Notes | Used as an algorithm reference and validation oracle. SOFA has its own license terms; read them before porting code. |

### S-IAU-WGPSN: Gazetteer of Planetary Nomenclature, rings and ring gaps (Tier 1)

| | |
|---|---|
| Provides | Names and distances from the planet's center (km) of the rings, divisions and gaps of Jupiter, Saturn, Uranus and Neptune. |
| URL | https://planetarynames.wr.usgs.gov/Page/Rings (IAU Working Group for Planetary System Nomenclature, hosted by the USGS Astrogeology Science Center) |
| Form | An HTML table; values enter through `data/catalog/rings.json` (transcribed 2026-10-22, each value as the text on the page, parsed by the pipeline). |
| Used by | Ring radii in the default and moons packs (P6-04, [ADR-0011](adr/0011-sky-background-and-rings.md)): Saturn's D, C, B and A rings, the Cassini Division and 13 gaps. |
| Notes | Distances are rounded (to 10 km for the main boundaries). A gap given as a center and a radial width becomes center plus and minus half the width. |

### S-PDS-RMS: PDS Ring-Moon Systems Node, Saturn's rings (Tier 2)

| | |
|---|---|
| Provides | Inner and outer boundaries, normal optical depth ranges and types of Saturn's rings, with a reference per row. |
| URL | https://pds-rings.seti.org/saturn/saturn_tables.html (table "Rings of Saturn", read 2026-10-22) |
| Underlying sources | The table's footnotes, for example Colwell, J. E., et al. (2009), in *Saturn from Cassini-Huygens*, Springer, 375-408; French, R. G., et al. (2017), *Icarus* 290, 14-45; Murray, C. D., Dermott, S. F. (1999), *Solar System Dynamics*. |
| Used by | Ring optical depths in the packs, used only to draw ring opacity, labeled Tier 2 in the pack and the viewer. No Tier 1 source of these values is available to the pipeline. |
| Form | An HTML table; values enter through `data/catalog/rings.json`. |

## 5. Solar System: small bodies

### S-JPL-SBDB: JPL Small-Body Database (Tier 1, Phase 7)

| | |
|---|---|
| Provides | Osculating elements with epoch and uncertainties, physical parameters and discovery data for asteroids and comets. |
| API | https://ssd-api.jpl.nasa.gov/doc/sbdb.html (single object), https://ssd-api.jpl.nasa.gov/doc/sbdb_query.html (bulk queries) |

### S-MPC: Minor Planet Center (Tier 1, alternative bulk source)

| | |
|---|---|
| Provides | MPCORB orbit file for all numbered and unnumbered minor planets. |
| URL | https://www.minorplanetcenter.net/iau/MPCORB.html |

## 6. Cross-check only (Tier 2)

### S-NSSDC-FACT: NASA planetary fact sheets

| | |
|---|---|
| URL | https://nssdc.gsfc.nasa.gov/planetary/factsheet/ |
| Use | Sanity checks and descriptive text. Not a simulation input. |

## 7. Sky background

### S-CDS-BSC5: The Bright Star Catalogue, 5th revised edition (Tier 1)

| | |
|---|---|
| Provides | 9110 entries (9096 stars, to about V 6.5): J2000 positions (FK5), proper motions, V magnitudes, B-V and other colors, spectral types, designations. |
| URL | https://cdsarc.cds.unistra.fr/ftp/V/50/ (CDS catalog V/50) |
| Citation | Hoffleit, D., Warren Jr., W. H. (1991). *The Bright Star Catalogue*, 5th Revised Ed. (Preliminary Version). Astronomical Data Center, NSSDC/ADC. Retrieved through VizieR, CDS, Strasbourg (DOI 10.26093/cds/vizier). |
| Checksums | No publisher checksum list. Pinned: `ReadMe` 11,571 bytes, SHA-256 44fd9c73e2eecad0beb47bdfa3f01c60fd43f93d6964198e31fcd48732de5b33; `catalog.gz` 573,921 bytes, SHA-256 3dc44b1e90be8fbe5bcc7656032560f51275f985c7e3f783c9028e1838ec7bed; retrieved 2026-10-21. |
| Terms | CDS VizieR rules of usage (https://cds.unistra.fr/vizier-org/licences_vizier.html, read 2026-10-22): free of use in a scientific context with the authors and publication cited; an acknowledgement of VizieR is appreciated; commercial use depends on the data origin. The ReadMe states no copyright. |
| Used by | The star catalog of the default and moons packs (section STAR, [ADR-0011](adr/0011-sky-background-and-rings.md)). |
| Notes | The pipeline reads the columns at the byte ranges of the ReadMe's "Byte-by-byte Description of file: catalog" and checks that the pinned ReadMe still describes them there. FK5 equinox J2000 coordinates are used as ICRF directions without a frame tie correction. pmRA is the projected motion, pmRA cos(Dec), per the ReadMe. |

## 8. Beyond the Solar System (Phase 9, not yet pinned)

| ID | Source | URL | Notes |
|---|---|---|---|
| S-ESA-GAIA | ESA Gaia Archive (DR3; DR4 when released) | https://gea.esac.esa.int/archive/ | Astrometry and photometry. The Gaia Catalogue of Nearby Stars (Gaia Collaboration, Smart et al. 2021) is a candidate starting set. Follow ESA's credit and citation requirements. |
| S-HIP2 | Hipparcos new reduction (van Leeuwen 2007) | via CDS VizieR (catalog I/311) | Bright stars that are saturated in Gaia. |
| S-CDS-SIMBAD | SIMBAD astronomical database | https://simbad.cds.unistra.fr/simbad/ | Cross-identifications and names. |
| S-NASA-EXO | NASA Exoplanet Archive | https://exoplanetarchive.ipac.caltech.edu/ | Planetary Systems tables with uncertainties. |

## 9. Visual assets (rendering only)

Textures and models are not simulation data, but their provenance and licenses are still tracked, in `UniverseSim/Assets/UniverseSim/ATTRIBUTION.md`.

| ID | Source | URL | Notes |
|---|---|---|---|
| S-NASA-SVS-MOON | NASA SVS CGI Moon Kit (LRO-based color and elevation maps) | https://svs.gsfc.nasa.gov/4720 | |
| S-USGS-ASTRO | USGS Astrogeology Science Center planetary maps | https://astrogeology.usgs.gov/ | Returned HTTP 403 to scripted requests on 2026-10-20. Retrieve manually and record the page URL in the attribution file. |

Other mission-derived maps (for example from NASA/JPL/Caltech mission archives) may be added the same way. Record the mission, the product and the license for each.
