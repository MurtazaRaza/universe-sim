# ADR-0002: Core in C# targeting .NET Standard 2.1, consumed by Unity as a prebuilt DLL

| | |
|---|---|
| Status | Accepted |
| Date | 2026-10-20 |

## Context

ADR-0001 requires an engine-agnostic core. The language and packaging decide how easily Unity consumes the core today and how portable it is tomorrow. Constraints:

- Unity 6 (6000.5.1f1) runs C# on its own runtime, and .NET Standard 2.1 is the safe library target.
- The core must be testable outside Unity.
- Likely future hosts include Godot (C#), Unreal or other C++ engines, and possibly the web.
- The project is maintained by a small team plus AI agents, so tooling simplicity matters.

## Decision

1. Write the core in **C#**, targeting **`netstandard2.1`**, with `LangVersion` 9.0, nullable reference types and zero package dependencies.
2. Build the core with the .NET SDK, outside Unity, and copy the resulting DLL, PDB and XML docs into `UniverseSim/Assets/Plugins/UniverseSim.Core/` with a script (`tools/scripts/build-core-for-unity`). The DLL is ignored by git. Its `.meta` file is committed so the GUID and import settings stay stable.
3. Tools, tests and the CLI target the current .NET LTS (`net10.0`) and may use NuGet packages.
4. Tests use **NUnit**, the framework behind the Unity Test Framework, so test idioms are shared.
5. For non-.NET hosts, the planned path is to multi-target the core to `net10.0` and publish it as a NativeAOT shared library with a C ABI. The fallback is a port that reads the documented data pack format.

## Consequences

- Unity consumes the core like any managed plugin. Debugging works through the portable PDB.
- Each core change needs a build and copy step before Unity sees it. The script makes this one command, and an editor menu item can wrap it later (P0-07).
- `LangVersion` 9.0 keeps the source compilable by Unity's compiler, which keeps open the option of compiling the core inside Unity as a local package.
- netstandard2.1 lacks some newer APIs (for example `System.Text.Json` is not built in, and generic math is unavailable). The core avoids JSON entirely (ADR-0005), and the math types are hand-written.
- When Unity's CoreCLR-based runtime becomes available, the target can be revisited (OQ-07).

## Alternatives considered

- **Rust or C++ core with a C ABI, called from Unity through P/Invoke.** The most portable option (Unreal, the web through WASM), but it needs native builds for every platform and architecture, interop marshalling for every API, and harder debugging inside Unity. The cost is high now for a benefit that may never be needed, and NativeAOT gives a similar route later from C#.
- **Core as source inside Unity (a local UPM package with an asmdef).** No copy step, but it puts Unity files (package.json, asmdef, .meta) into the engine-agnostic module, or depends on symlinks, which are fragile on Windows. Kept as a fallback, which the `LangVersion` 9.0 constraint preserves.
- **Python core.** Excellent astronomy libraries, but Unity cannot embed it in a practical way. Python is used only for generating independent reference data (`tools/reference/`).
