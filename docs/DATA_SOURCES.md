# Data Sources Registry

| | |
|---|---|
| Status | Active |
| Last updated | 2026-10-20 |
| Related | [SPEC.md section 10](SPEC.md#10-data-provenance), [ADR-0005](adr/0005-data-provenance-and-storage.md) |

This is the human-readable registry of the sources the simulation may use. The machine-readable counterparts are `data/sources.json` (declared sources) and `data/sources.lock.json` (exact files, sizes, checksums and retrieval dates), both created in Phase 2.

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
| Notes | File names encode the solution (for example `jup365.bsp`, `sat441.bsp`, `plu060.bsp`, `mar099s.bsp`). Many files exceed 100 MB. Select per body by coverage in Phase 6 and trim the segments into the pack. Version numbers do not increase with date, so read the summaries. |

### S-JPL-HORIZONS: JPL Horizons System (Tier 1, reference data)

| | |
|---|---|
| Provides | State vectors, observer tables and orbital elements for nearly all Solar System objects and many spacecraft. It can also generate SPK files for small bodies. |
| URL | https://ssd.jpl.nasa.gov/horizons/ ; API: https://ssd.jpl.nasa.gov/api/horizons.api ; API docs: https://ssd-api.jpl.nasa.gov/doc/horizons.html |
| Notes | API version 1.2 as of 2026-10-20. Used to generate reference vectors (VAL-02, VAL-05) and small-body SPKs (P7-02). Record the full query with every saved response. |

## 4. Solar System: physical constants, orientation and time

### S-NAIF-PCK11: NAIF generic planetary constants kernel (Tier 1)

| | |
|---|---|
| Provides | Radii (triaxial), pole orientation and prime meridian rotation models for the Sun, the planets, the satellites and selected small bodies. |
| File | `pck00011.tpc` (text kernel, 128 KB) |
| URL | https://naif.jpl.nasa.gov/pub/naif/generic_kernels/pck/ |
| Underlying source | Archinal, B. A., et al. (2018). Report of the IAU Working Group on Cartographic Coordinates and Rotational Elements: 2015. *Celestial Mechanics and Dynamical Astronomy*, 130:22. https://doi.org/10.1007/s10569-017-9805-5 (cited in the kernel header) |
| Notes | The kernel's comments record body-specific caveats (for example bodies for which the IAU report no longer gives rotational elements). Read them when building the catalog. |

### S-NAIF-GM440: gravitational parameters (Tier 1)

| | |
|---|---|
| Provides | GM in km^3/s^2 for the Sun, the planets, the system barycenters, the Moon, satellites and selected small bodies. |
| File | `gm_de440.tpc` |
| URL | https://naif.jpl.nasa.gov/pub/naif/generic_kernels/pck/ |
| Underlying sources | Per the file header: DE440 values for the Sun, the planets, the barycenters and the Moon (Park et al. 2021). JPL satellite solution release forms for the other satellites. SB441-N16 for the small-body perturbers. |
| Notes | GM values differ slightly between sources. For example, Earth's GM is 398600.43550702266 km^3/s^2 in `gm_de440.tpc` but 398600.435436 in the Horizons object data header (both checked 2026-10-20). Always record which file supplied a value. |

### S-NAIF-LSK: leap seconds kernel (Tier 1)

| | |
|---|---|
| Provides | The TAI - UTC leap second table and the constants SPICE uses for its TDB-TT model. |
| File | `naif0012.tls` (latest in the NAIF directory as of 2026-10-20) |
| URL | https://naif.jpl.nasa.gov/pub/naif/generic_kernels/lsk/ |
| Checksums | No publisher checksum list (the `lsk` directory has no `aa_checksums.txt`; checked 2026-10-20). `naif0012.tls`: 5257 bytes, SHA-256 678e32bdb5a744117a467cd9601cd6b373f0e9bc9bbde1371d5eee39600a039b (retrieved 2026-10-20). |
| Cross-check | IERS Bulletin C (leap second announcements): https://hpiers.obspm.fr/iers/bul/bulc/ |
| Used by | Phase 1 unit test fixtures (`tools/scripts/generate_spice_test_fixtures.py`); from Phase 2, the data pack. |
| Notes | Check Bulletin C when updating. If a new leap second is announced, NAIF releases a new LSK. The kernel's last entry is TAI - UTC = 37 s from 2017-01-01. |

### S-NAIF-MOONPA: high-precision lunar orientation (Tier 1, Phase 6 optional)

| | |
|---|---|
| File | `moon_pa_de440_200625.bpc` (binary PCK, 12 MB) |
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

## 7. Beyond the Solar System (Phase 9, not yet pinned)

| ID | Source | URL | Notes |
|---|---|---|---|
| S-ESA-GAIA | ESA Gaia Archive (DR3; DR4 when released) | https://gea.esac.esa.int/archive/ | Astrometry and photometry. The Gaia Catalogue of Nearby Stars (Gaia Collaboration, Smart et al. 2021) is a candidate starting set. Follow ESA's credit and citation requirements. |
| S-HIP2 | Hipparcos new reduction (van Leeuwen 2007) | via CDS VizieR (catalog I/311) | Bright stars that are saturated in Gaia. |
| S-CDS-SIMBAD | SIMBAD astronomical database | https://simbad.cds.unistra.fr/simbad/ | Cross-identifications and names. |
| S-NASA-EXO | NASA Exoplanet Archive | https://exoplanetarchive.ipac.caltech.edu/ | Planetary Systems tables with uncertainties. |

## 8. Visual assets (rendering only)

Textures and models are not simulation data, but their provenance and licenses are still tracked, in `UniverseSim/Assets/UniverseSim/ATTRIBUTION.md`.

| ID | Source | URL | Notes |
|---|---|---|---|
| S-NASA-SVS-MOON | NASA SVS CGI Moon Kit (LRO-based color and elevation maps) | https://svs.gsfc.nasa.gov/4720 | |
| S-USGS-ASTRO | USGS Astrogeology Science Center planetary maps | https://astrogeology.usgs.gov/ | Returned HTTP 403 to scripted requests on 2026-10-20. Retrieve manually and record the page URL in the attribution file. |

Other mission-derived maps (for example from NASA/JPL/Caltech mission archives) may be added the same way. Record the mission, the product and the license for each.
