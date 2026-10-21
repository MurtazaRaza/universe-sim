# ADR-0002: Core in C++20 with a C ABI, consumed by Unity as a native plugin

| | |
|---|---|
| Status | Accepted |
| Date | 2026-10-20 |
| Decided by | Project owner (replaced an initial C# proposal before it was adopted) |

## Context

ADR-0001 requires an engine-agnostic core. The language and packaging decide how hosts consume the core and how far it can travel. Constraints:

- Unity 6 (6000.5.1f1) is the first host. Its scripting language is C#, and it loads native libraries through P/Invoke.
- Likely future hosts include Unreal (C++), Godot (C++ GDExtension or C#), custom C++ engines, and possibly the web.
- The core must be testable without any engine.
- Numerical results must be deterministic (SPEC NFR-04).

## Decision

1. **Language and build.** The core is written in **C++20** using only the standard library. It is built with **CMake (3.25 or newer)** using the presets in `core/CMakePresets.json`, with Ninja as the generator.
2. **Two library targets.**
   - `usim_core` (static library, namespace `usim`): the C++ API. It is used directly by the CLI, the tests and any C++ host that compiles or links the core (for example an Unreal module).
   - `universesim` (shared library): a **stable C ABI** declared in `core/capi/include/usim/usim.h`, for hosts in other languages. It is named without a `lib` prefix on every platform (`universesim.dylib`, `.dll`, `.so`).
3. **C ABI rules.**
   - Only plain C types cross the boundary: fixed-width integers, `double`, and structs of those.
   - No exception crosses the boundary.
   - Handles are opaque.
   - Arrays are caller-owned buffers with an explicit capacity.
   - Strings are UTF-8, and the library documents the lifetime of each string it returns.
   - `USIM_ABI_VERSION` increments on any breaking change. Hosts check `usim_abi_version()` before calling anything else.
   - Only `USIM_API` symbols are exported; everything else has hidden visibility.
4. **Unity integration.**
   - C# P/Invoke bindings live in the Unity project (`Assets/UniverseSim/Runtime/Native/`).
   - `tools/scripts/build-core-for-unity.sh` builds the plugin and copies it to `Assets/UniverseSim/Plugins/<platform>/`.
   - Plugin binaries and their `.meta` files are not committed. An editor `AssetPostprocessor` (`NativePluginImportSettings.cs`) enforces their import settings.
   - On macOS the plugin is a universal binary (arm64 and x86_64) with deployment target 12.0. This matches the Unity 6000.5 macOS player, whose `UnityPlayer.dylib` reports `minos 12.0`.
5. **Determinism flags.**
   - No fast-math.
   - FMA contraction is disabled (`-ffp-contract=off`; MSVC `/fp:precise`).
   - Warnings are errors.
   - The MSVC runtime is linked statically, so the plugin needs no redistributable.
6. **Tests.**
   - C++ tests use **Catch2 v3**, fetched by CMake and pinned by version and SHA-256.
   - The Unity bindings are tested with the Unity Test Framework (EditMode and PlayMode).
7. **Tooling.** Tools that never ship (the data pipeline and the reference-data generators) are written in Python. See ADR-0005.

## Consequences

- **Portability.**
  - Any engine that can call C (practically all of them) can host the core.
  - C++ engines can compile it directly.
  - WebAssembly through Emscripten and static linking for mobile (iOS `__Internal`) remain possible.
- **Per-platform builds.** Each platform and architecture needs its own native binary and build preset. Only macOS exists so far; Windows and Linux are plan tasks.
- **Binding upkeep.** Every C ABI function needs a matching C# declaration in the Unity bindings. Struct layouts must match exactly, so the C ABI will expose struct sizes, and the bindings will check them at startup.
- **Editor workflow.** The Unity Editor cannot unload a native plugin once it has used it. After rebuilding the core, restart the Editor. A development-time loader that loads a uniquely named copy (hot reload) can remove this friction later (OQ-07).
- **Debugging across the boundary.** Debugging into the core from Unity means attaching a native debugger (lldb, Xcode or Visual Studio) to the Unity process. Most core logic should be debugged through the C++ tests and the CLI instead.
- **Safety.** C++ has no memory safety. Mitigations: warnings as errors, sanitizer builds (AddressSanitizer and UndefinedBehaviorSanitizer presets are a plan task), and a small, validated C ABI surface.

## Alternatives considered

- **C# targeting .NET Standard 2.1, consumed by Unity as a managed DLL** (the initial proposal). It gives the simplest Unity integration and debugging. Portability, however, depends on .NET hosts, and reaching C++ engines would need NativeAOT. Not chosen, because the project owner prefers a native core that any engine can use.
- **Rust with a C ABI.** It gives memory safety with the same C ABI story. But C++ is the native language of Unreal and most engines, and the reference astronomy code (CSPICE, SOFA) is C or C-compatible.
- **Plain C.** It gives the simplest ABI, but too little expressiveness for the math, time and frame types. The C ABI layer already gives C compatibility at the boundary.
- **Core compiled inside Unity (IL2CPP or Burst).** That ties the core to Unity and contradicts ADR-0001.
