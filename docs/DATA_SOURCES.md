# Data Sources Registry

| | |
|---|---|
| Status | Active |
| Last updated | 2026-11-02 |
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
- A file may list mirrors in `data/sources.json`: other servers that hold byte-identical copies of the publisher's file. `fetch` tries them in order only when the file's own URL cannot be downloaded and the lock already pins the file, and a mirror's copy must match the pin exactly, so the data, the source and the pack stay the same. New pins and re-pins always come from the publisher's URL. A mirror is not a new source: the source entry, its tier and its citation do not change.
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
| Used by | The default data pack: triaxial radii (and the derived mean radius) and IAU rotation elements of the v1 bodies; VAL-04 reference orientations; Neptune's reference pressure, from the comment "(Values are for the 1 bar pressure level.)" above `BODY899_RADII` (transcribed in `data/catalog/manual/surface-conditions.json`, ADR-0017). |
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

### S-NAIF-MOONPA: high-precision lunar orientation (Tier 1)

| | |
|---|---|
| Provides | The orientation of the Moon from DE440: the Euler angles of the lunar principal axes frame `MOON_PA_DE440` relative to the ICRF, and the constant rotation from it to the mean Earth/polar axis frame `MOON_ME_DE440_ME421` (aligned with DE421's ME frame). |
| Files | `moon_pa_de440_200625.bpc` (binary PCK type 2, 12.9 MB, 1549-12-31 to 2650-01-25) and `moon_de440_250416.tf` (lunar frame kernel, the current one; it replaced `moon_de440_200625.tf`, which the PCK's comments name, and `moon_pa_de440_220930.tf`, changing only alias frame ID codes) |
| URL | https://naif.jpl.nasa.gov/pub/naif/generic_kernels/pck/ and https://naif.jpl.nasa.gov/pub/naif/generic_kernels/fk/satellites/ |
| Underlying source | Park, R. S., Folkner, W. M., Williams, J. G., Boggs, D. H. (2021). The JPL Planetary and Lunar Ephemerides DE440 and DE441. *The Astronomical Journal*, 161:105. https://doi.org/10.3847/1538-3881/abd414 (cited in both kernels' comments) |
| Checksums | No publisher checksum list (neither directory has an `aa_checksums.txt`). Pinned on 2026-10-22: `moon_pa_de440_200625.bpc` 12,863,488 bytes, SHA-256 60cd55aa401ea2ea97360636f567554bfe4e37bb829f901b4460a455dfaf783f (identical to the copy downloaded on 2026-10-21); `moon_de440_250416.tf` 19,478 bytes, SHA-256 a47c71e9c9f33796bdafb2c9d69a7ee447b6016ecad80f71cd6f3e479f9cf768. |
| Used by | Both packs: the Moon's body-fixed frame `MOON_ME_DE440_ME421` (section ORNT, P6-05, [ADR-0012](adr/0012-lunar-orientation-from-the-binary-pck.md)); VAL-04 reference orientations of the Moon. |
| Notes | The frame kernel's comments put the ME and PA frames about 0.029 degrees apart, and `IAU_MOON` (the IAU model of `pck00011.tpc`) within about 0.0051 degrees of ME over 2000 to 2040; measured over the default pack's span, up to 1.06e-4 rad (ADR-0012). NAIF's server was slow on 2026-10-22: `fetch` timed out twice on the PCK before it completed. |

### S-NAIF-EARTHPCK: Earth orientation, combined historical and long-term predict (Tier 1, optional)

| | |
|---|---|
| Provides | The orientation of the terrestrial frame ITRF93 (PCK class ID 3000, a frame built into SPICE) as Euler angles relative to ECLIPJ2000: precession (IAU 1976), nutation (IAU 1980) with the EOP nutation corrections, rotation through true sidereal time (UT1) and polar motion (the kernel's comments). |
| Files | `earth_1962_260806_2126_combined.bpc` (binary PCK type 2, 31.3 MB: 61 segments of up to 1000 daily records of degree 20, 1962-01-20 to 2126-11-03; optional, used by the moons pack only) |
| URL | https://naif.jpl.nasa.gov/pub/naif/generic_kernels/pck/ (NAIF moves superseded combined files to `a_old_versions/` in that directory, as it did for the 2024 and 2025 editions; update the file's URL in `data/sources.json` when this one moves) |
| Underlying source | The JPL Earth Orientation Parameter file `latest.long` (https://eop.jpl.nasa.gov/eop/latest.long), last datum 2026-08-06, extended by NAIF for the long-term predict (the kernel's comments). |
| Accuracy | From the comments: below 3 microradians up to the last datum and for about 10 weeks after it; after that a low-accuracy predict, estimated at 5 to 6 milliradians, with polar motion and nutation corrections held constant and TAI - UT1R constant. Over that predict interval the comments put it 1.5 to 5.6 milliradians from the `IAU_EARTH` model. |
| Checksums | No publisher checksum list. Pinned on 2026-10-23: 31,318,016 bytes, SHA-256 cc87ad1a495cf598800ba403763d350f087ac0b97da9fec603278a3864c6a53e (identical to a separate download the same day). |
| Used by | The moons pack: the Earth's body-fixed frame ITRF93 over its window, 2000 to 2100 (section ORNT, P6-06, [ADR-0013](adr/0013-earth-orientation-in-the-moons-pack.md)); VAL-04 reference orientations of the Earth in that pack. The default pack keeps `IAU_EARTH`: no Earth PCK covers its span (1849 to 2150). |
| Notes | `earth_latest_high_prec.bpc` (2000 to about three months ahead, replaced at least twice a week under the same name) cannot be pinned and was not used. |

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
| URL | https://nssdc.gsfc.nasa.gov/planetary/factsheet/ (the metric comparison table), `planetfact_notes.html` (its notes), and one sheet per planet (`jupiterfact.html` and so on) |
| Use | Sanity checks and descriptive text. Since 2026-11-02 also a labeled Tier 2 input, for surface conditions only ([ADR-0017](adr/0017-surface-conditions.md)): no Tier 1 source available to the pipeline gives them. |
| Provides | From the comparison table: Mean Temperature (C) for Mercury to Neptune, the Moon and Pluto, and Surface Pressure (bars) for the six bodies the notes call terrestrial (Mercury, Venus, Earth, the Moon, Mars, Pluto); the giants' pressures are "Unknown*". From the Jupiter, Saturn and Uranus sheets: their radii are at the 1 bar level ("Equatorial radius (1 bar level) (km)", the pack's IAU radii). |
| Form | HTML pages; values enter through `data/catalog/manual/surface-conditions.json` (transcribed 2026-11-02, each value as the text of its cell with its unit, converted to K and Pa by the pipeline). |
| Terms | NASA web content; NASA should be acknowledged as the source (NASA Images and Media Usage Guidelines). |
| Used by | The default and moons packs, section SURF (format 1.4): mean temperature, surface pressure and the 1 bar reference pressure of Jupiter, Saturn and Uranus. |
| Notes | Mean temperature is "the average temperature over the whole planet's surface (or for the gas giants at the one bar level)" and "not representative of any given region" (the notes). Each giant's own sheet gives a "Temperature at 1 bar" that differs from the table by up to 2 degrees (Jupiter: 165 K, -108 C, against -110 C); the table is used for all ten bodies alike. The IAU 2015 report (Archinal et al. 2018), the Tier 1 source for the giants' 1 bar level, should replace the sheets for that value once transcribed from a copy (its publisher refuses automated downloads). |

## 7. Sky background

### S-CDS-BSC5: The Bright Star Catalogue, 5th revised edition (Tier 1)

| | |
|---|---|
| Provides | 9110 entries (9096 stars, to about V 6.5): J2000 positions (FK5), proper motions, V magnitudes, B-V and other colors, spectral types, designations. |
| URL | https://cdsarc.cds.unistra.fr/ftp/V/50/ (CDS catalog V/50) |
| Mirror | https://vizier.cfa.harvard.edu/ftp/cats/V/50/, the VizieR mirror at the Center for Astrophysics, Harvard and Smithsonian (listed in VizieR's mirrors page). Both files there matched the pins byte for byte on 2026-11-02, when CDS could not be reached from the Windows development machine (section 2). |
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

Textures and models are not simulation data, but their provenance and licenses are still tracked: the sources here and in `data/sources.json` (pinned in the lock file like every input, as optional files), and each asset in `UniverseSim/Assets/UniverseSim/ATTRIBUTION.md`, which `usim-pipeline textures` generates. The surface maps ([ADR-0014](adr/0014-surface-map-textures.md)) are listed per body in `data/catalog/textures.json`. Their sources are about 4.8 GB; `tools/scripts/fetch-texture-sources.sh` downloads and pins them.

### S-USGS-ASTRO: USGS Astrogeology global mosaics (Tier 1, visual)

| | |
|---|---|
| Provides | Global image mosaics of planets and moons from NASA and partner missions, as GeoTIFFs with ISIS cube labels (projection) and PDS3 labels (byte layout). |
| Files | 15 mosaics (Mercury, Mars, Phobos, Io, Europa, Ganymede, Callisto, Enceladus, Tethys, Dione, Rhea, Iapetus, Triton, Charon, Pluto; 17 MB to 1.9 GB, about 4.6 GB in all) with their two labels, and the DLR basemap of Mimas in `Cassini_DLR_Mimas.zip`. |
| URL | https://astrogeology.usgs.gov/search (product pages); files at https://asc-pds-services.s3.us-west-2.amazonaws.com/mosaic/ (where planetarymaps.usgs.gov/mosaic/ redirects). |
| Checksums | Each mosaic has a publisher MD5 file next to it (`<file>.md5`); the labels and the Mimas archive have none and are pinned by SHA-256. |
| Terms | USGS Copyrights and Credits (read 2026-10-23): USGS-produced data are in the U.S. public domain; credit is requested. The products' FGDC metadata ask users to cite the authors (originators listed per map in ATTRIBUTION.md). |
| Notes | Scripted requests worked on 2026-10-23 (they returned HTTP 403 on 2026-10-20). Conventions differ per product (longitude direction, the projection center, planetocentric or planetographic latitude, a width slightly over 360 degrees); the pipeline reads them from the ISIS labels. |

### S-NASA-SVS-MOON: NASA SVS CGI Moon Kit (Tier 1, visual)

| | |
|---|---|
| Provides | The color map of the Moon built from the LROC WAC Hapke normalized mosaic (643, 566 and 415 nm as red, green and blue, adjusted toward human vision), 2025 version. |
| Files | `lroc_color_16bit_srgb_4k.tif` (4096 x 2048, 16-bit sRGB, 61.9 MB) |
| URL | https://svs.gsfc.nasa.gov/4720 |
| Terms | Public domain unless noted (SVS help page, read 2026-10-23); credit "NASA's Scientific Visualization Studio" as the page asks. |

### S-NASA-EO-BMNG: Blue Marble: Next Generation (Tier 1, visual)

| | |
|---|---|
| Provides | A cloud-free true color composite of the Earth from MODIS for each month of 2004, with shading for topography and bathymetry. The August map is used. |
| Files | `world.topo.bathy.200408.3x5400x2700_geo.tif` (GeoTIFF, WGS 84, 19.8 MB) |
| URL | https://science.nasa.gov/earth/earth-observatory/blue-marble-next-generation/base-topography-bathymetry/ |
| Terms | The Blue Marble page asks anyone using or republishing it to credit "NASA Earth Observatory" (read 2026-10-23); NASA's media guidelines apply. |

### S-NASA-PHOTOJOURNAL: Cassini's Best Maps of Jupiter, PIA07782 (Tier 1, visual)

| | |
|---|---|
| Provides | A cylindrical color map of Jupiter from Cassini ISS images of 2000-12-11 and 12, in planetocentric latitude, "colors close to those the human eye would see" (the page). |
| Files | `PIA07782.tif` (3601 x 1801, 19.5 MB) |
| URL | https://science.nasa.gov/photojournal/cassinis-best-maps-of-jupiter-cylindrical-map/ |
| Terms | NASA media guidelines (read 2026-10-23): generally not subject to U.S. copyright, NASA acknowledged as the source; credit NASA/JPL/Space Science Institute. |

Other mission-derived maps may be added the same way. Record the mission, the product and the license for each.

## 10. Sky cues (rendering only)

Qualitative colors of skies and twilight that the stylized style draws (A-07, B-06, [ADR-0018](adr/0018-atmospheres-and-sky-cues.md)). They enter through `data/catalog/sky-cues.json`, each as a hue from a fixed vocabulary with the source's sentence that states it, and `usim-pipeline sky-cues` writes them to `UniverseSim/Assets/UniverseSim/Data/SkyCues.json`, where the ship and info panels cite them. Whether a body has an atmosphere at all is not a cue: the core derives it from the pack's surface conditions (S-NSSDC-FACT and S-NAIF-PCK11, ADR-0017). These sources have no files to pin: only short quotations are kept, with the page and the date read.

### S-NASA-PJ-SKIES: Mars Pathfinder sky images on NASA Photojournal (Tier 1)

| | |
|---|---|
| Provides | PIA01547, "True Color of Mars - Pathfinder Sol 24 at 4 PM" (added 1999-06-22): "The brownish gray sky as it would be seen by an observer on Mars in this four-frame, true color mosaic taken on sol 24 (at approximately 1610 LST)." PIA00917, "Color Variations in the Sky at Sunset" (added 1997-08-28): "The blue color only becomes apparent near sunrise and sunset, when the light has to pass through the largest amount of dust." |
| URL | https://science.nasa.gov/photojournal/true-color-of-mars-pathfinder-sol-24-at-4-pm and https://science.nasa.gov/photojournal/color-variations-in-the-sky-at-sunset (the photojournal.jpl.nasa.gov catalog pages redirect there), read 2026-11-02 |
| Credit | NASA/JPL |
| Used by | Mars's `daytime_sky` (brownish gray) and `twilight` (blue) cues. |
| Notes | Curiosity saw the same blue sunset (PIA19400, NASA/JPL-Caltech/MSSS/Texas A&M Univ.). PIA00917 also says the dust "absorbs blue light, giving the sky its red color"; the cue keeps PIA01547's true color words. |

### S-NASA-EO-ISS: astronaut photographs on NASA Earth Observatory (Tier 1)

| | |
|---|---|
| Provides | "Sundown and Lights Up Over the U.S. Northeast" (Image of the Day 2024-05-12, astronaut photograph ISS070-E-76065 of 2024-01-17): "As the Sun dips below the curvature of Earth, the varying layers of the atmosphere appear as different hues in this oblique photograph of Earth's limb." and "The troposphere, the lowest layer of the atmosphere, appears red and yellow." |
| URL | https://science.nasa.gov/earth/earth-observatory/sundown-and-lights-up-over-the-us-northeast-152802, read 2026-11-02 |
| Credit | ISS Crew Earth Observations Facility and the Earth Science and Remote Sensing Unit, NASA Johnson Space Center; caption by Wilfredo García López (Jacobs-JETS II Contract at NASA-JSC) |
| Used by | The Earth's `twilight` cue (red and yellow). |

### S-PIETERS-1986: The color of the surface of Venus (Tier 1, peer reviewed)

| | |
|---|---|
| Provides | The abstract: "Multispectral images of the basaltic surface of Venus obtained by Venera 13 were processed to remove the effects of orange-colored incident radiation resulting from interactions with the thick Venusian atmosphere." |
| Citation | Pieters, C. M., Head, J. W., Patterson, W., Pratt, S., Garvin, J. (1986). The color of the surface of Venus. *Science*, 234, 1986-12-12. |
| URL | https://ntrs.nasa.gov/citations/19870039592 (NASA Technical Reports Server record with the abstract, read 2026-11-02; it gives no pages or DOI, so none are recorded here) |
| Used by | Venus's `daytime_sky` cue (orange: the daylight at the surface). |

### S-NASA-SPACEPLACE: "Why Is the Sky Blue?" (Tier 2)

| | |
|---|---|
| Provides | "This is why we see a blue sky most of the time." |
| URL | https://spaceplace.nasa.gov/blue-sky/en/ (page last updated 2026-07-31), read 2026-11-02 |
| Used by | The Earth's `daytime_sky` cue (blue), labeled Tier 2 in the panels: no Tier 1 statement of the daytime sky's color was found in an accessible form. |
| Notes | The same page describes Mars's sky ("During the daytime, the Martian sky takes on an orange or reddish color.") from Mars Pathfinder images; the Tier 1 captions above are used instead. |
