# ADR-0007: Reading values from text kernels

| | |
|---|---|
| Status | Accepted |
| Date | 2026-10-20 |

## Context

P2-04 planned to read constants from NAIF text kernels through the SpiceyPy kernel pool (`furnsh`, `gdpool`). The acceptance criteria require the pack's catalog values to "match the source kernels exactly". A text kernel stores each value as decimal text, for example `BODY399_GM = ( 3.9860043550702266D+05 )`.

Measured on 2026-10-20 with CSPICE N0067 (SpiceyPy 8.2.0): CSPICE's text kernel parser does not round correctly. Compared with the correctly rounded double of each value's text (Python `float()`):

| Kernel | Numeric values | Read differently by CSPICE | Largest difference |
|---|---|---|---|
| `gm_de440.tpc` | 227 | 36 | 5 ulps (`BODY5_GM`) |
| `pck00011.tpc` | 2896 | 9 | 1 ulp |
| `naif0012.tls` | 5 | 0 | 0 |

For the v1 bodies, 11 of 20 GM values and 6 of 495 PCK values are affected. Earth's GM, for example, reads as 398600.4355070226 in CSPICE but 398600.43550702266 is the double nearest the published text. The differences are physically irrelevant (relative 1e-16 to 6e-16, far below the uncertainty of any of these values), but they decide what "exactly" means and they would make the pack depend on one parser's rounding.

## Decision

1. The pack holds the **correctly rounded value of the text written in the kernel**. The pipeline parses text kernels itself (`usim_pipeline.kernels.TextKernel`, following the Kernel Required Reading: `\begindata` sections, `=` and `+=`, value lists across lines, D and E exponents, @-dates).
2. Every value is **cross-checked against CSPICE's kernel pool**: the variable must exist in both with the same number of values, and each value must agree within 16 ulps (measured maximum: 5). Any parser mistake fails the build.
3. The build reports per kernel how many values CSPICE reads differently and by how much.
4. `data/reference/pack_kernel_values.csv` records each value's text as written; the core's validation test parses that text with `strtod` and compares with the pack bit for bit. This check does not depend on the pipeline's parser.

## Consequences

- "The pack matches the source" has a precise, parser-independent meaning: the value is the double nearest the published decimal text.
- Comparisons against CSPICE that use kernel constants (for example VAL-04 orientations from `pxform`) start from inputs that can differ by a few ulps. The effect is many orders of magnitude below the validation thresholds.
- The Phase 1 unit fixtures read `naif0012.tls` through the kernel pool; CSPICE reads that file exactly, so those values are unchanged.
- The pipeline carries a small parser (about 80 lines) that must follow the kernel format. The cross-check confines any error to a failed build, never to wrong data.

## Alternatives considered

- **Use the kernel pool values as planned.** Simpler, and identical to the validation oracle's inputs, but the stored values would differ from the published text by up to 5 ulps, and "exact" would mean "as CSPICE happens to round".
- **Parse the text without cross-checking.** Independent of CSPICE, but a parser bug could silently produce wrong values.
